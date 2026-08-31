using Microsoft.Extensions.Options;
using ShmsBackend.Api.Configuration;
using ShmsBackend.Api.Models.DTOs.Email;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Models.Entities;
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
    private readonly INotificationPreferenceService _notificationPreferenceService;

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
        IFrontendUrlService frontendUrlService,
        INotificationPreferenceService notificationPreferenceService)
    {
        _emailOptions = emailOptions.Value;
        _logger = logger;
        _frontendUrlService = frontendUrlService;
        _notificationPreferenceService = notificationPreferenceService;

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

    public async Task<bool> SendPasswordResetOtpEmailAsync(string toEmail, string firstName, string otp)
    {
        _logger.LogInformation("Sending password reset OTP email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Password Reset Code — Romah Estates",
            GetPasswordResetOtpEmailTemplate(firstName, otp));
    }

    public async Task<bool> SendEmailVerificationEmailAsync(string toEmail, string firstName, string verificationLink)
    {
        _logger.LogInformation("Sending email verification to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Romah Estates — Verify Your Email Address",
            GetEmailVerificationTemplate(firstName, verificationLink));
    }

    public async Task<bool> SendPortalVerifyWithPasswordEmailAsync(string toEmail, string firstName, string verificationLink, string temporaryPassword)
    {
        _logger.LogInformation("Sending portal verify+password email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Romah Estates — Verify Your Email & Get Started",
            GetPortalVerifyWithPasswordTemplate(firstName, verificationLink, temporaryPassword));
    }

    public async Task<bool> SendConfirmNewEmailAsync(string toEmail, string firstName, string confirmationLink)
    {
        _logger.LogInformation("Sending confirm-new-email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Confirm Your New Email Address — Romah Estates",
            GetConfirmNewEmailTemplate(firstName, confirmationLink));
    }

    public async Task<bool> SendExplorerWelcomeEmailAsync(string toEmail, string firstName, string loginUrl)
    {
        _logger.LogInformation("Sending explorer welcome email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Welcome to Romah Estates — You're All Set!",
            GetExplorerWelcomeTemplate(firstName, loginUrl));
    }

    public async Task<bool> SendAccountLockedEmailAsync(string toEmail, string firstName)
    {
        _logger.LogInformation("Sending account locked email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Security Alert: Your Account Has Been Locked",
            GetAccountLockedTemplate(firstName));
    }

    public async Task<bool> SendWeeklyPasswordEmailAsync(string toEmail, string firstName, string password)
    {
        _logger.LogInformation("Sending weekly shared password email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Romah Estates — This Week's Shared Access Password",
            GetWeeklyPasswordTemplate(firstName, password));
    }

    public async Task<bool> SendWeeklyClientPasswordEmailAsync(string toEmail, string firstName, string password)
    {
        _logger.LogInformation("Sending weekly client portal support password email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Romah Estates — Client Portal Support Password (This Week)",
            GetWeeklyClientPasswordTemplate(firstName, password));
    }

    public async Task<bool> SendAccountDeactivatedEmailAsync(string toEmail, string firstName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Account")) return false;
        _logger.LogInformation("Sending account deactivation email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Romah Estates — Account Deactivated",
            GetAccountDeactivatedTemplate(firstName));
    }

    public async Task<bool> SendAccountReactivatedEmailAsync(string toEmail, string firstName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Account")) return false;
        _logger.LogInformation("Sending account reactivation email to: {Email}", toEmail);
        return await SendEmail(
            toEmail,
            "Romah Estates — Account Reactivated",
            GetAccountReactivatedTemplate(firstName));
    }

    public async Task<bool> SendPaymentReceiptEmailAsync(string toEmail, string firstName, string mpesaReceiptNumber, decimal amount, string houseNumber, string flatName, DateTime paidAt, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Rent")) return false;
        _logger.LogInformation("Sending payment receipt email to: {Email}", toEmail);
        return await SendEmail(toEmail, "Payment Receipt — Romah Estates",
            GetPaymentReceiptTemplate(firstName, mpesaReceiptNumber, amount, houseNumber, flatName, paidAt));
    }

    public async Task<bool> SendItemizedPaymentReceiptEmailAsync(string toEmail, string firstName, string mpesaReceiptNumber, decimal totalAmount, List<(int month, int year, decimal applied)> breakdown, string houseNumber, string flatName, DateTime paidAt, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Rent")) return false;
        _logger.LogInformation("Sending itemized payment receipt email to: {Email}", toEmail);
        return await SendEmail(toEmail, "Payment Receipt — Romah Estates",
            GetItemizedPaymentReceiptTemplate(firstName, mpesaReceiptNumber, totalAmount, breakdown, houseNumber, flatName, paidAt));
    }

    public async Task<bool> SendPaymentReminderEmailAsync(string toEmail, string firstName, decimal amountDue, DateTime dueDate, string houseNumber, string flatName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Rent")) return false;
        _logger.LogInformation("Sending payment reminder email to: {Email}", toEmail);
        return await SendEmail(toEmail, "Payment Reminder — Romah Estates",
            GetPaymentReminderTemplate(firstName, amountDue, dueDate, houseNumber, flatName));
    }

    public async Task<bool> SendPaymentOverdueEmailAsync(string toEmail, string firstName, List<(string MonthLabel, decimal Balance)> breakdown, decimal totalArrears, string houseNumber, string flatName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Rent")) return false;
        _logger.LogInformation("Sending payment overdue email to: {Email}", toEmail);
        return await SendEmail(toEmail, "Payment Overdue — Romah Estates",
            GetPaymentOverdueTemplate(firstName, breakdown, totalArrears, houseNumber, flatName));
    }

    public async Task<bool> SendRentChangeNoticeAsync(string toEmail, string firstName, string houseNumber, decimal newRentFee, int effectiveMonth, int effectiveYear, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Rent")) return false;
        _logger.LogInformation("Sending rent change notice to: {Email}", toEmail);
        return await SendEmail(toEmail, "Upcoming Rent Change — Romah Estates",
            GetRentChangeNoticeTemplate(firstName, houseNumber, newRentFee, effectiveMonth, effectiveYear));
    }

    public async Task<bool> SendFlatCreatedLandlordEmailAsync(string toEmail, string firstName, string flatName, int houseCount, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return false;
        _logger.LogInformation("Sending flat created email to landlord: {Email}", toEmail);
        return await SendEmail(toEmail, $"Your flat '{flatName}' has been created — Romah Estates",
            GetFlatCreatedLandlordTemplate(firstName, flatName, houseCount));
    }

    public async Task<bool> SendFlatAssignedAgentEmailAsync(string toEmail, string firstName, string flatName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return false;
        _logger.LogInformation("Sending flat assigned email to agent: {Email}", toEmail);
        return await SendEmail(toEmail, "New flat assigned to you — Romah Estates",
            GetFlatAssignedAgentTemplate(firstName, flatName));
    }

    public async Task SendComplaintConfirmationEmailAsync(string toEmail, string firstName, string ticketNumber, string complaintTypeName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Complaints")) return;
        _logger.LogInformation("Sending complaint confirmation email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Complaint Received — {ticketNumber}",
            GetComplaintConfirmationTemplate(firstName, ticketNumber, complaintTypeName));
    }

    public async Task SendComplaintManagementAlertEmailAsync(string toEmail, string firstName, string ticketNumber, string complaintTypeName, string tenantName, string houseNumber, string flatName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Complaints")) return;
        _logger.LogInformation("Sending complaint management alert email to: {Email}", toEmail);
        await SendEmail(toEmail, $"New Complaint Raised — {ticketNumber}",
            GetComplaintManagementAlertTemplate(firstName, ticketNumber, complaintTypeName, tenantName, houseNumber, flatName));
    }

    public async Task SendComplaintClosedEmailAsync(string toEmail, string firstName, string ticketNumber, string resolutionNotes, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Complaints")) return;
        _logger.LogInformation("Sending complaint closed email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Complaint Resolved — {ticketNumber}",
            GetComplaintClosedTemplate(firstName, ticketNumber, resolutionNotes));
    }

    public async Task SendComplaintEscalatedAgentEmailAsync(string toEmail, string firstName, string ticketNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Complaints")) return;
        _logger.LogInformation("Sending complaint escalation email to agent: {Email}", toEmail);
        await SendEmail(toEmail, $"Complaint Escalated to You — {ticketNumber}",
            GetComplaintEscalatedAgentTemplate(firstName, ticketNumber));
    }

    public async Task SendApprovalStepEmailAsync(string toEmail, string firstName, string ticketNumber, int stepOrder, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Approvals")) return;
        _logger.LogInformation("Sending approval-step email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Approval Needed — {ticketNumber}",
            GetApprovalStepTemplate(firstName, ticketNumber, stepOrder));
    }

    public async Task SendApprovalRejectedEmailAsync(string toEmail, string firstName, string ticketNumber, string rejectionReason, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Approvals")) return;
        _logger.LogInformation("Sending approval-rejected email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Complaint Sent Back for Revision — {ticketNumber}",
            GetApprovalRejectedTemplate(firstName, ticketNumber, rejectionReason));
    }

    public async Task SendLandlordApprovalNeededEmailAsync(string toEmail, string firstName, string ticketNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Approvals")) return;
        _logger.LogInformation("Sending landlord approval-needed email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Your Approval Needed — {ticketNumber}",
            GetLandlordApprovalNeededTemplate(firstName, ticketNumber));
    }

    public async Task SendLandlordDecisionEmailAsync(string toEmail, string firstName, string ticketNumber, string decision, string? notes, decimal? amount, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Approvals")) return;
        _logger.LogInformation("Sending landlord-decision email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Landlord {decision} Complaint {ticketNumber}",
            GetLandlordDecisionTemplate(firstName, ticketNumber, decision, notes, amount));
    }

    public async Task SendFlatEditSubmittedEmailAsync(string toEmail, string firstName, string flatName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Approvals")) return;
        _logger.LogInformation("Sending flat edit submitted email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Flat Edit Submitted — {flatName}",
            GetFlatEditSubmittedTemplate(firstName, flatName));
    }

    public async Task SendDeductionCreatedEmailAsync(string toEmail, string firstName, string ticketNumber, decimal amount, string? description, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Rent")) return;
        _logger.LogInformation("Sending deduction created email to landlord: {Email}", toEmail);
        await SendEmail(toEmail, $"Deduction Created — {ticketNumber}",
            GetDeductionCreatedTemplate(firstName, ticketNumber, amount, description));
    }

    public async Task SendComplaintOverdueManagementEmailAsync(string toEmail, string firstName, string ticketNumber, int daysOpen, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Complaints")) return;
        _logger.LogInformation("Sending overdue complaint reminder email to management: {Email}", toEmail);
        await SendEmail(toEmail, $"Overdue Complaint — {ticketNumber}",
            GetComplaintOverdueManagementTemplate(firstName, ticketNumber, daysOpen));
    }

    public async Task SendComplaintOverdueAgentEmailAsync(string toEmail, string firstName, string ticketNumber, int daysOpen, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Complaints")) return;
        _logger.LogInformation("Sending overdue complaint reminder email to agent: {Email}", toEmail);
        await SendEmail(toEmail, $"Overdue Complaint — Action Required — {ticketNumber}",
            GetComplaintOverdueAgentTemplate(firstName, ticketNumber, daysOpen));
    }

    public async Task SendComplaintOverdueLandlordEmailAsync(string toEmail, string firstName, string ticketNumber, int daysOpen, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Complaints")) return;
        _logger.LogInformation("Sending overdue complaint reminder email to landlord: {Email}", toEmail);
        await SendEmail(toEmail, $"Complaint Awaiting Your Decision — {ticketNumber}",
            GetComplaintOverdueLandlordTemplate(firstName, ticketNumber, daysOpen));
    }

    // ── Preference gating ────────────────────────────────────────────────────

    /// <summary>
    /// Whether an email in <paramref name="group"/> should be sent to this user.
    /// If <paramref name="userId"/> is null/empty (identity not threaded through), always returns true —
    /// we never silently block an email whose recipient wasn't resolved. Otherwise loads the user's
    /// NotificationPreference (all-true when none exists, matching GetOrCreateAsync) and returns
    /// MasterEmailEnabled AND {group}EmailEnabled.
    /// </summary>
    private async Task<bool> ShouldSendEmailAsync(string? userId, bool isPortalUser, string group)
    {
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var uid))
            return true;

        var pref = await _notificationPreferenceService.GetOrCreateAsync(uid, isPortalUser);

        bool Flag(string name) =>
            (bool?)typeof(NotificationPreference).GetProperty($"{name}EmailEnabled")?.GetValue(pref) ?? true;

        return Flag("Master") && Flag(group);
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

    // ── Password Reset OTP Template ──────────────────────────────────────────

    private string GetPasswordResetOtpEmailTemplate(string firstName, string otp)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para("We received a request to reset your Romah Estates account password.")}
{Para("Your 6-digit password reset code is:")}
<div style='text-align:center; margin: 32px 0;'>
  <span style='font-size: 2.5rem; font-weight: 700; letter-spacing: 12px; color: {ColourGold};'>{otp}</span>
</div>
{Para($"This code expires in <strong style='color:{ColourGold};'>15 minutes</strong>.")}
{Divider()}
{SmallNote("If you didn't request a password reset, ignore this email. Your password will remain unchanged.")}";

        return WrapInLayout("Password Reset Code — Romah Estates", inner);
    }

    // ── Portal Verify + Password Template ───────────────────────────────────

    private string GetPortalVerifyWithPasswordTemplate(string firstName, string verificationLink, string temporaryPassword)
    {
        var inner = $@"
{H2($"Welcome to Romah Estates, {firstName}!")}
{Para("Your portal account has been created. Verify your email address using the button below and use the temporary password shown here to get started.")}

{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;
            text-transform:uppercase;margin:0 0 8px 0;'>Your Temporary Password</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;
               font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {temporaryPassword}
  </span>
")}

{Para("Click the button below to verify your email. You will be prompted to enter your temporary password and choose a new one.")}
{GoldButton(verificationLink, "VERIFY EMAIL & SET PASSWORD")}
{Para($"This link will expire in <strong style='color:{ColourGold};'>2 weeks</strong>.")}
{Divider()}
{SmallNote("If you did not expect this email, please contact your system administrator.")}";

        return WrapInLayout("Verify Your Email — Romah Estates", inner);
    }

    // ── Explorer Welcome Template ────────────────────────────────────────────

    private string GetExplorerWelcomeTemplate(string firstName, string loginUrl)
    {
        var inner = $@"
{H2($"Welcome to Romah Estates, {firstName}!")}
{Para($"Your Explorer account has been created successfully on <strong style='color:{ColourGold};'>Romah Estates</strong>.")}
{Para("You can now log in and start exploring available properties.")} 
{GoldButton(loginUrl, "LOG IN TO ROMAH ESTATES")}
{Divider()}
{SmallNote("If you did not create this account, please ignore this email.")}";

        return WrapInLayout("Welcome to Romah Estates", inner);
    }

    // ── Weekly Shared Password Template ───────────────────────────────────

    private string GetWeeklyPasswordTemplate(string firstName, string password)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para("You are subscribed to the Romah Estates weekly shared access password. Use the password below to sign in to the management portal this week, alongside your own email address.")}

{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;
            text-transform:uppercase;margin:0 0 12px 0;'>This Week's Password</p>
  <span style='display:inline-block;font-family:""Courier New"",monospace;
        font-size:22px;font-weight:700;color:{ColourGold};
        letter-spacing:2px;word-break:break-all;'>
    {password}
  </span>
")}

{Para($"This password is valid for <strong style='color:{ColourGold};'>7 days</strong> and is replaced automatically. A new one will be emailed to you when it rotates.")}
{Divider()}
{SmallNote("Keep this password confidential. If you should no longer receive it, ask an administrator to remove you from the subscriber list.")}";

        return WrapInLayout("This Week's Shared Access Password — Romah Estates", inner);
    }

    // ── Weekly Client Portal Support Password Template ────────────────────

    private string GetWeeklyClientPasswordTemplate(string firstName, string password)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para("This is the Romah Estates <strong>Client Portal Support Password</strong> for this week. Support staff can use it — together with a client's email address and account type — to sign into any client portal account (Tenant, Landlord, Agent, or Explorer) to help troubleshoot an issue.")}
{Para($"<strong style='color:{ColourGold};'>This is not the management portal password.</strong> Use it only for assisting client accounts, and only when the client has asked for help.")}

{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;
            text-transform:uppercase;margin:0 0 12px 0;'>This Week's Client Portal Support Password</p>
  <span style='display:inline-block;font-family:""Courier New"",monospace;
        font-size:22px;font-weight:700;color:{ColourGold};
        letter-spacing:2px;word-break:break-all;'>
    {password}
  </span>
")}

{Para($"Valid for <strong style='color:{ColourGold};'>7 days</strong>, then replaced automatically. A new one is emailed to you on rotation.")}
{Divider()}
{SmallNote("Keep this password strictly confidential. To stop receiving it, ask an administrator to remove you from the client-support subscriber list.")}";

        return WrapInLayout("Client Portal Support Password — Romah Estates", inner);
    }

    // ── Account Locked Template ────────────────────────────────────────────

    private string GetAccountLockedTemplate(string firstName)
    {
        var inner = $@"
{H2($"Account Locked, {firstName}")}
{Para($"For your security, your <strong style='color:{ColourGold};'>Romah Estates</strong> account has been locked after several unsuccessful sign-in attempts.")}
{Para("This is an automatic safeguard. If those attempts were not you, no further action is needed beyond resetting your password.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;margin:0 0 8px 0;'>HOW TO REGAIN ACCESS</p>
  <p style='margin:0;font-size:15px;color:{ColourTextSec};'>On the sign-in page, choose <strong>Forgot Password</strong> and complete a password reset. Once your password has been reset, your account will be unlocked automatically.</p>
")}
{Divider()}
{SmallNote("If you did not try to sign in, we recommend resetting your password as a precaution.")}";

        return WrapInLayout("Security Alert — Account Locked", inner);
    }

    // ── Account Deactivated Template ────────────────────────────────────────

    private string GetAccountDeactivatedTemplate(string firstName)
    {
        var inner = $@"
{H2($"Account Deactivated, {firstName}")}
{Para($"Your account on <strong style='color:{ColourGold};'>Romah Estates</strong> has been <strong>deactivated</strong> by an administrator.")}
{Para("You will not be able to log in or access any portal features while your account is deactivated.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;margin:0 0 8px 0;'>NEED HELP?</p>
  <p style='margin:0;font-size:15px;color:{ColourTextSec};'>Please contact the Romah Estates management team to resolve this issue.</p>
")}
{Divider()}
{SmallNote("If you believe this was done in error, please contact your administrator immediately.")}";

        return WrapInLayout("Account Deactivated — Romah Estates", inner);
    }

    // ── Account Reactivated Template ─────────────────────────────────────────

    private string GetAccountReactivatedTemplate(string firstName)
    {
        var inner = $@"
{H2($"Account Reactivated, {firstName}")}
{Para($"Great news! Your account on <strong style='color:{ColourGold};'>Romah Estates</strong> has been <strong>reactivated</strong> by an administrator.")}
{Para("You can now log in and access all your portal features.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;margin:0 0 8px 0;'>NEXT STEPS</p>
  <p style='margin:0;font-size:15px;color:{ColourTextSec};'>Visit the Romah Estates portal to log in and access your account.</p>
")}
{Divider()}
{SmallNote("If you did not expect this email or have concerns, please contact your administrator.")}";

        return WrapInLayout("Account Reactivated — Romah Estates", inner);
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

    // ── Confirm New Email Template ──────────────────────────────────────────

    private string GetConfirmNewEmailTemplate(string firstName, string confirmationLink)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"We received a request to change the email address on your <strong style='color:{ColourGold};'>Romah Estates</strong> account to this address.")}
{Para("To confirm this change and start using this email to sign in, click the button below:")}
{GoldButton(confirmationLink, "CONFIRM NEW EMAIL")}
{Para($"This link will expire in <strong style='color:{ColourGold};'>14 days</strong>.")}
{Divider()}
{SmallNote("If you didn't request this change, you can safely ignore this email — your account isn't affected until the new address is confirmed.")}";

        return WrapInLayout("Confirm Your New Email Address — Romah Estates", inner);
    }

    private string GetPaymentReceiptTemplate(string firstName, string mpesaReceiptNumber, decimal amount, string houseNumber, string flatName, DateTime paidAt)
    {
        var inner = $@"
{H2($"Payment Received, {firstName}!")}
{Para($"Your payment for <strong style='color:{ColourGold};'>House {houseNumber}</strong> in {flatName} has been received successfully.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;margin:0 0 8px 0;'>RECEIPT DETAILS</p>
  <table style='width:100%;border-collapse:collapse;'>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>M-Pesa Receipt</td><td style='color:{ColourGold};font-weight:700;font-size:14px;text-align:right;'>{mpesaReceiptNumber}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Amount Paid</td><td style='color:{ColourTextSec};font-weight:700;font-size:14px;text-align:right;'>KES {amount:N2}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Date</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>{paidAt:MMMM dd, yyyy HH:mm}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Property</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>House {houseNumber}, {flatName}</td></tr>
  </table>
")}
{Divider()}
{SmallNote("Please keep this receipt for your records. If you have any questions, contact your property manager.")}";
        return WrapInLayout("Payment Receipt — Romah Estates", inner);
    }

    private string GetItemizedPaymentReceiptTemplate(string firstName, string mpesaReceiptNumber, decimal totalAmount, List<(int month, int year, decimal applied)> breakdown, string houseNumber, string flatName, DateTime paidAt)
    {
        var rows = string.Join("", breakdown.Select(b =>
            $"<tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>" +
            $"{new DateTime(b.year, b.month, 1):MMMM yyyy}</td>" +
            $"<td style='color:{ColourTextSec};font-weight:600;font-size:14px;text-align:right;'>KES {b.applied:N2}</td></tr>"));

        var inner = $@"
{H2($"Payment Received, {firstName}!")}
{Para($"Your payment for <strong style='color:{ColourGold};'>House {houseNumber}</strong> in {flatName} has been received and applied across the following months:")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;margin:0 0 12px 0;'>PAYMENT BREAKDOWN</p>
  <table style='width:100%;border-collapse:collapse;'>
    {rows}
    <tr><td colspan='2'><hr style='border:none;border-top:1px solid {ColourBorderGold};margin:8px 0;'></td></tr>
    <tr>
      <td style='color:{ColourTextSec};font-size:14px;font-weight:700;padding:4px 0;'>Total Paid</td>
      <td style='color:{ColourGold};font-weight:700;font-size:16px;text-align:right;'>KES {totalAmount:N2}</td>
    </tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>M-Pesa Receipt</td><td style='color:{ColourGold};font-weight:700;font-size:14px;text-align:right;'>{mpesaReceiptNumber}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Date</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>{paidAt:MMMM dd, yyyy HH:mm}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Property</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>House {houseNumber}, {flatName}</td></tr>
  </table>
")}
{Divider()}
{SmallNote("Please keep this receipt for your records. If you have any questions, contact your property manager.")}";
        return WrapInLayout("Payment Receipt — Romah Estates", inner);
    }

    private string GetPaymentReminderTemplate(string firstName, decimal amountDue, DateTime dueDate, string houseNumber, string flatName)
    {
        var inner = $@"
{H2($"Payment Reminder, {firstName}")}
{Para($"This is a friendly reminder that your rent payment for <strong style='color:{ColourGold};'>House {houseNumber}</strong> in {flatName} is due soon.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;margin:0 0 8px 0;'>PAYMENT DUE</p>
  <p style='margin:0;font-size:22px;font-weight:700;color:{ColourGold};'>KES {amountDue:N2}</p>
  <p style='margin:8px 0 0;color:{ColourTextMuted};font-size:13px;'>Due by: <strong style='color:{ColourTextSec};'>{dueDate:MMMM dd, yyyy}</strong></p>
")}
{Para("Please ensure your payment is made on time to avoid overdue charges.")}
{Divider()}
{SmallNote("Log in to the Romah Estates portal to make your payment.")}";
        return WrapInLayout("Payment Reminder — Romah Estates", inner);
    }

    private string GetPaymentOverdueTemplate(string firstName, List<(string MonthLabel, decimal Balance)> breakdown, decimal totalArrears, string houseNumber, string flatName)
    {
        var rows = string.Join("", breakdown.Select(b =>
            $"<tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>{b.MonthLabel}</td>" +
            $"<td style='color:#ef4444;font-weight:600;font-size:14px;text-align:right;'>KES {b.Balance:N2}</td></tr>"));

        var inner = $@"
{H2($"Payment Overdue, {firstName}")}
{Para($"Your rent for <strong style='color:{ColourGold};'>House {houseNumber}</strong> in {flatName} has the following overdue balance(s). Please make payment immediately to avoid further action.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;margin:0 0 12px 0;'>OVERDUE BREAKDOWN</p>
  <table style='width:100%;border-collapse:collapse;'>
    {rows}
    <tr><td colspan='2'><hr style='border:none;border-top:1px solid {ColourBorderGold};margin:8px 0;'></td></tr>
    <tr>
      <td style='color:{ColourTextSec};font-size:14px;font-weight:700;padding:4px 0;'>Total Overdue</td>
      <td style='color:#ef4444;font-weight:700;font-size:16px;text-align:right;'>KES {totalArrears:N2}</td>
    </tr>
  </table>
")}
{Divider()}
{SmallNote("If you have already made this payment, please ignore this email or contact your property manager.")}";
        return WrapInLayout("Payment Overdue — Romah Estates", inner);
    }

    private string GetRentChangeNoticeTemplate(string firstName, string houseNumber, decimal newRentFee, int effectiveMonth, int effectiveYear)
    {
        var monthName = new DateTime(effectiveYear, effectiveMonth, 1).ToString("MMMM yyyy");
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Please be informed that the rent for <strong style='color:{ColourGold};'>House {houseNumber}</strong> will change to the amount shown below, effective <strong style='color:{ColourGold};'>{monthName}</strong>.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;margin:0 0 8px 0;'>NEW RENT AMOUNT</p>
  <p style='margin:0;font-size:22px;font-weight:700;color:{ColourGold};'>KES {newRentFee:N2}</p>
  <p style='margin:8px 0 0;color:{ColourTextMuted};font-size:13px;'>Effective from: <strong style='color:{ColourTextSec};'>{monthName}</strong></p>
")}
{Para("If you have any questions regarding this change, please contact your property manager.")}
{Divider()}
{SmallNote("This is an automated notification from Romah Estates Smart Housing Management System.")}";
        return WrapInLayout("Upcoming Rent Change — Romah Estates", inner);
    }

    private string GetFlatCreatedLandlordTemplate(string firstName, string flatName, int houseCount)
    {
        var houseText = houseCount > 0 ? $" with {houseCount} house{(houseCount == 1 ? "" : "s")}" : "";
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your flat <strong style='color:{ColourGold};'>'{flatName}'</strong> has been successfully created{houseText} on the Romah Estates Smart Housing Management System.")}
{Para("You can now view and manage your flat and its units from the landlord portal.")}
{Divider()}
{SmallNote("If you have any questions, please contact the Romah Estates management team.")}";
        return WrapInLayout("Flat Created — Romah Estates", inner);
    }

    private string GetFlatAssignedAgentTemplate(string firstName, string flatName)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A new flat <strong style='color:{ColourGold};'>'{flatName}'</strong> has been assigned to you on Romah Estates for management.")}
{Para("Please log in to your agent portal to view the flat details and begin managing it.")}
{Divider()}
{SmallNote("If you did not expect this assignment, please contact your administrator.")}";
        return WrapInLayout("New Flat Assigned — Romah Estates", inner);
    }

    private string GetComplaintConfirmationTemplate(string firstName, string ticketNumber, string complaintTypeName)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your complaint has been received and logged on the <strong style='color:{ColourGold};'>Romah Estates</strong> system.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET NUMBER</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
  <p style='margin:12px 0 0;color:{ColourTextMuted};font-size:13px;'>Type: <strong style='color:{ColourTextSec};'>{complaintTypeName}</strong></p>
")}
{Para("Our team will review your complaint and update you on its progress. Please keep your ticket number for reference.")}
{Divider()}
{SmallNote("If you did not raise this complaint, please contact the Romah Estates management team immediately.")}";

        return WrapInLayout($"Complaint Received — {ticketNumber}", inner);
    }

    private string GetComplaintManagementAlertTemplate(string firstName, string ticketNumber, string complaintTypeName, string tenantName, string houseNumber, string flatName)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para("A new complaint has been raised and requires your attention.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 12px 0;'>COMPLAINT DETAILS</p>
  <table style='width:100%;border-collapse:collapse;'>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Ticket Number</td><td style='color:{ColourGold};font-weight:700;font-size:14px;text-align:right;'>{ticketNumber}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Type</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>{complaintTypeName}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Raised By</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>{tenantName}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>House</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>{houseNumber}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Flat</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>{flatName}</td></tr>
  </table>
")}
{Para("Please log in to the management portal to review this complaint.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"New Complaint — {ticketNumber}", inner);
    }

    private string GetComplaintClosedTemplate(string firstName, string ticketNumber, string resolutionNotes)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your complaint <strong style='color:{ColourGold};'>{ticketNumber}</strong> has been reviewed and closed by the Romah Estates management team.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET NUMBER</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
")}
<div style='background-color:{ColourElevated};border-left:4px solid {ColourGold};border-radius:6px;padding:16px 20px;margin:24px 0;'>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>RESOLUTION NOTES</p>
  <p style='color:{ColourTextSec};font-size:15px;line-height:1.7;margin:0;'>{resolutionNotes}</p>
</div>
{Para("Thank you for bringing this matter to our attention. We hope this resolution meets your satisfaction.")}
{Divider()}
{SmallNote("If you have further concerns, please do not hesitate to raise a new complaint through the Romah Estates portal.")}";

        return WrapInLayout($"Complaint Resolved — {ticketNumber}", inner);
    }

    private string GetComplaintEscalatedAgentTemplate(string firstName, string ticketNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A complaint has been escalated to you for physical resolution on the <strong style='color:{ColourGold};'>Romah Estates</strong> system.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET NUMBER</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
")}
{Para("Please review the complaint details, carry out the necessary work, and submit your completion notes and evidence through the Romah Estates agent portal.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Complaint Escalated to You — {ticketNumber}", inner);
    }

    private string GetFlatEditSubmittedTemplate(string firstName, string flatName)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"An edit has been submitted for your flat <strong style='color:{ColourGold};'>'{flatName}'</strong> on the Romah Estates system and is now going through internal approval.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>FLAT</p>
  <span style='font-family:""Syne"",Arial,sans-serif;font-size:20px;font-weight:700;color:{ColourGold};'>
    {flatName}
  </span>
")}
{Para("You will be notified when the edit has cleared internal review and requires your final approval.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Flat Edit Submitted — {flatName}", inner);
    }

    private string GetApprovalStepTemplate(string firstName, string ticketNumber, int stepOrder)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A complaint requires your approval on the <strong style='color:{ColourGold};'>Romah Estates</strong> system.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET NUMBER</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
  <p style='margin:12px 0 0;color:{ColourTextMuted};font-size:13px;'>Approval Step: <strong style='color:{ColourTextSec};'>{stepOrder}</strong></p>
")}
{Para("Please log in to the management portal to review and action this complaint.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Approval Needed — {ticketNumber}", inner);
    }

    private string GetApprovalRejectedTemplate(string firstName, string ticketNumber, string rejectionReason)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Complaint <strong style='color:{ColourGold};'>{ticketNumber}</strong> has been rejected at an approval step and sent back to you for revision.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET NUMBER</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
")}
<div style='background-color:{ColourElevated};border-left:4px solid {ColourGold};border-radius:6px;padding:16px 20px;margin:24px 0;'>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>REJECTION REASON</p>
  <p style='color:{ColourTextSec};font-size:15px;line-height:1.7;margin:0;'>{rejectionReason}</p>
</div>
{Para("Please review the reason above and make the necessary revisions before resubmitting.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Complaint Sent Back for Revision — {ticketNumber}", inner);
    }

    private string GetLandlordApprovalNeededTemplate(string firstName, string ticketNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A complaint on your property has cleared internal review and now requires <strong style='color:{ColourGold};'>your final approval</strong>.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET NUMBER</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
")}
{Para("Please log in to your landlord portal to review the complaint details and make your final decision.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Your Approval Needed — {ticketNumber}", inner);
    }

    private string GetLandlordDecisionTemplate(string firstName, string ticketNumber, string decision, string? notes, decimal? amount)
    {
        var decisionColour = decision == "Approved" ? ColourSuccess : ColourError;
        var amountRow = amount.HasValue
            ? $"<tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Amount</td><td style='color:{ColourTextSec};font-weight:700;font-size:14px;text-align:right;'>KES {amount.Value:N2}</td></tr>"
            : "";
        var notesBlock = !string.IsNullOrWhiteSpace(notes)
            ? $@"<div style='background-color:{ColourElevated};border-left:4px solid {ColourGold};border-radius:6px;padding:16px 20px;margin:24px 0;'>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>LANDLORD NOTES</p>
  <p style='color:{ColourTextSec};font-size:15px;line-height:1.7;margin:0;'>{notes}</p>
</div>"
            : "";

        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"The landlord has made a final decision on complaint <strong style='color:{ColourGold};'>{ticketNumber}</strong>.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 12px 0;'>DECISION DETAILS</p>
  <table style='width:100%;border-collapse:collapse;'>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Ticket Number</td><td style='color:{ColourGold};font-weight:700;font-size:14px;text-align:right;'>{ticketNumber}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Decision</td><td style='color:{decisionColour};font-weight:700;font-size:14px;text-align:right;'>{decision}</td></tr>
    {amountRow}
  </table>
")}
{notesBlock}
{Para("Please log in to the management portal to review the full complaint details.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Landlord {decision} Complaint {ticketNumber}", inner);
    }

    private string GetDeductionCreatedTemplate(string firstName, string ticketNumber, decimal amount, string? description)
    {
        var descBlock = !string.IsNullOrWhiteSpace(description)
            ? $"<tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Description</td><td style='color:{ColourTextSec};font-size:13px;text-align:right;'>{description}</td></tr>"
            : "";

        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Following your approval of complaint <strong style='color:{ColourGold};'>{ticketNumber}</strong>, a deduction has been recorded against your account on the Romah Estates system.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 12px 0;'>DEDUCTION DETAILS</p>
  <table style='width:100%;border-collapse:collapse;'>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Ticket Number</td><td style='color:{ColourGold};font-weight:700;font-size:14px;text-align:right;'>{ticketNumber}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Amount Deducted</td><td style='color:{ColourTextSec};font-weight:700;font-size:14px;text-align:right;'>KES {amount:N2}</td></tr>
    {descBlock}
  </table>
")}
{Para("This deduction will be reflected in your account statement. Please log in to your landlord portal to view full deduction history.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Deduction Created — {ticketNumber}", inner);
    }

    private string GetComplaintOverdueManagementTemplate(string firstName, string ticketNumber, int daysOpen)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"The following complaint has exceeded its review period and requires your attention.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 12px 0;'>OVERDUE COMPLAINT</p>
  <table style='width:100%;border-collapse:collapse;'>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Ticket Number</td><td style='color:{ColourGold};font-weight:700;font-size:14px;text-align:right;'>{ticketNumber}</td></tr>
    <tr><td style='color:{ColourTextMuted};font-size:13px;padding:4px 0;'>Days Open</td><td style='color:#ef4444;font-weight:700;font-size:14px;text-align:right;'>{daysOpen} days</td></tr>
  </table>
")}
{Para("Please log in to the management portal to review and action this complaint.")}
{Divider()}
{SmallNote("This is an automated reminder from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Overdue Complaint — {ticketNumber}", inner);
    }

    private string GetComplaintOverdueAgentTemplate(string firstName, string ticketNumber, int daysOpen)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A complaint assigned to you has been open for <strong style='color:#ef4444;'>{daysOpen} days</strong> and requires your prompt attention.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET NUMBER</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
  <p style='margin:12px 0 0;color:#ef4444;font-size:13px;font-weight:600;'>{daysOpen} days open</p>
")}
{Para("Please complete your work on this complaint and submit your completion notes and evidence through the Romah Estates agent portal as soon as possible.")}
{Divider()}
{SmallNote("This is an automated reminder from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Overdue Complaint — {ticketNumber}", inner);
    }

    private string GetComplaintOverdueLandlordTemplate(string firstName, string ticketNumber, int daysOpen)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A complaint on your property has been awaiting your decision for <strong style='color:#ef4444;'>{daysOpen} days</strong>.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET NUMBER</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
  <p style='margin:12px 0 0;color:#ef4444;font-size:13px;font-weight:600;'>{daysOpen} days awaiting your decision</p>
")}
{Para("Please log in to your landlord portal to review the complaint details and submit your final decision at your earliest convenience.")}
{Divider()}
{SmallNote("This is an automated reminder from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Complaint Awaiting Your Decision — {ticketNumber}", inner);
    }

    public async Task SendVacateAssignedAgentEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending vacate inspection assigned email to agent: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Inspection Assigned — {houseNumber}",
            GetVacateAssignedAgentTemplate(firstName, houseNumber));
    }

    private string GetVacateAssignedAgentTemplate(string firstName, string houseNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A vacate inspection has been assigned to you on the <strong style='color:{ColourGold};'>Romah Estates</strong> system.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{Para("Please carry out the inspection, complete the inspection report, and submit your findings and evidence through the Romah Estates agent portal.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Inspection Assigned — {houseNumber}", inner);
    }

    public async Task SendVacateCancelledAgentEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending vacate inspection cancelled email to agent: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Inspection Cancelled — {houseNumber}",
            GetVacateCancelledAgentTemplate(firstName, houseNumber));
    }

    private string GetVacateCancelledAgentTemplate(string firstName, string houseNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A vacate inspection previously assigned to you on the <strong style='color:{ColourGold};'>Romah Estates</strong> system has been cancelled.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{Para("No further action is required from you for this inspection. If you have any questions, please contact management through the Romah Estates agent portal.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Inspection Cancelled — {houseNumber}", inner);
    }

    public async Task SendVacateArrearsBlockEmailAsync(string toEmail, string firstName, decimal arrearsAmount, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending vacate arrears block email to tenant: {Email}", toEmail);
        await SendEmail(toEmail, "Vacate Request Blocked — Outstanding Arrears",
            GetVacateArrearsBlockTemplate(firstName, arrearsAmount));
    }

    private string GetVacateArrearsBlockTemplate(string firstName, decimal arrearsAmount)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your vacate request on the <strong style='color:{ColourGold};'>Romah Estates</strong> system could not be processed because you have outstanding arrears that must be cleared first.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>OUTSTANDING ARREARS</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    KES {arrearsAmount:N2}
  </span>
")}
{Para("Please clear all outstanding arrears through the Romah Estates tenant portal and then re-submit your vacate request.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout("Vacate Request Blocked — Outstanding Arrears", inner);
    }

    public async Task SendVacateSettlementReversedEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending vacate settlement reversed email to tenant: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Settlement Reversed — {houseNumber}",
            GetVacateSettlementReversedTemplate(firstName, houseNumber));
    }

    private string GetVacateSettlementReversedTemplate(string firstName, string houseNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your vacate request for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system has been cancelled by management. Any forfeited deposit or advance credit amounts have been fully restored to your account.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{Para("If you have any questions about this reversal, please contact management through the Romah Estates tenant portal.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Settlement Reversed — {houseNumber}", inner);
    }

    public async Task SendVacateApprovedTenantEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending vacate approved email to tenant: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Request Approved — {houseNumber}",
            GetVacateApprovedTenantTemplate(firstName, houseNumber));
    }

    private string GetVacateApprovedTenantTemplate(string firstName, string houseNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your vacate request for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system has been reviewed and approved by management. Your settlement summary is now ready for review.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{Para("Please log in to the Romah Estates tenant portal to view your settlement details, including any amounts owed or refundable.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Request Approved — {houseNumber}", inner);
    }

    public async Task SendComplaintRejectedManagementEmailAsync(string toEmail, string firstName, string ticketNumber, string? rejectionNotes, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Complaints")) return;
        _logger.LogInformation("Sending complaint rejection management email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Complaint Rejected — {ticketNumber}",
            GetComplaintRejectedManagementTemplate(firstName, ticketNumber, rejectionNotes));
    }

    private string GetComplaintRejectedManagementTemplate(string firstName, string ticketNumber, string? rejectionNotes)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Complaint <strong style='color:{ColourGold};'>{ticketNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system was rejected at the internal approval step and has been sent back for resubmission.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>TICKET</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {ticketNumber}
  </span>
")}
{(string.IsNullOrWhiteSpace(rejectionNotes) ? "" : Para($"<strong>Reason:</strong> {rejectionNotes}"))}
{Para("Please review and resubmit the billable decision to restart the approval process.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Complaint Rejected — {ticketNumber}", inner);
    }

    public async Task SendVacateRejectedManagementEmailAsync(string toEmail, string firstName, string houseNumber, string? rejectionNotes, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Approvals")) return;
        _logger.LogInformation("Sending vacate rejection management email to: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Request Rejected — {houseNumber}",
            GetVacateRejectedManagementTemplate(firstName, houseNumber, rejectionNotes));
    }

    private string GetVacateRejectedManagementTemplate(string firstName, string houseNumber, string? rejectionNotes)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A vacate request for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system was rejected at the internal approval step.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{(string.IsNullOrWhiteSpace(rejectionNotes) ? "" : Para($"<strong>Reason:</strong> {rejectionNotes}"))}
{Para("Please review the rejection notes and take appropriate action through the Romah Estates management portal.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Request Rejected — {houseNumber}", inner);
    }

    public async Task SendVacateFinalRejectionTenantEmailAsync(string toEmail, string firstName, string houseNumber, string remarks, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending vacate final rejection email to tenant: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Request Closed — {houseNumber}",
            GetVacateFinalRejectionTenantTemplate(firstName, houseNumber, remarks));
    }

    public async Task SendVacateAppealManagementEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Approvals")) return;
        _logger.LogInformation("Sending vacate appeal alert email to management: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Settlement Appeal — {houseNumber}",
            GetVacateAppealManagementTemplate(firstName, houseNumber));
    }

    private string GetVacateAppealManagementTemplate(string firstName, string houseNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A tenant has appealed the approved vacate settlement for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{Para("Please log in to the management portal to review the tenant's appeal, inspect the disputed inspection lines, and take appropriate action.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Settlement Appeal — {houseNumber}", inner);
    }

    public async Task SendVacateSettlementPaidTenantEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending vacate settlement paid email to tenant: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Settlement Payment Received — {houseNumber}",
            GetVacateSettlementPaidTenantTemplate(firstName, houseNumber));
    }

    private string GetVacateSettlementPaidTenantTemplate(string firstName, string houseNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your vacate settlement payment for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system has been received and recorded.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{Para("Your vacate process is now complete. If you have any questions, please contact the Romah Estates management team.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Settlement Payment Received — {houseNumber}", inner);
    }

    public async Task SendVacateRefundPaidTenantEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending vacate refund paid email to tenant: {Email}", toEmail);
        await SendEmail(toEmail, $"Vacate Refund Processed — {houseNumber}",
            GetVacateRefundPaidTenantTemplate(firstName, houseNumber));
    }

    private string GetVacateRefundPaidTenantTemplate(string firstName, string houseNumber)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Management has processed your vacate refund for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system. Your refund amount has been marked as paid.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{Para("If you have any questions about your refund, please contact the Romah Estates management team.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Refund Processed — {houseNumber}", inner);
    }

    private string GetVacateFinalRejectionTenantTemplate(string firstName, string houseNumber, string remarks)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your vacate request for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system has been reviewed by management and has been closed.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
")}
{Para($"<strong>Management Remarks:</strong> {remarks}")}
{Para("If you believe this decision was made in error or you have further questions, please contact management directly.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Vacate Request Closed — {houseNumber}", inner);
    }

    public async Task SendSessionRequestAgentEmailAsync(string toEmail, string firstName, string houseNumber, DateTime scheduledAt, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending viewing session request email to agent: {Email}", toEmail);
        await SendEmail(toEmail, $"Viewing Session Requested — {houseNumber}",
            GetSessionRequestAgentTemplate(firstName, houseNumber, scheduledAt));
    }

    private string GetSessionRequestAgentTemplate(string firstName, string houseNumber, DateTime scheduledAt)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"A prospective tenant has requested a viewing session for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system and is awaiting your acceptance.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:16px 0 8px 0;'>REQUESTED DATE &amp; TIME</p>
  <span style='font-size:16px;font-weight:600;color:{ColourTextPrime};'>
    {scheduledAt:dddd, dd MMMM yyyy} at {scheduledAt:HH:mm} UTC
  </span>
")}
{Para("Please log in to the Romah Estates agent portal to accept or decline this session request.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Viewing Session Requested — {houseNumber}", inner);
    }

    public async Task SendSessionConfirmedExplorerEmailAsync(string toEmail, string firstName, string houseNumber, string agentName, string agentPhone, DateTime scheduledAt, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending session confirmed email to explorer: {Email}", toEmail);
        await SendEmail(toEmail, $"Viewing Session Confirmed — {houseNumber}",
            GetSessionConfirmedExplorerTemplate(firstName, houseNumber, agentName, agentPhone, scheduledAt));
    }

    private string GetSessionConfirmedExplorerTemplate(string firstName, string houseNumber, string agentName, string agentPhone, DateTime scheduledAt)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Great news! Your viewing session for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system has been confirmed by the assigned agent.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:16px 0 8px 0;'>DATE &amp; TIME</p>
  <span style='font-size:16px;font-weight:600;color:{ColourTextPrime};'>
    {scheduledAt:dddd, dd MMMM yyyy} at {scheduledAt:HH:mm} UTC
  </span>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:16px 0 8px 0;'>AGENT</p>
  <span style='font-size:15px;font-weight:600;color:{ColourTextPrime};'>{agentName}</span>
  {(string.IsNullOrWhiteSpace(agentPhone) ? "" : $"<br/><span style='font-size:13px;color:{ColourTextSec};'>{agentPhone}</span>")}
")}
{Para("Please ensure you are available at the scheduled time. If you need to reschedule, contact the agent directly or reach out to management through the Romah Estates portal.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Viewing Session Confirmed — {houseNumber}", inner);
    }

    public async Task SendSessionDeclinedManagementEmailAsync(string toEmail, string firstName, string houseNumber, string agentName, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending session declined alert to management: {Email}", toEmail);
        await SendEmail(toEmail, $"Viewing Session Declined — {houseNumber}",
            GetSessionDeclinedManagementTemplate(firstName, houseNumber, agentName));
    }

    private string GetSessionDeclinedManagementTemplate(string firstName, string houseNumber, string agentName)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"An agent has declined a viewing session request on the <strong style='color:{ColourGold};'>Romah Estates</strong> system. Reassignment may be required.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:16px 0 8px 0;'>AGENT</p>
  <span style='font-size:15px;font-weight:600;color:{ColourTextPrime};'>{agentName}</span>
")}
{Para("Please review this session in the management portal and reassign an agent or follow up with the prospective tenant as appropriate.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Viewing Session Declined — {houseNumber}", inner);
    }

    public async Task SendSessionReassignedExplorerEmailAsync(string toEmail, string firstName, string houseNumber, string agentName, string agentPhone, DateTime scheduledAt, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending session reassigned email to explorer: {Email}", toEmail);
        await SendEmail(toEmail, $"Viewing Session Reassigned — {houseNumber}",
            GetSessionReassignedExplorerTemplate(firstName, houseNumber, agentName, agentPhone, scheduledAt));
    }

    private string GetSessionReassignedExplorerTemplate(string firstName, string houseNumber, string agentName, string agentPhone, DateTime scheduledAt)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your viewing session for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system has been reassigned to a new agent. The new agent will need to accept the session before it is confirmed.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:16px 0 8px 0;'>SCHEDULED DATE &amp; TIME</p>
  <span style='font-size:16px;font-weight:600;color:{ColourTextPrime};'>
    {scheduledAt:dddd, dd MMMM yyyy} at {scheduledAt:HH:mm} UTC
  </span>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:16px 0 8px 0;'>NEW AGENT</p>
  <span style='font-size:15px;font-weight:600;color:{ColourTextPrime};'>{agentName}</span>
  {(string.IsNullOrWhiteSpace(agentPhone) ? "" : $"<br/><span style='font-size:13px;color:{ColourTextSec};'>{agentPhone}</span>")}
")}
{Para("Your session is currently pending acceptance by the new agent. You will be notified once they confirm. If you have any questions, please contact management through the Romah Estates portal.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Viewing Session Reassigned — {houseNumber}", inner);
    }

    public async Task SendSessionFeedbackPromptEmailAsync(string toEmail, string firstName, string houseNumber, DateTime scheduledAt, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending session feedback prompt email to explorer: {Email}", toEmail);
        await SendEmail(toEmail, $"How Was Your Viewing? — {houseNumber}",
            GetSessionFeedbackPromptTemplate(firstName, houseNumber, scheduledAt));
    }

    private string GetSessionFeedbackPromptTemplate(string firstName, string houseNumber, DateTime scheduledAt)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"Your scheduled viewing session for house <strong style='color:{ColourGold};'>{houseNumber}</strong> on the <strong style='color:{ColourGold};'>Romah Estates</strong> system was due on {scheduledAt:dddd, dd MMMM yyyy}. Did your viewing take place?")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>HOUSE</p>
  <span style='font-family:""Courier New"",monospace;font-size:22px;font-weight:700;color:{ColourGold};letter-spacing:4px;'>
    {houseNumber}
  </span>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:16px 0 8px 0;'>SCHEDULED DATE</p>
  <span style='font-size:16px;font-weight:600;color:{ColourTextPrime};'>
    {scheduledAt:dddd, dd MMMM yyyy} at {scheduledAt:HH:mm} UTC
  </span>
")}
{Para("Please log in to the Romah Estates portal and <strong>close the session</strong> if your viewing took place, or <strong>reschedule</strong> if you need a new time. If no action is taken within 24 hours, the session will be automatically forfeited.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"How Was Your Viewing? — {houseNumber}", inner);
    }

    public async Task SendSessionCapacityAlertEmailAsync(string toEmail, string firstName, string agentName, string scheduledDate, string? userId = null, bool isPortalUser = false)
    {
        if (!await ShouldSendEmailAsync(userId, isPortalUser, "Properties")) return;
        _logger.LogInformation("Sending session capacity alert to management: {Email}", toEmail);
        await SendEmail(toEmail, $"Agent Capacity Alert — {scheduledDate}",
            GetSessionCapacityAlertTemplate(firstName, agentName, scheduledDate));
    }

    private string GetSessionCapacityAlertTemplate(string firstName, string agentName, string scheduledDate)
    {
        var inner = $@"
{H2($"Hello {firstName},")}
{Para($"An agent on the <strong style='color:{ColourGold};'>Romah Estates</strong> system has reached 5 or more live viewing sessions on a single day. Reassignment may be appropriate.")}
{GoldBox($@"
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:0 0 8px 0;'>AGENT</p>
  <span style='font-size:18px;font-weight:700;color:{ColourGold};'>
    {agentName}
  </span>
  <p style='color:{ColourTextMuted};font-size:12px;letter-spacing:1px;text-transform:uppercase;margin:16px 0 8px 0;'>DATE</p>
  <span style='font-size:16px;font-weight:600;color:{ColourTextPrime};'>{scheduledDate}</span>
")}
{Para("Please review this agent's session load in the management portal and consider reassigning some sessions to other available agents in the same ward.")}
{Divider()}
{SmallNote("This is an automated alert from the Romah Estates Smart Housing Management System.")}";

        return WrapInLayout($"Agent Capacity Alert — {scheduledDate}", inner);
    }
}