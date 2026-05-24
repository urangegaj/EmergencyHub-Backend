using System.Security.Cryptography;
using AuthService.Data;
using AuthService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JwtBlacklist;
using Shared.Auth;
using Shared.Enums;
using StackExchange.Redis;

namespace AuthService.Services;

public class AuthGrpcService(
    AuthDbContext db,
    IConnectionMultiplexer redis,
    TokenService tokens,
    JwtBlacklistService blacklist,
    ILogger<AuthGrpcService> logger) : Auth.AuthBase
{
    private static readonly TimeSpan RefreshTokenTtl = TimeSpan.FromDays(7);
    private readonly IDatabase _cache = redis.GetDatabase();

    public override async Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
    {
        if (await db.Users.AnyAsync(u => u.Email == request.Email))
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Email already in use."));

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown role: {request.Role}"));

        DepartmentType? department = null;
        if (role == UserRole.Responder)
        {
            if (!request.HasDepartment || string.IsNullOrWhiteSpace(request.Department))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Department is required for Responders."));

            if (!Enum.TryParse<DepartmentType>(request.Department, ignoreCase: true, out var dept))
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown department: {request.Department}"));

            department = dept;
        }

        var dbRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == request.Role)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Role '{request.Role}' not seeded."));

        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "city_id must be a valid GUID."));

        var user = new User
        {
            CityId = cityId,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = dbRole.Id,
            Department = department
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.HasPhone ? request.Phone : null
        });
        await db.SaveChangesAsync();

        return new RegisterResponse { UserId = user.Id.ToString() };
    }

    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Invalid credentials."));

        if (!user.IsActive)
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Account is deactivated."));

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid credentials."));

        var refreshToken = GenerateRefreshToken();
        await _cache.StringSetAsync(RefreshKey(refreshToken), user.Id.ToString(), RefreshTokenTtl);

        var response = new LoginResponse
        {
            AccessToken = tokens.IssueAccessToken(user),
            RefreshToken = refreshToken,
            UserId = user.Id.ToString(),
            CityId = user.CityId.ToString(),
            Role = user.Role.Name
        };

        if (user.Department.HasValue)
            response.Department = user.Department.Value.ToString();

        return response;
    }

    public override async Task<LoginResponse> Refresh(RefreshRequest request, ServerCallContext context)
    {
        var key = RefreshKey(request.RefreshToken);
        var stored = await _cache.StringGetAsync(key);

        if (!stored.HasValue || !Guid.TryParse(stored, out var userId))
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid or expired refresh token."));

        await _cache.KeyDeleteAsync(key);

        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        var newRefreshToken = GenerateRefreshToken();
        await _cache.StringSetAsync(RefreshKey(newRefreshToken), user.Id.ToString(), RefreshTokenTtl);

        var response = new LoginResponse
        {
            AccessToken = tokens.IssueAccessToken(user),
            RefreshToken = newRefreshToken,
            UserId = user.Id.ToString(),
            CityId = user.CityId.ToString(),
            Role = user.Role.Name
        };

        if (user.Department.HasValue)
            response.Department = user.Department.Value.ToString();

        return response;
    }

    public override async Task<LogoutResponse> Logout(LogoutRequest request, ServerCallContext context)
    {
        await _cache.KeyDeleteAsync(RefreshKey(request.RefreshToken));

        var accessToken = request.HasAccessToken ? request.AccessToken : null;
        await AccessTokenBlacklistHelper.TryBlacklistAsync(
            accessToken, blacklist, tokens, logger, context.CancellationToken);

        return new LogoutResponse();
    }

    public override async Task<UserResponse> GetUser(GetUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_id."));

        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        return MapUser(user);
    }

    public override async Task<ListUsersResponse> ListDepartmentUsers(
        ListDepartmentUsersRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.CityId, out var cityId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid city_id."));

        if (!Enum.TryParse<DepartmentType>(request.Department, ignoreCase: true, out var department))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown department: {request.Department}."));

        var users = await db.Users
            .Include(u => u.Role)
            .Where(u => u.CityId == cityId
                        && u.Department == department
                        && u.Role.Name == nameof(UserRole.Responder))
            .ToListAsync(context.CancellationToken);

        var response = new ListUsersResponse();
        response.Users.AddRange(users.Select(MapUser));
        return response;
    }

    public override async Task<PagedUsersResponse> ListUsers(ListUsersRequest request, ServerCallContext context)
    {
        var cityId = ExtractCityId(context);

        var query = db.Users
            .Include(u => u.Role)
            .Include(u => u.Profile)
            .Where(u => u.CityId == cityId);

        if (!string.IsNullOrEmpty(request.Role))
            query = query.Where(u => u.Role.Name == request.Role);

        if (!string.IsNullOrEmpty(request.Department)
            && Enum.TryParse<DepartmentType>(request.Department, ignoreCase: true, out var dept))
            query = query.Where(u => u.Department == dept);

        var total = await query.CountAsync(context.CancellationToken);

        var page = request.Page > 0 ? request.Page : 1;
        var pageSize = request.PageSize > 0 ? request.PageSize : 20;

        var users = await query
            .OrderBy(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(context.CancellationToken);

        var response = new PagedUsersResponse
        {
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
        response.Users.AddRange(users.Select(MapAdminUser));
        return response;
    }

    public override async Task<CreateUserResponse> CreateUser(CreateUserRequest request, ServerCallContext context)
    {
        var cityId = ExtractCityId(context);

        if (await db.Users.AnyAsync(u => u.Email == request.Email, context.CancellationToken))
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Email already in use."));

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown role: {request.Role}"));

        DepartmentType? department = null;
        if (role == UserRole.Responder)
        {
            if (!request.HasDepartment || string.IsNullOrWhiteSpace(request.Department))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Department is required for Responders."));

            if (!Enum.TryParse<DepartmentType>(request.Department, ignoreCase: true, out var parsedDept))
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown department: {request.Department}"));

            department = parsedDept;
        }

        var dbRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == request.Role, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Role '{request.Role}' not seeded."));

        var user = new User
        {
            CityId = cityId,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = dbRole.Id,
            Department = department,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(context.CancellationToken);

        db.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.HasPhone ? request.Phone : null
        });
        await db.SaveChangesAsync(context.CancellationToken);

        return new CreateUserResponse { UserId = user.Id.ToString() };
    }

    public override async Task<UpdateUserResponse> UpdateUser(UpdateUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_id."));

        var user = await db.Users
            .Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == userId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        if (request.HasFirstName && user.Profile is not null)
            user.Profile.FirstName = request.FirstName;

        if (request.HasLastName && user.Profile is not null)
            user.Profile.LastName = request.LastName;

        if (request.HasPhone && user.Profile is not null)
            user.Profile.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone;

        if (request.HasDepartment)
        {
            if (string.IsNullOrWhiteSpace(request.Department))
                user.Department = null;
            else if (Enum.TryParse<DepartmentType>(request.Department, ignoreCase: true, out var dept))
                user.Department = dept;
            else
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown department: {request.Department}"));
        }

        await db.SaveChangesAsync(context.CancellationToken);
        return new UpdateUserResponse();
    }

    public override async Task<DeactivateUserResponse> DeactivateUser(DeactivateUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_id."));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        user.IsActive = false;
        await db.SaveChangesAsync(context.CancellationToken);
        return new DeactivateUserResponse();
    }

    public override async Task<AssignRoleResponse> AssignRole(AssignRoleRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_id."));

        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var newRole))
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown role: {request.Role}"));

        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        DepartmentType? department = null;
        if (newRole == UserRole.Responder)
        {
            if (!request.HasDepartment || string.IsNullOrWhiteSpace(request.Department))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Department is required when assigning Responder role."));

            if (!Enum.TryParse<DepartmentType>(request.Department, ignoreCase: true, out var parsedDept))
                throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown department: {request.Department}"));

            department = parsedDept;
        }

        var dbRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == request.Role, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Role '{request.Role}' not seeded."));

        user.RoleId = dbRole.Id;
        user.Department = department;

        await db.SaveChangesAsync(context.CancellationToken);
        return new AssignRoleResponse();
    }

    private static UserResponse MapUser(User user)
    {
        var response = new UserResponse
        {
            UserId = user.Id.ToString(),
            Email = user.Email,
            Role = user.Role.Name
        };

        if (user.Department.HasValue)
            response.Department = user.Department.Value.ToString();

        return response;
    }

    private static AdminUserResponse MapAdminUser(User user)
    {
        var r = new AdminUserResponse
        {
            UserId = user.Id.ToString(),
            Email = user.Email,
            Role = user.Role.Name,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt.ToString("O"),
            FirstName = user.Profile?.FirstName ?? "",
            LastName = user.Profile?.LastName ?? ""
        };
        if (user.Department.HasValue) r.Department = user.Department.Value.ToString();
        if (user.Profile?.Phone is not null) r.Phone = user.Profile.Phone;
        return r;
    }

    private static Guid ExtractCityId(ServerCallContext context)
    {
        var raw = context.RequestHeaders.GetValue("city_id")
            ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "city_id metadata missing."));
        return Guid.TryParse(raw, out var id) ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "city_id is not a valid GUID."));
    }

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string RefreshKey(string token) => $"refresh:{token}";
}
