using System.IdentityModel.Tokens.Jwt;
using JwtBlacklist;
using Microsoft.AspNetCore.Authorization;

namespace Gateway.Middleware;

/// <summary>
/// After JWT authentication, rejects requests whose access token jti is in the Redis blacklist.
/// </summary>
/// <remarks>
/// <para>Fails closed when Redis is unavailable: every authenticated request returns 401.</para>
/// <para>
/// Availability tradeoff: a Redis outage blocks all logged-in Gateway traffic rather than
/// allowing potentially revoked tokens through until Redis recovers. This prioritizes
/// security over availability; consider a read replica or short-lived local cache if
/// that becomes unacceptable in production.
/// </para>
/// </remarks>
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
                await WriteUnauthorizedAsync(context, "Token is missing required identifier.");
                return;
            }

            try
            {
                if (await blacklist.IsBlacklistedAsync(jti, context.RequestAborted))
                {
                    logger.LogWarning("Rejected request with blacklisted token (jti={Jti})", jti);
                    await WriteUnauthorizedAsync(context, "Token has been revoked.");
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Redis blacklist check failed for jti={Jti}", jti);
                await WriteUnauthorizedAsync(context, "Unable to validate token revocation status.");
                return;
            }
        }

        await next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "text/plain";
        await context.Response.WriteAsync(message);
    }

    private static bool IsAllowAnonymous(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }
}
