using Grpc.Core;

namespace AuthService.Features.UpdateUser;

public interface IUpdateUserHandler
{
    Task<UpdateUserResponse> HandleAsync(UpdateUserRequest request, ServerCallContext context);
}
