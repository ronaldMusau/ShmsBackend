namespace ShmsBackend.Api.Services.OTP;

public interface IOtpService
{
    Task<string> GenerateOtpAsync(string key);  // Changed from email to key to support email+type
    Task<bool> ValidateOtpAsync(string key, string otp);  // Changed from email to key
    Task DeleteOtpAsync(string key);  // Changed from email to key
}