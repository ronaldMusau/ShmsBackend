namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// A copy of every M-Pesa STK callback exactly as received, kept so a payment whose callback arrived but was not applied
/// can be applied again by reconciliation (the status query alone does not return the receipt number or amount).
/// </summary>
public class MpesaCallbackLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CheckoutRequestId { get; set; } = string.Empty;
    public int ResultCode { get; set; }
    public string RawJson { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public bool Applied { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? Error { get; set; }
}
