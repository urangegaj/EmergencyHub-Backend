using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using Shared.Auth;

namespace AuthService.Services;

internal static class AccessTokenBlacklistHelper
{
    public static async Task TryBlacklistAsync(
        string? accessToken,
        JwtBlacklistService blacklist,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return;

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(accessToken))
        {
            logger.LogWarning("Logout: malformed access token, skipping blacklist");
            return;
        }

        JwtSecurityToken jwt;
        try
        {
            jwt = handler.ReadJwtToken(accessToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Logout: could not read access token, skipping blacklist");
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
