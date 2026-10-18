namespace ShmsBackend.Data.Models.Entities.Portal;

public class ComplaintCheckoutAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Complaint? Complaint { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Platform service charge added on top of <see cref="Amount"/> for this attempt (never shown to tenant/landlord/agent).</summary>
    public decimal? ServiceChargeAmount { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string PaidBy { get; set; } = "Tenant";     // "Tenant" | "Management"
    public Guid? InitiatedByAdminId { get; set; }
    public string CheckoutRequestId { get; set; } = string.Empty;
    public string AttemptStatus { get; set; } = "Processing";
    public string? ResultCode { get; set; }
    public string? ResultDesc { get; set; }
    public string? MpesaReceiptNumber { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
