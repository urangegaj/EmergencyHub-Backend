using AuthService.Features.Shared;

using AuthService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Features.DeactivateUser;

public class DeactivateUserHandler(AuthDbContext db) : IDeactivateUserHandler
{
    public async Task<DeactivateUserResponse> HandleAsync(DeactivateUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_id."));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        if (!user.IsActive)
            throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        user.IsActive = false;
        await db.SaveChangesAsync(context.CancellationToken);
        return new DeactivateUserResponse();
    }
}
