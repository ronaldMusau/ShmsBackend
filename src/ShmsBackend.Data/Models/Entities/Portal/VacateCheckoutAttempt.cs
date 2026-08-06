namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateCheckoutAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateSettlementId { get; set; }
    public VacateSettlement? VacateSettlement { get; set; }
    public string CheckoutRequestId { get; set; } = string.Empty;
    public string AttemptStatus { get; set; } = "Processing";
    public string? ResultCode { get; set; }
    public string? ResultDesc { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
