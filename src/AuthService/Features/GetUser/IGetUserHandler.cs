using Grpc.Core;

namespace AuthService.Features.GetUser;

public interface IGetUserHandler
{
    Task<UserResponse> HandleAsync(GetUserRequest request, ServerCallContext context);
}
