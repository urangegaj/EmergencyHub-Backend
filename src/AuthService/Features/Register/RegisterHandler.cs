using AuthService.Features.Shared;

using AuthService.Data;
using AuthService.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace AuthService.Features.Register;

public class RegisterHandler(AuthDbContext db) : IRegisterHandler
{
    public async Task<RegisterResponse> HandleAsync(RegisterRequest request, ServerCallContext context)
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
}
