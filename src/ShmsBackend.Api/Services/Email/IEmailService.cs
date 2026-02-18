using ShmsBackend.Api.Models.DTOs.Email;

namespace ShmsBackend.Api.Services.Email;

public interface IEmailService
{
    Task<bool> SendOtpEmailAsync(EmailTemplateDto emailData);
    Task<bool> SendWelcomeEmailAsync(string toEmail, string firstName, string temporaryPassword);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetLink);
    Task<bool> SendEmailVerificationEmailAsync(string toEmail, string firstName, string verificationLink);
}