namespace ShmsBackend.Api.Services.Common;

public interface ICacheHelper
{
    Task<T?> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory);
    Task RemoveAsync(string key);

    // Bulk-removes every cached entry whose key starts with the given prefix (e.g. "listings:") —
    // the mechanism broad, simple cache invalidation uses instead of tracking individual keys.
    Task RemoveByPrefixAsync(string prefix);
}
