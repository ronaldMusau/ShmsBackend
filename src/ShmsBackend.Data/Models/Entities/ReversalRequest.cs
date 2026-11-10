namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// A request to undo a payment record that reconciliation found to be wrong. It goes through the "Reversal" approval
/// sequence; the reversal runs only when the last approver approves.
/// </summary>
public class ReversalRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public string IssueKey { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime IssueAt { get; set; }
    public string? CheckoutRequestId { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string? House { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "AwaitingApproval";   // AwaitingApproval | Executed | Rejected | Blocked
    public int CurrentStepOrder { get; set; }
    public string? RequestedByUserId { get; set; }
    public string? RequestedByName { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public string? ResultMessage { get; set; }
}
