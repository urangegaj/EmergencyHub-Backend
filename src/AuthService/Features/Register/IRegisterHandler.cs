using Grpc.Core;

namespace AuthService.Features.Register;

public interface IRegisterHandler
{
    Task<RegisterResponse> HandleAsync(RegisterRequest request, ServerCallContext context);
}
