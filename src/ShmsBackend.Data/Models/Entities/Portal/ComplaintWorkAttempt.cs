namespace ShmsBackend.Data.Models.Entities.Portal;

public class ComplaintWorkAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Complaint? Complaint { get; set; }
    public int AttemptNumber { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string? TenantVerdict { get; set; }
    public string? TenantVerdictReason { get; set; }
    public DateTime? TenantVerdictAt { get; set; }
}
