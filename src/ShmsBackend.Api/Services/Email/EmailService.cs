using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;
using ShmsBackend.Api.Models.DTOs.Email;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ShmsBackend.Api.Services.Email;

public class EmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly ResendEmailOptions _emailOptions;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<ResendEmailOptions> emailOptions,
        ILogger<EmailService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _emailOptions = emailOptions.Value;
        _logger = logger;

        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.resend.com");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _emailOptions.ApiKey);
    }

    public async Task<bool> SendOtpEmailAsync(EmailTemplateDto emailData)
    {
        try
        {
            var htmlContent = GetOtpEmailTemplate(emailData);

            var emailRequest = new
            {
                from = $"{_emailOptions.FromName} <{_emailOptions.FromEmail}>",
                to = new[] { emailData.To },
                subject = emailData.Subject,
                html = htmlContent
            };

            var json = JsonSerializer.Serialize(emailRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/emails", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("OTP email sent successfully to {Email}", emailData.To);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send OTP email to {Email}", emailData.To);
            return false;
        }
    }

    public async Task<bool> SendWelcomeEmailAsync(string toEmail, string firstName, string temporaryPassword)
    {
        try
        {
            var htmlContent = GetWelcomeEmailTemplate(firstName, temporaryPassword);

            var emailRequest = new
            {
                from = $"{_emailOptions.FromName} <{_emailOptions.FromEmail}>",
                to = new[] { toEmail },
                subject = "Welcome to Romah Estates - Your Account Details",
                html = htmlContent
            };

            var json = JsonSerializer.Serialize(emailRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/emails", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Welcome email sent successfully to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to {Email}", toEmail);
            return false;
        }
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetLink)
    {
        try
        {
            var htmlContent = GetPasswordResetEmailTemplate(firstName, resetLink);

            var emailRequest = new
            {
                from = $"{_emailOptions.FromName} <{_emailOptions.FromEmail}>",
                to = new[] { toEmail },
                subject = "Password Reset Request - Romah Estates",
                html = htmlContent
            };

            var json = JsonSerializer.Serialize(emailRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/emails", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Password reset email sent successfully to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", toEmail);
            return false;
        }
    }

    public async Task<bool> SendEmailVerificationEmailAsync(string toEmail, string firstName, string verificationLink)
    {
        try
        {
            var htmlContent = GetEmailVerificationTemplate(firstName, verificationLink);

            var emailRequest = new
            {
                from = $"{_emailOptions.FromName} <{_emailOptions.FromEmail}>",
                to = new[] { toEmail },
                subject = "Smart Housing - Verify Your Email Address",
                html = htmlContent
            };

            var json = JsonSerializer.Serialize(emailRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/emails", content);

            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Email verification sent successfully to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email verification to {Email}", toEmail);
            return false;
        }
    }

    private string GetOtpEmailTemplate(EmailTemplateDto emailData)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>OTP Verification</title>
</head>
<body style='margin: 0; padding: 0; font-family: Arial, sans-serif; background-color: #f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color: #f4f4f4; padding: 20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background-color: #ffffff; border-radius: 8px; overflow: hidden;'>
                    <tr>
                        <td style='background-color: #2563eb; padding: 30px; text-align: center;'>
                            <h1 style='color: #ffffff; margin: 0; font-size: 28px;'>Romah Estates</h1>
                        </td>
                    </tr>
                    <tr>
                        <td style='padding: 40px 30px;'>
                            <h2 style='color: #333333; margin: 0 0 20px 0;'>Hello {emailData.RecipientName},</h2>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                Your verification code is:
                            </p>
                            <div style='background-color: #f8f9fa; border-radius: 8px; padding: 20px; text-align: center; margin: 30px 0;'>
                                <h1 style='color: #2563eb; margin: 0; font-size: 36px; letter-spacing: 8px;'>{emailData.OtpCode}</h1>
                            </div>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                This code will expire in <strong>{emailData.ExpiryMinutes} minutes</strong>.
                            </p>
                            <p style='color: #666666; line-height: 1.6; margin: 0;'>
                                If you didn't request this code, please ignore this email.
                            </p>
                        </td>
                    </tr>
                    <tr>
                        <td style='background-color: #f8f9fa; padding: 20px 30px; text-align: center;'>
                            <p style='color: #999999; margin: 0; font-size: 12px;'>
                                © 2026 Romah Estates. All rights reserved.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    private string GetWelcomeEmailTemplate(string firstName, string temporaryPassword)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Welcome to Romah Estates</title>
</head>
<body style='margin: 0; padding: 0; font-family: Arial, sans-serif; background-color: #f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color: #f4f4f4; padding: 20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background-color: #ffffff; border-radius: 8px; overflow: hidden;'>
                    <tr>
                        <td style='background-color: #2563eb; padding: 30px; text-align: center;'>
                            <h1 style='color: #ffffff; margin: 0; font-size: 28px;'>Welcome to Romah Estates</h1>
                        </td>
                    </tr>
                    <tr>
                        <td style='padding: 40px 30px;'>
                            <h2 style='color: #333333; margin: 0 0 20px 0;'>Hello {firstName},</h2>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                Your admin account has been created successfully. Below are your login credentials:
                            </p>
                            <div style='background-color: #f8f9fa; border-radius: 8px; padding: 20px; margin: 20px 0;'>
                                <p style='color: #333333; margin: 0 0 10px 0;'><strong>Temporary Password:</strong></p>
                                <p style='color: #2563eb; margin: 0; font-size: 18px; font-family: monospace;'>{temporaryPassword}</p>
                            </div>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                Please change this password after your first login for security purposes.
                            </p>
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='#' style='background-color: #2563eb; color: #ffffff; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                    Login to Your Account
                                </a>
                            </div>
                        </td>
                    </tr>
                    <tr>
                        <td style='background-color: #f8f9fa; padding: 20px 30px; text-align: center;'>
                            <p style='color: #999999; margin: 0; font-size: 12px;'>
                                © 2026 Romah Estates. All rights reserved.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    private string GetPasswordResetEmailTemplate(string firstName, string resetLink)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Password Reset Request</title>
</head>
<body style='margin: 0; padding: 0; font-family: Arial, sans-serif; background-color: #f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color: #f4f4f4; padding: 20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background-color: #ffffff; border-radius: 8px; overflow: hidden;'>
                    <tr>
                        <td style='background-color: #2563eb; padding: 30px; text-align: center;'>
                            <h1 style='color: #ffffff; margin: 0; font-size: 28px;'>Password Reset</h1>
                        </td>
                    </tr>
                    <tr>
                        <td style='padding: 40px 30px;'>
                            <h2 style='color: #333333; margin: 0 0 20px 0;'>Hello {firstName},</h2>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                We received a request to reset your password. Click the button below to proceed:
                            </p>
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='{resetLink}' style='background-color: #2563eb; color: #ffffff; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                    Reset Password
                                </a>
                            </div>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                This link will expire in 1 hour.
                            </p>
                            <p style='color: #666666; line-height: 1.6; margin: 0;'>
                                If you didn't request this reset, please ignore this email.
                            </p>
                        </td>
                    </tr>
                    <tr>
                        <td style='background-color: #f8f9fa; padding: 20px 30px; text-align: center;'>
                            <p style='color: #999999; margin: 0; font-size: 12px;'>
                                © 2026 Romah Estates. All rights reserved.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    private string GetEmailVerificationTemplate(string firstName, string verificationLink)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Verify Your Email</title>
</head>
<body style='margin: 0; padding: 0; font-family: Arial, sans-serif; background-color: #f4f4f4;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color: #f4f4f4; padding: 20px;'>
        <tr>
            <td align='center'>
                <table width='600' cellpadding='0' cellspacing='0' style='background-color: #ffffff; border-radius: 8px; overflow: hidden;'>
                    <tr>
                        <td style='background-color: #2563eb; padding: 30px; text-align: center;'>
                            <h1 style='color: #ffffff; margin: 0; font-size: 28px;'>Smart Housing Management</h1>
                        </td>
                    </tr>
                    <tr>
                        <td style='padding: 40px 30px;'>
                            <h2 style='color: #333333; margin: 0 0 20px 0;'>Hello {firstName},</h2>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                An account has been created for you in the Smart Housing Management System.
                            </p>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                To activate your account and set your password, please click the button below:
                            </p>
                            <div style='text-align: center; margin: 30px 0;'>
                                <a href='{verificationLink}' style='background-color: #2563eb; color: #ffffff; padding: 12px 30px; text-decoration: none; border-radius: 5px; display: inline-block;'>
                                    Verify Email & Set Password
                                </a>
                            </div>
                            <p style='color: #666666; line-height: 1.6; margin: 0 0 20px 0;'>
                                This link will expire in <strong>24 hours</strong>.
                            </p>
                            <p style='color: #666666; line-height: 1.6; margin: 0;'>
                                If you didn't expect this email, please ignore it or contact your system administrator.
                            </p>
                        </td>
                    </tr>
                    <tr>
                        <td style='background-color: #f8f9fa; padding: 20px 30px; text-align: center;'>
                            <p style='color: #999999; margin: 0; font-size: 12px;'>
                                © 2026 Smart Housing Management System. All rights reserved.
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }
}