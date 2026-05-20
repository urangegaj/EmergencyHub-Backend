using System.Security.Cryptography;
using AuthService.Data;
using AuthService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using StackExchange.Redis;

namespace AuthService.Services;

public class AuthGrpcService(AuthDbContext db, IConnectionMultiplexer redis, TokenService tokens) : Auth.AuthBase
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
        return new LogoutResponse();
    }

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string RefreshKey(string token) => $"refresh:{token}";
}
