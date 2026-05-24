using AuthService.Features.Shared;

using AuthService.Data;
using AuthService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace AuthService.Features.CreateUser;

public class CreateUserHandler(AuthDbContext db) : ICreateUserHandler
{
    public async Task<CreateUserResponse> HandleAsync(CreateUserRequest request, ServerCallContext context)
    {
        var cityId = AuthMapper.ExtractCityId(context);

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
}
