namespace ShmsBackend.Data.Models.Entities.Portal;

public class PaymentApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public string MpesaReceiptNumber { get; set; } = string.Empty;
    public decimal AmountApplied { get; set; }
    public string? CheckoutRequestId { get; set; }
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;
}
