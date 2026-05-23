using System.Text.Json;
using StackExchange.Redis;

namespace Shared.Redis;

public sealed class RedisCache(IConnectionMultiplexer redis) : IRedisCache
{
    private readonly IDatabase _db = redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(key);
        return val.HasValue ? JsonSerializer.Deserialize<T>(val!) : default;
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        => await _db.StringSetAsync(key, JsonSerializer.Serialize(value), ttl);

    public async Task InvalidateAsync(string key, CancellationToken ct = default)
        => await _db.KeyDeleteAsync(key);
}
