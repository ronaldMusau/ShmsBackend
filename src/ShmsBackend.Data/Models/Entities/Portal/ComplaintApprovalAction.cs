namespace ShmsBackend.Data.Models.Entities.Portal;

public class ComplaintApprovalAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Complaint? Complaint { get; set; }
    public int AttemptNumber { get; set; }
    public int StepOrder { get; set; }
    public Guid ApproverId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime ActionedAt { get; set; } = DateTime.UtcNow;
}
