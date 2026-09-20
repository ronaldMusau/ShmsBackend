namespace ShmsBackend.Api.Services.Common;

public interface ICacheHelper
{
    Task<T?> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory);
    Task RemoveAsync(string key);
}
