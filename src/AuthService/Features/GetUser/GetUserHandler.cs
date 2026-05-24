using AuthService.Features.Shared;

using AuthService.Data;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Features.GetUser;

public class GetUserHandler(AuthDbContext db) : IGetUserHandler
{
    public async Task<UserResponse> HandleAsync(GetUserRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.UserId, out var userId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid user_id."));

        var user = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "User not found."));

        return AuthMapper.MapUser(user);
    }
}
