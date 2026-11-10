namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// A reconciliation finding that staff have dealt with: either confirmed as fine ("Verified") or the payment record
/// was undone because M-Pesa never received the money ("Reversed"). Findings with a review move to the Reconciled list.
/// </summary>
public class ReconciliationReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IssueKey { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime IssueAt { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantName { get; set; }
    public string? House { get; set; }
    public string? CheckoutRequestId { get; set; }
    public string Outcome { get; set; } = "Verified";   // Verified | Reversed
    public string? Note { get; set; }
    public string? MpesaAnswer { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public string? ReviewedByRole { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
}
