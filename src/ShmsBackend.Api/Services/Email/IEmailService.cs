using ShmsBackend.Api.Models.DTOs.Email;

namespace ShmsBackend.Api.Services.Email;

public interface IEmailService
{
    Task<bool> SendOtpEmailAsync(EmailTemplateDto emailData);
    Task<bool> SendWelcomeEmailAsync(string toEmail, string firstName, string temporaryPassword);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetLink);
    Task<bool> SendPasswordResetOtpEmailAsync(string toEmail, string firstName, string otp);
    Task<bool> SendEmailVerificationEmailAsync(string toEmail, string firstName, string verificationLink);
    Task<bool> SendPortalWelcomeEmailAsync(string toEmail, string firstName, string password);
    Task<bool> SendAccountDeactivatedEmailAsync(string toEmail, string firstName);
    Task<bool> SendAccountReactivatedEmailAsync(string toEmail, string firstName);
}