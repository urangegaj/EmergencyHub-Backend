using Grpc.Core;

namespace AuthService.Features.Login;

public interface ILoginHandler
{
    Task<LoginResponse> HandleAsync(LoginRequest request, ServerCallContext context);
}
