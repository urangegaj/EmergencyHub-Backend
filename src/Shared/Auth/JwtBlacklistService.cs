using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shared.Auth;

/// <summary>
/// Stores and checks revoked access-token JTIs in Redis.
/// Gateway blacklist checks fail-closed: if Redis is unavailable, requests are rejected.
/// </summary>
public class JwtBlacklistService(IConnectionMultiplexer redis, ILogger<JwtBlacklistService> logger)
{
    private readonly IDatabase _cache = redis.GetDatabase();

    public async Task BlacklistAsync(string jti, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (ttl <= TimeSpan.Zero)
            return;

        await _cache.StringSetAsync(
            JwtBlacklistConstants.KeyForJti(jti),
            JwtBlacklistConstants.RevokedValue,
            ttl);

        logger.LogInformation("Access token blacklisted (jti={Jti}, ttlSeconds={TtlSeconds})", jti, (int)ttl.TotalSeconds);
    }

    public async Task<bool> IsBlacklistedAsync(string jti, CancellationToken cancellationToken = default)
    {
        return await _cache.KeyExistsAsync(JwtBlacklistConstants.KeyForJti(jti));
    }
}
