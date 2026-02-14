using ShmsBackend.Api.Models.DTOs.Email;

namespace ShmsBackend.Api.Services.Email;

/// <summary>
/// Interface for email operations
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an OTP verification email
    /// </summary>
    /// <param name="emailData">Email template data containing recipient, OTP, etc.</param>
    /// <returns>True if email was sent successfully, false otherwise</returns>
    Task<bool> SendOtpEmailAsync(EmailTemplateDto emailData);

    /// <summary>
    /// Sends a welcome email with temporary password
    /// </summary>
    /// <param name="toEmail">Recipient email address</param>
    /// <param name="firstName">Recipient's first name</param>
    /// <param name="temporaryPassword">Temporary password for first login</param>
    /// <returns>True if email was sent successfully, false otherwise</returns>
    Task<bool> SendWelcomeEmailAsync(string toEmail, string firstName, string temporaryPassword);

    /// <summary>
    /// Sends a password reset email with reset link
    /// </summary>
    /// <param name="toEmail">Recipient email address</param>
    /// <param name="firstName">Recipient's first name</param>
    /// <param name="resetLink">Password reset link</param>
    /// <returns>True if email was sent successfully, false otherwise</returns>
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetLink);
}