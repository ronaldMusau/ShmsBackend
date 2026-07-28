namespace ShmsBackend.Data.Models.Entities.Portal;

public class ComplaintLandlordDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Complaint? Complaint { get; set; }
    public int ApprovalAttemptNumber { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime DecidedAt { get; set; }
    public Guid DecidedByLandlordId { get; set; }
}
