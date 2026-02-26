namespace ShmsBackend.Api.Services.Common;

public interface IFrontendUrlService
{
    string GetBaseUrl();
    string GetEmailVerificationUrl(string token, string email);
    string GetPasswordResetUrl(string token, string email);
    string GetLoginUrl();
    string GetEmailVerificationPath(string token, string email);
}