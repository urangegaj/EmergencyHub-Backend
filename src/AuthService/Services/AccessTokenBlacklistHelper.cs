using System.IdentityModel.Tokens.Jwt;
using JwtBlacklist;
using Microsoft.Extensions.Logging;

namespace AuthService.Services;

internal static class AccessTokenBlacklistHelper
{
    public static async Task TryBlacklistAsync(
        string? accessToken,
        JwtBlacklistService blacklist,
        TokenService tokens,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return;

        if (!tokens.TryValidateAccessToken(accessToken, out var jwt) || jwt is null)
        {
            logger.LogWarning("Logout: access token failed signature or claim validation, skipping blacklist");
            return;
        }

        var jti = jwt.Id;
        if (string.IsNullOrEmpty(jti))
        {
            logger.LogWarning("Logout: access token missing jti claim, skipping blacklist");
            return;
        }

        var remaining = jwt.ValidTo - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            logger.LogDebug("Logout: access token already expired (jti={Jti}), skipping blacklist", jti);
            return;
        }

        await blacklist.BlacklistAsync(jti, remaining, cancellationToken);
    }
}
