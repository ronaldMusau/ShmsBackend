namespace ShmsBackend.Data.Models.Entities;

/// <summary>One approver's decision on a reversal or a payout (Kind = "Reversal" or "Payout").</summary>
public class MoneyApprovalAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public int StepOrder { get; set; }
    public Guid ApproverId { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;      // Approved | Rejected
    public string? Notes { get; set; }
    public DateTime ActionedAt { get; set; } = DateTime.UtcNow;
}
