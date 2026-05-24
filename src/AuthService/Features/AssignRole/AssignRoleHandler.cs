using AuthService.Features.Shared;

using AuthService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace AuthService.Features.AssignRole;

public class AssignRoleHandler(AuthDbContext db) : IAssignRoleHandler
{
    public async Task<AssignRoleResponse> HandleAsync(AssignRoleRequest request, ServerCallContext context)
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
}
