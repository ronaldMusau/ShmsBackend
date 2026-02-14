using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Services.Auth;

public class PreAuthCacheService : IPreAuthCacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<PreAuthCacheService> _logger;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10); // Match OTP expiry

    public PreAuthCacheService(IDistributedCache cache, ILogger<PreAuthCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task StorePreAuthDataAsync(string email, UserType userType, PreAuthDto preAuthData)
    {
        var key = GetPreAuthKey(email, userType);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheDuration
        };

        var json = JsonSerializer.Serialize(preAuthData);
        await _cache.SetStringAsync(key, json, options);

        _logger.LogInformation("Pre-auth data stored for {Email} as {UserType}", email, userType);
    }

    public async Task<PreAuthDto?> GetPreAuthDataAsync(string email, UserType userType)
    {
        var key = GetPreAuthKey(email, userType);
        var json = await _cache.GetStringAsync(key);

        if (string.IsNullOrEmpty(json))
        {
            _logger.LogWarning("Pre-auth data not found for {Email} as {UserType}", email, userType);
            return null;
        }

        return JsonSerializer.Deserialize<PreAuthDto>(json);
    }

    public async Task RemovePreAuthDataAsync(string email, UserType userType)
    {
        var key = GetPreAuthKey(email, userType);
        await _cache.RemoveAsync(key);

        _logger.LogInformation("Pre-auth data removed for {Email} as {UserType}", email, userType);
    }

    private static string GetPreAuthKey(string email, UserType userType)
    {
        return $"preauth:{email.ToLower()}:{userType}";
    }
}