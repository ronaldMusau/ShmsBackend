using Microsoft.Extensions.Caching.Distributed;

namespace ShmsBackend.Api.Services.Auth;

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<TokenBlacklistService> _logger;

    public TokenBlacklistService(IDistributedCache cache, ILogger<TokenBlacklistService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task BlacklistTokenAsync(string token, TimeSpan? expiration = null)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromHours(24)
        };

        await _cache.SetStringAsync(GetBlacklistKey(token), "blacklisted", options);
        _logger.LogInformation("Token blacklisted successfully");
    }

    public async Task<bool> IsTokenBlacklistedAsync(string token)
    {
        var result = await _cache.GetStringAsync(GetBlacklistKey(token));
        return !string.IsNullOrEmpty(result);
    }

    private static string GetBlacklistKey(string token) => $"blacklist:{token}";
}