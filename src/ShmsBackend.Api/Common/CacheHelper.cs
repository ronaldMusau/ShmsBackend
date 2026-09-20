using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace ShmsBackend.Api.Services.Common;

// Cache-aside wrapper over the app's existing Redis IDistributedCache (see Program.cs's
// AddStackExchangeRedisCache) — the generic building block for read-through caching of
// rarely-changing reference data. Not tied to any one entity; new callers just pick a key and TTL.
public class CacheHelper : ICacheHelper
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CacheHelper> _logger;

    public CacheHelper(IDistributedCache cache, ILogger<CacheHelper> logger)
    {
        _cache = cache;
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
}
