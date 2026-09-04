using ShmsBackend.Api.Models.DTOs.Email;

namespace ShmsBackend.Api.Services.Email;

public interface IEmailService
{
    // ── Always-on transactional / security emails (never preference-gated) ──
    Task<bool> SendOtpEmailAsync(EmailTemplateDto emailData);
    Task<bool> SendWelcomeEmailAsync(string toEmail, string firstName, string temporaryPassword);
    Task<bool> SendPasswordResetEmailAsync(string toEmail, string firstName, string resetLink);
    Task<bool> SendPasswordResetOtpEmailAsync(string toEmail, string firstName, string otp);
    Task<bool> SendEmailVerificationEmailAsync(string toEmail, string firstName, string verificationLink);
    Task<bool> SendPortalVerifyWithPasswordEmailAsync(string toEmail, string firstName, string verificationLink, string temporaryPassword);
    Task<bool> SendConfirmNewEmailAsync(string toEmail, string firstName, string confirmationLink);
    Task<bool> SendExplorerWelcomeEmailAsync(string toEmail, string firstName, string loginUrl);
    Task<bool> SendAccountLockedEmailAsync(string toEmail, string firstName);
    Task<bool> SendWeeklyPasswordEmailAsync(string toEmail, string firstName, string password);
    Task<bool> SendWeeklyClientPasswordEmailAsync(string toEmail, string firstName, string password);
    Task<bool> SendAgreementReadyToSignEmailAsync(string toEmail, string firstName, string roleLabel);

    // ── Preference-gated emails (optional userId/isPortalUser threads the recipient identity) ──
    Task<bool> SendAccountDeactivatedEmailAsync(string toEmail, string firstName, string? userId = null, bool isPortalUser = false);
    Task<bool> SendAccountReactivatedEmailAsync(string toEmail, string firstName, string? userId = null, bool isPortalUser = false);
    Task<bool> SendAgreementVerifiedEmailAsync(string toEmail, string firstName, string? userId = null, bool isPortalUser = false);
    Task<bool> SendAgreementRejectedEmailAsync(string toEmail, string firstName, string reason, string? userId = null, bool isPortalUser = false);
    Task<bool> SendAgreementReminderEmailAsync(string toEmail, string firstName, string roleLabel, string? userId = null, bool isPortalUser = false, string? attachmentFileName = null, byte[]? attachmentBytes = null);
    Task<bool> SendPaymentReceiptEmailAsync(string toEmail, string firstName, string mpesaReceiptNumber, decimal amount, string houseNumber, string flatName, DateTime paidAt, string? userId = null, bool isPortalUser = false);
    Task<bool> SendItemizedPaymentReceiptEmailAsync(string toEmail, string firstName, string mpesaReceiptNumber, decimal totalAmount, List<(int month, int year, decimal applied)> breakdown, string houseNumber, string flatName, DateTime paidAt, string? userId = null, bool isPortalUser = false);
    Task<bool> SendPaymentReminderEmailAsync(string toEmail, string firstName, decimal amountDue, DateTime dueDate, string houseNumber, string flatName, string? userId = null, bool isPortalUser = false);
    Task<bool> SendPaymentOverdueEmailAsync(string toEmail, string firstName, List<(string MonthLabel, decimal Balance)> breakdown, decimal totalArrears, string houseNumber, string flatName, string? userId = null, bool isPortalUser = false);
    Task<bool> SendRentChangeNoticeAsync(string toEmail, string firstName, string houseNumber, decimal newRentFee, int effectiveMonth, int effectiveYear, string? userId = null, bool isPortalUser = false);
    Task<bool> SendFlatCreatedLandlordEmailAsync(string toEmail, string firstName, string flatName, int houseCount, string? userId = null, bool isPortalUser = false);
    Task<bool> SendFlatAssignedAgentEmailAsync(string toEmail, string firstName, string flatName, string? userId = null, bool isPortalUser = false);
    Task SendComplaintConfirmationEmailAsync(string toEmail, string firstName, string ticketNumber, string complaintTypeName, string? userId = null, bool isPortalUser = false);
    Task SendComplaintManagementAlertEmailAsync(string toEmail, string firstName, string ticketNumber, string complaintTypeName, string tenantName, string houseNumber, string flatName, string? userId = null, bool isPortalUser = false);
    Task SendComplaintClosedEmailAsync(string toEmail, string firstName, string ticketNumber, string resolutionNotes, string? userId = null, bool isPortalUser = false);
    Task SendComplaintEscalatedAgentEmailAsync(string toEmail, string firstName, string ticketNumber, string? userId = null, bool isPortalUser = false);
    Task SendApprovalStepEmailAsync(string toEmail, string firstName, string ticketNumber, int stepOrder, string? userId = null, bool isPortalUser = false);
    Task SendApprovalRejectedEmailAsync(string toEmail, string firstName, string ticketNumber, string rejectionReason, string? userId = null, bool isPortalUser = false);
    Task SendLandlordApprovalNeededEmailAsync(string toEmail, string firstName, string ticketNumber, string? userId = null, bool isPortalUser = false);
    Task SendLandlordDecisionEmailAsync(string toEmail, string firstName, string ticketNumber, string decision, string? notes, decimal? amount, string? userId = null, bool isPortalUser = false);
    Task SendFlatEditSubmittedEmailAsync(string toEmail, string firstName, string flatName, string? userId = null, bool isPortalUser = false);
    Task SendDeductionCreatedEmailAsync(string toEmail, string firstName, string ticketNumber, decimal amount, string? description, string? userId = null, bool isPortalUser = false);
    Task SendComplaintOverdueManagementEmailAsync(string toEmail, string firstName, string ticketNumber, int daysOpen, string? userId = null, bool isPortalUser = false);
    Task SendComplaintOverdueAgentEmailAsync(string toEmail, string firstName, string ticketNumber, int daysOpen, string? userId = null, bool isPortalUser = false);
    Task SendComplaintOverdueLandlordEmailAsync(string toEmail, string firstName, string ticketNumber, int daysOpen, string? userId = null, bool isPortalUser = false);
    Task SendVacateAssignedAgentEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false);
    Task SendVacateCancelledAgentEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false);
    Task SendVacateArrearsBlockEmailAsync(string toEmail, string firstName, decimal arrearsAmount, string? userId = null, bool isPortalUser = false);
    Task SendVacateSettlementReversedEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false);
    Task SendVacateApprovedTenantEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false);
    Task SendComplaintRejectedManagementEmailAsync(string toEmail, string firstName, string ticketNumber, string? rejectionNotes, string? userId = null, bool isPortalUser = false);
    Task SendVacateRejectedManagementEmailAsync(string toEmail, string firstName, string houseNumber, string? rejectionNotes, string? userId = null, bool isPortalUser = false);
    Task SendVacateFinalRejectionTenantEmailAsync(string toEmail, string firstName, string houseNumber, string remarks, string? userId = null, bool isPortalUser = false);
    Task SendVacateAppealManagementEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false);
    Task SendVacateSettlementPaidTenantEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false);
    Task SendVacateRefundPaidTenantEmailAsync(string toEmail, string firstName, string houseNumber, string? userId = null, bool isPortalUser = false);
    Task SendFirstWarningToVacateEmailAsync(string toEmail, string firstName, decimal arrearsAmount, int overdueDays, string? userId = null, bool isPortalUser = false);
    Task SendFinalWarningToVacateEmailAsync(string toEmail, string firstName, decimal arrearsAmount, int overdueDays, string? userId = null, bool isPortalUser = false);
    Task SendForcedVacateNoticeEmailAsync(string toEmail, string firstName, string houseNumber, string reason, int vacateMonth, int vacateYear, string? userId = null, bool isPortalUser = false);
    Task SendSessionRequestAgentEmailAsync(string toEmail, string firstName, string houseNumber, DateTime scheduledAt, string? userId = null, bool isPortalUser = false);
    Task SendSessionConfirmedExplorerEmailAsync(string toEmail, string firstName, string houseNumber, string agentName, string agentPhone, DateTime scheduledAt, string? userId = null, bool isPortalUser = false);
    Task SendSessionDeclinedManagementEmailAsync(string toEmail, string firstName, string houseNumber, string agentName, string? userId = null, bool isPortalUser = false);
    Task SendSessionReassignedExplorerEmailAsync(string toEmail, string firstName, string houseNumber, string agentName, string agentPhone, DateTime scheduledAt, string? userId = null, bool isPortalUser = false);
    Task SendSessionFeedbackPromptEmailAsync(string toEmail, string firstName, string houseNumber, DateTime scheduledAt, string? userId = null, bool isPortalUser = false);
    Task SendSessionCapacityAlertEmailAsync(string toEmail, string firstName, string agentName, string scheduledDate, string? userId = null, bool isPortalUser = false);
}
