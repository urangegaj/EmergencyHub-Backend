using AuthService.Features.Shared;

using AuthService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;

namespace AuthService.Features.UpdateUser;

public class UpdateUserHandler(AuthDbContext db) : IUpdateUserHandler
{
    public async Task<UpdateUserResponse> HandleAsync(UpdateUserRequest request, ServerCallContext context)
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
}
