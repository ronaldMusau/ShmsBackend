namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// Money paid OUT to a customer's M-Pesa number (Business to Customer): refunds and similar.
/// Every payout is recorded before anything is sent, needs approval by a different person, and is updated when M-Pesa answers.
/// </summary>
public class B2CPayout
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Purpose { get; set; } = "Other";            // AdvanceRefund | DepositRefund | Other
    public Guid? TenantId { get; set; }
    /// <summary>The vacate refund (VacateSettlement) this payout pays. When it succeeds, that refund is marked paid.</summary>
    public Guid? SettlementId { get; set; }
    public string? TenantName { get; set; }
    public string ReceiverPhone { get; set; } = string.Empty;  // 2547XXXXXXXX
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "PendingApproval";    // PendingApproval | Approved | Submitting | Submitted | Succeeded | Failed | Rejected | TimedOut
    public string? RequestedByUserId { get; set; }
    public string? RequestedByName { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string? ApprovedByUserId { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectReason { get; set; }
    public int CurrentStepOrder { get; set; }
    /// <summary>Who entered their authenticator code and sent the approved payout.</summary>
    public string? SentByUserId { get; set; }
    public string? SentByName { get; set; }
    public string? ConversationId { get; set; }
    public string? ResultCode { get; set; }
    public string? ResultDesc { get; set; }
    public string? MpesaReceipt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
