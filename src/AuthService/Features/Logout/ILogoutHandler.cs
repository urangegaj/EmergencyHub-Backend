using Grpc.Core;

namespace AuthService.Features.Logout;

public interface ILogoutHandler
{
    Task<LogoutResponse> HandleAsync(LogoutRequest request, ServerCallContext context);
}
