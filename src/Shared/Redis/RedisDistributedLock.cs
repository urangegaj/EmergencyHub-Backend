using StackExchange.Redis;

namespace Shared.Redis;

public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    private static readonly string ReleaseScript =
        "if redis.call('get',KEYS[1])==ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var acquired = await db.StringSetAsync(key, token, ttl, When.NotExists);
        return acquired ? new LockHandle(db, key, token) : null;
    }

    private sealed class LockHandle(IDatabase db, string key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
            => await db.ScriptEvaluateAsync(ReleaseScript, [(RedisKey)key], [(RedisValue)token]);
    }
}
