using Grpc.Core;

namespace AuthService.Features.RefreshToken;

public interface IRefreshTokenHandler
{
    Task<LoginResponse> HandleAsync(RefreshRequest request, ServerCallContext context);
}
