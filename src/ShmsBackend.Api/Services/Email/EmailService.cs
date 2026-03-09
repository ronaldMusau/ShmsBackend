using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;
using ShmsBackend.Api.Models.DTOs.Email;
using ShmsBackend.Api.Services.Common;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ShmsBackend.Api.Services.Email;

public class EmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly ResendEmailOptions _emailOptions;
    private readonly ILogger<EmailService> _logger;
    private readonly IFrontendUrlService _frontendUrlService;

    // ── Gold theme colours (mirrors gold-theme.css variables) ──
    private const string ColourBg = "#080808";
    private const string ColourCard = "#161616";
    private const string ColourElevated = "#1e1e1e";
    private const string ColourGold = "#D4AF37";
    private const string ColourGoldDark = "#AA8C2F";
    private const string ColourGoldGlow = "rgba(212,175,55,0.15)";
    private const string ColourBorderGold = "rgba(212,175,55,0.25)";
    private const string ColourTextPrime = "#FFFFFF";
    private const string ColourTextSec = "rgba(255,255,255,0.7)";
    private const string ColourTextMuted = "rgba(255,255,255,0.45)";
    private const string ColourSuccess = "#10B981";
    private const string ColourError = "#EF4444";

    public EmailService(
        IOptions<ResendEmailOptions> emailOptions,
        ILogger<EmailService> logger,
        IHttpClientFactory httpClientFactory,
        IFrontendUrlService frontendUrlService)
    {
        _emailOptions = emailOptions.Value;
        _logger = logger;
        _frontendUrlService = frontendUrlService;

        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.resend.com");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _emailOptions.ApiKey);

        _logger.LogInformation("EmailService initialized with FromEmail: {FromEmail}, FromName: {FromName}",
            _emailOptions.FromEmail, _emailOptions.FromName);
    }

    // ── Public send methods ──────────────────────────────────────────────────

    public async Task<bool> SendOtpEmailAsync(EmailTemplateDto emailData)
    {
        _logger.LogInformation("Sending OTP email to: {Email}", emailData.To);
        return await SendEmail(
            emailData.To,
            emailData.Subject,
            GetOtpEmailTemplate(emailData));
    }

    public async Task<bool> SendWelcomeEmailAsync(string toEmail, string firstName, string temporaryPassword)
    {
        _logger.LogInformation("Sending welcome email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Welcome to Romah Estates — Your Account Details",
            GetWelcomeEmailTemplate(firstName, temporaryPassword));
    }

    public async Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetLink)
    {
        _logger.LogInformation("Sending password reset email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Password Reset Request — Romah Estates",
            GetPasswordResetEmailTemplate(firstName, resetLink));
    }

    public async Task<bool> SendEmailVerificationEmailAsync(string toEmail, string firstName, string verificationLink)
    {
        _logger.LogInformation("Sending email verification to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Romah Estates — Verify Your Email Address",
            GetEmailVerificationTemplate(firstName, verificationLink));
    }

    // ── Shared HTTP helper ───────────────────────────────────────────────────

    private async Task<bool> SendEmail(string toEmail, string subject, string htmlContent)
    {
        try
        {
            var emailRequest = new
            {
                from = $"{_emailOptions.FromName} <{_emailOptions.FromEmail}>",
                to = new[] { toEmail },
                subject,
                html = htmlContent
            };

            var json = JsonSerializer.Serialize(emailRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("/emails", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent successfully to {Email}", toEmail);
                return true;
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to send email to {Email}. Status: {Status}, Error: {Error}",
                toEmail, response.StatusCode, error);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending email to {Email}", toEmail);
            return false;
        }
    }

    // ── Shared layout wrapper ────────────────────────────────────────────────

    /// <summary>
    /// Wraps inner HTML in the gold-themed email shell.
    /// All templates call this — one place to update the chrome.
    /// </summary>
    private string WrapInLayout(string title, string innerHtml) => $@"
<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <title>{title}</title>
</head>
<body style='margin:0;padding:0;background-color:{ColourBg};font-family:""DM Sans"",Arial,sans-serif;'>
  <table width='100%' cellpadding='0' cellspacing='0'
         style='background-color:{ColourBg};padding:32px 16px;'>
    <tr><td align='center'>

      <!-- Card -->
      <table width='600' cellpadding='0' cellspacing='0'
             style='max-width:600px;width:100%;background-color:{ColourCard};
                    border-radius:14px;overflow:hidden;
                    border:1px solid {ColourBorderGold};'>

        <!-- Header -->
        <tr>
          <td style='background:linear-gradient(135deg,{ColourGold},{ColourGoldDark});
                     padding:28px 32px;text-align:center;'>
            <span style='font-family:""Syne"",Arial,sans-serif;font-size:22px;
                         font-weight:700;color:#000000;letter-spacing:1px;'>
              🏢 ROMAH ESTATES
            </span>
          </td>
        </tr>

        <!-- Body -->
        <tr>
          <td style='padding:36px 32px;'>
            {innerHtml}
          </td>
        </tr>

        <!-- Footer -->
        <tr>
          <td style='background-color:{ColourElevated};
                     border-top:1px solid {ColourBorderGold};
                     padding:20px 32px;text-align:center;'>
            <p style='color:{ColourTextMuted};margin:0;font-size:12px;line-height:1.6;'>
              © 2026 Romah Estates Smart Housing Management System.<br>
              All rights reserved. This is an automated message — please do not reply.
            </p>
          </td>
        </tr>

      </table>
    </td></tr>
  </table>
</body>
</html>";

    // ── Template helpers ─────────────────────────────────────────────────────

    private string H2(string text) =>
        $"<h2 style='font-family:\"Syne\",Arial,sans-serif;font-size:22px;font-weight:700;" +
        $"color:{ColourTextPrime};margin:0 0 16px 0;'>{text}</h2>";

    private string Para(string text) =>
        $"<p style='color:{ColourTextSec};font-size:15px;line-height:1.7;margin:0 0 16px 0;'>{text}</p>";

    private string GoldBox(string innerHtml) =>
        $"<div style='background-color:{ColourElevated};border:1px solid {ColourBorderGold};" +
        $"border-radius:10px;padding:24px;margin:24px 0;text-align:center;'>" +
        $"{innerHtml}</div>";

    private string GoldButton(string href, string label) =>
        $"<div style='text-align:center;margin:28px 0;'>" +
        $"<a href='{href}' style='background:linear-gradient(135deg,{ColourGold},{ColourGoldDark});" +
        $"color:#000000;font-family:\"Syne\",Arial,sans-serif;font-weight:700;font-size:14px;" +
        $"letter-spacing:1px;text-decoration:none;padding:14px 32px;border-radius:10px;" +
        $"display:inline-block;'>{label}</a></div>";

    private string Divider() =>
        $"<hr style='border:none;border-top:1px solid {ColourBorderGold};margin:24px 0;'>";

    private string SmallNote(string text) =>
        $"<p style='color:{ColourTextMuted};font-size:13px;line-height:1.6;margin:0 0 12px 0;'>{text}</p>";

    // ── OTP Template ─────────────────────────────────────────────────────────

    private string GetOtpEmailTemplate(EmailTemplateDto emailData)
    {
        var loginUrl = _frontendUrlService.GetLoginUrl();

        // Copy-button uses a mailto trick to avoid JS (email clients block JS).
        // Instead we show the OTP large + a "select all" affordance note.
        var inner = $@"
{H2($"Hello {emailData.RecipientName},")}
{Para("Use the verification code below to complete your login. This code is time-sensitive — do not share it with anyone.")}

{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;
            text-transform:uppercase;margin:0 0 12px 0;'>Your OTP Code</p>

  <span id='otp' style='display:inline-block;font-family:""Courier New"",monospace;
        font-size:42px;font-weight:700;color:{ColourGold};
        letter-spacing:12px;margin:0 0 16px 0;'>
    {emailData.OtpCode}
  </span>

  <!-- Copy hint — works without JS by using a mailto link that pre-fills the code -->
  <br>
  <a href='mailto:?body={emailData.OtpCode}'
     style='display:inline-block;background:rgba(212,175,55,0.12);
            border:1px solid {ColourBorderGold};border-radius:6px;
            color:{ColourGold};font-size:12px;font-weight:600;
            letter-spacing:0.5px;padding:6px 16px;text-decoration:none;
            margin-top:4px;'>
    📋 Copy Code
  </a>
")}

{Para($"This code will expire in <strong style='color:{ColourGold};'>{emailData.ExpiryMinutes} minutes</strong>.")}
{Divider()}
{SmallNote("If you didn't request this code, please ignore this email. Your account is safe.")}
{SmallNote($"<a href='{loginUrl}' style='color:{ColourGold};'>→ Go to Login</a>")}";

        return WrapInLayout("OTP Verification — Romah Estates", inner);
    }

    // ── Welcome Template ─────────────────────────────────────────────────────

    private string GetWelcomeEmailTemplate(string firstName, string temporaryPassword)
    {
        var loginUrl = _frontendUrlService.GetLoginUrl();

        var inner = $@"
{H2($"Welcome, {firstName}!")}
{Para("Your administrator account on the Romah Estates Smart Housing Management System has been created. Here are your login credentials:")}

{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;
            text-transform:uppercase;margin:0 0 8px 0;'>Temporary Password</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;
               font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {temporaryPassword}
  </span>
")}

{Para($"Please <strong style='color:{ColourGold};'>change your password</strong> immediately after your first login for security.")}
{GoldButton(loginUrl, "LOGIN TO YOUR ACCOUNT")}
{Divider()}
{SmallNote("If you did not expect this email, please contact your system administrator.")}";

        return WrapInLayout("Welcome to Romah Estates", inner);
    }

    // ── Password Reset Template ──────────────────────────────────────────────

    private string GetPasswordResetEmailTemplate(string firstName, string resetLink)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para("We received a request to reset your password for your Romah Estates account. Click the button below to set a new password:")}
{GoldButton(resetLink, "RESET MY PASSWORD")}
{Para($"This link will expire in <strong style='color:{ColourGold};'>1 hour</strong>.")}
{Divider()}
{SmallNote("If you didn't request a password reset, you can safely ignore this email. Your password will remain unchanged.")}";

        return WrapInLayout("Password Reset — Romah Estates", inner);
    }

    // ── Email Verification Template ──────────────────────────────────────────

    private string GetEmailVerificationTemplate(string firstName, string verificationLink)
    {
        var loginUrl = _frontendUrlService.GetLoginUrl();

        var inner = $@"
{H2($"Hello {firstName},")}
{Para("An account has been created for you on the <strong style='color:{ColourGold};'>Romah Estates Smart Housing Management System</strong>.")}
{Para("To activate your account and set your password, click the button below:")}
{GoldButton(verificationLink, "VERIFY EMAIL & SET PASSWORD")}

{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:13px;margin:0 0 8px 0;'>What happens next?</p>
  <p style='color:{ColourTextSec};font-size:14px;line-height:1.7;margin:0;'>
    1. Click the button above<br>
    2. Enter your temporary password (sent separately) and choose a new secure password<br>
    3. Log in at <a href='{loginUrl}' style='color:{ColourGold};'>{loginUrl}</a>
  </p>
")}

{Para($"This link will expire in <strong style='color:{ColourGold};'>24 hours</strong>.")}
{Divider()}
{SmallNote("If you didn't expect this email, please ignore it or contact your system administrator.")}";

        return WrapInLayout("Verify Your Email — Romah Estates", inner);
    }
}