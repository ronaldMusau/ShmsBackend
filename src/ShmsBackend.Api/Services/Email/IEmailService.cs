using ShmsBackend.Api.Models.DTOs.Email;

namespace ShmsBackend.Api.Services.Email;

public interface IEmailService
{
    Task<bool> SendOtpEmailAsync(EmailTemplateDto emailData);
    Task<bool> SendWelcomeEmailAsync(string toEmail, string firstName, string temporaryPassword);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetLink);
    Task<bool> SendPasswordResetOtpEmailAsync(string toEmail, string firstName, string otp);
    Task<bool> SendEmailVerificationEmailAsync(string toEmail, string firstName, string verificationLink);
    Task<bool> SendPortalVerifyWithPasswordEmailAsync(string toEmail, string firstName, string verificationLink, string temporaryPassword);
    Task<bool> SendExplorerWelcomeEmailAsync(string toEmail, string firstName, string loginUrl);
    Task<bool> SendAccountDeactivatedEmailAsync(string toEmail, string firstName);
    Task<bool> SendAccountReactivatedEmailAsync(string toEmail, string firstName);
    Task<bool> SendPaymentReceiptEmailAsync(string toEmail, string firstName, string mpesaReceiptNumber, decimal amount, string houseNumber, string flatName, DateTime paidAt);
    Task<bool> SendPaymentReminderEmailAsync(string toEmail, string firstName, decimal amountDue, DateTime dueDate, string houseNumber, string flatName);
    Task<bool> SendPaymentOverdueEmailAsync(string toEmail, string firstName, decimal amountDue, int daysOverdue, string houseNumber, string flatName);
    Task<bool> SendRentChangeNoticeAsync(string toEmail, string firstName, string houseNumber, decimal newRentFee, int effectiveMonth, int effectiveYear);
    Task<bool> SendFlatCreatedLandlordEmailAsync(string toEmail, string firstName, string flatName, int houseCount);
    Task<bool> SendFlatAssignedAgentEmailAsync(string toEmail, string firstName, string flatName);
}