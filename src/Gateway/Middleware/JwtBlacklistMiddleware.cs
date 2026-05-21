using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Shared.Auth;

namespace Gateway.Middleware;

/// <summary>
/// After JWT authentication, rejects requests whose access token jti is in the Redis blacklist.
/// Fails closed when Redis is unavailable (401) so revoked tokens cannot slip through during outages.
/// </summary>
public class JwtBlacklistMiddleware(
    RequestDelegate next,
    JwtBlacklistService blacklist,
    ILogger<JwtBlacklistMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && !IsAllowAnonymous(context))
        {
            var jti = context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (string.IsNullOrEmpty(jti))
            {
                logger.LogWarning("Authenticated request missing jti claim");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Token is missing required identifier.");
                return;
            }

            try
            {
                if (await blacklist.IsBlacklistedAsync(jti, context.RequestAborted))
                {
                    logger.LogWarning("Rejected request with blacklisted token (jti={Jti})", jti);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Token has been revoked.");
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Redis blacklist check failed for jti={Jti}", jti);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unable to validate token revocation status.");
                return;
            }
        }

        await next(context);
    }

    private static bool IsAllowAnonymous(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }
}
