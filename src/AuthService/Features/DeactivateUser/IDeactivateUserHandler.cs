using Grpc.Core;

namespace AuthService.Features.DeactivateUser;

public interface IDeactivateUserHandler
{
    Task<DeactivateUserResponse> HandleAsync(DeactivateUserRequest request, ServerCallContext context);
}
