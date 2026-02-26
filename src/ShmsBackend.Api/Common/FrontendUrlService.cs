using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;

namespace ShmsBackend.Api.Services.Common;

public class FrontendUrlService : IFrontendUrlService
{
    private readonly AppSettings _appSettings;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FrontendUrlService> _logger;

    public FrontendUrlService(
        IOptions<AppSettings> appSettings,
        IWebHostEnvironment environment,
        ILogger<FrontendUrlService> logger)
    {
        _appSettings = appSettings.Value;
        _environment = environment;
        _logger = logger;

        _logger.LogInformation("FrontendUrlService initialized with URL: {FrontendUrl} in environment: {Environment}",
            _appSettings.FrontendUrl, _environment.EnvironmentName);
    }

    public string GetBaseUrl()
    {
        return _appSettings.FrontendUrl.TrimEnd('/');
    }

    public string GetEmailVerificationUrl(string token, string email)
    {
        var baseUrl = GetBaseUrl();
        var url = $"{baseUrl}/verify-email?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";

        _logger.LogDebug("Generated email verification URL: {Url}", url);
        return url;
    }

    public string GetPasswordResetUrl(string token, string email)
    {
        var baseUrl = GetBaseUrl();
        return $"{baseUrl}/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
    }

    public string GetLoginUrl()
    {
        return $"{GetBaseUrl()}/login";
    }

    public string GetEmailVerificationPath(string token, string email)
    {
        return $"/verify-email?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
    }
}