using Grpc.Core;

namespace AuthService.Features.CreateUser;

public interface ICreateUserHandler
{
    Task<CreateUserResponse> HandleAsync(CreateUserRequest request, ServerCallContext context);
}
