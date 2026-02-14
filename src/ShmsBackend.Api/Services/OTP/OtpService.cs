using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace ShmsBackend.Api.Services.OTP;

public class OtpService : IOtpService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<OtpService> _logger;
    private readonly int _otpExpirationMinutes = 10;

    public OtpService(IDistributedCache cache, ILogger<OtpService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GenerateOtpAsync(string key)
    {
        // Generate 6-digit OTP
        var random = new Random();
        var otp = random.Next(100000, 999999).ToString();

        var otpData = new
        {
            Code = otp,
            CreatedAt = DateTime.UtcNow
        };

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_otpExpirationMinutes)
        };

        await _cache.SetStringAsync(
            GetOtpKey(key),
            JsonSerializer.Serialize(otpData),
            options
        );

        _logger.LogInformation("OTP generated for key: {Key}", key);
        return otp;
    }

    public async Task<bool> ValidateOtpAsync(string key, string otp)
    {
        var cachedData = await _cache.GetStringAsync(GetOtpKey(key));

        if (string.IsNullOrEmpty(cachedData))
        {
            _logger.LogWarning("OTP not found or expired for key: {Key}", key);
            return false;
        }

        var otpData = JsonSerializer.Deserialize<dynamic>(cachedData);
        var storedOtp = otpData?.GetProperty("Code").GetString();

        if (storedOtp == otp)
        {
            await DeleteOtpAsync(key);
            _logger.LogInformation("OTP validated successfully for key: {Key}", key);
            return true;
        }

        _logger.LogWarning("Invalid OTP provided for key: {Key}", key);
        return false;
    }

    public async Task DeleteOtpAsync(string key)
    {
        await _cache.RemoveAsync(GetOtpKey(key));
        _logger.LogInformation("OTP deleted for key: {Key}", key);
    }

    private static string GetOtpKey(string key) => $"otp:{key.ToLower()}";
}