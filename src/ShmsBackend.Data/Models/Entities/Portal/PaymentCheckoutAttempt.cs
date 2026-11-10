namespace ShmsBackend.Data.Models.Entities.Portal;

public class PaymentCheckoutAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public string CheckoutRequestId { get; set; } = string.Empty;
    public string AttemptStatus { get; set; } = "Processing";
    public string? ResultCode { get; set; }
    public string? ResultDesc { get; set; }
    public int RetryCount { get; set; } = 0;
    /// <summary>Service charge carried by THIS push (set at push time, counts as collected only when the attempt succeeds).</summary>
    public decimal? ServiceChargeAmount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
