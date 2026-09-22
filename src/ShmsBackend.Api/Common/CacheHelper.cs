using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;
using StackExchange.Redis;

namespace ShmsBackend.Api.Services.Common;

// Cache-aside wrapper over the app's existing Redis IDistributedCache (see Program.cs's
// AddStackExchangeRedisCache) — the generic building block for read-through caching of
// rarely-changing reference data. Not tied to any one entity; new callers just pick a key and TTL.
public class CacheHelper : ICacheHelper
{
    private readonly IDistributedCache _cache;
    private readonly IConnectionMultiplexer _redis;
    private readonly string _instanceName;
    private readonly ILogger<CacheHelper> _logger;

    public CacheHelper(IDistributedCache cache, IConnectionMultiplexer redis, IOptions<RedisOptions> redisOptions, ILogger<CacheHelper> logger)
    {
        _cache = cache;
        _redis = redis;
        _instanceName = redisOptions.Value.InstanceName ?? string.Empty;
        _logger = logger;
    }

    public async Task<T?> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var cached = await _cache.GetStringAsync(key);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                return JsonSerializer.Deserialize<T>(cached);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize cache entry {Key}; recomputing from source", key);
            }
        }

        var value = await factory();

        var json = JsonSerializer.Serialize(value);
        await _cache.SetStringAsync(key, json, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });

        return value;
    }

    public Task RemoveAsync(string key) => _cache.RemoveAsync(key);

    public async Task RemoveByPrefixAsync(string prefix)
    {
        // IDistributedCache silently prefixes every key it writes with RedisOptions:InstanceName —
        // the raw Redis keys are "{InstanceName}{key}", so the SCAN pattern must account for that
        // or it will match nothing.
        var pattern = (RedisValue)$"{_instanceName}{prefix}*";
        var keysToDelete = new List<RedisKey>();

        foreach (var endpoint in _redis.GetEndPoints())
        {
            var server = _redis.GetServer(endpoint);
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                keysToDelete.Add(key);
            }
        }

        if (keysToDelete.Count == 0)
            return;

        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(keysToDelete.ToArray());

        _logger.LogInformation("Removed {Count} cache entries matching prefix {Prefix}", keysToDelete.Count, prefix);
    }
}
