namespace ShmsBackend.Data.Models.Entities.Portal;

public class ApprovalSequenceStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Module { get; set; } = string.Empty;
    public int StepOrder { get; set; }
    public Guid ApproverId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
