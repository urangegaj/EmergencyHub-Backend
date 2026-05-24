using AuthService.Features.Shared;

using AuthService.Services;
using Grpc.Core;
using JwtBlacklist;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AuthService.Features.Logout;

public class LogoutHandler(
    IConnectionMultiplexer redis,
    JwtBlacklistService blacklist,
    TokenService tokens,
    ILogger<LogoutHandler> logger) : ILogoutHandler
{
    private readonly IDatabase _cache = redis.GetDatabase();

    public async Task<LogoutResponse> HandleAsync(LogoutRequest request, ServerCallContext context)
    {
        await _cache.KeyDeleteAsync(AuthMapper.RefreshKey(request.RefreshToken));

        var accessToken = request.HasAccessToken ? request.AccessToken : null;
        await AccessTokenBlacklistHelper.TryBlacklistAsync(
            accessToken, blacklist, tokens, logger, context.CancellationToken);

        return new LogoutResponse();
    }
}
