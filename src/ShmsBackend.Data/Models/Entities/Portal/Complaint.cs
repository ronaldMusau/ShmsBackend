using ShmsBackend.Data.Models.Interfaces;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Complaint : ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TicketNumber { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Guid HouseId { get; set; }
    public Guid FlatId { get; set; }
    public Guid LandlordId { get; set; }
    public Guid ComplaintTypeId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public bool? IsBillable { get; set; }
    public string? BillableTarget { get; set; }
    public string? BillableTargetOverrideReason { get; set; }
    public decimal? BillableAmount { get; set; }
    public string? BillableExplanation { get; set; }
    public Guid? ReviewedByAdminId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? CurrentApprovalStepOrder { get; set; }
    public int ApprovalAttemptNumber { get; set; } = 1;
    public string? LandlordDecision { get; set; }
    public string? LandlordDecisionNotes { get; set; }
    public DateTime? LandlordActionedAt { get; set; }
    public string? FinalDecision { get; set; }
    public DateTime? FinalDecisionAt { get; set; }
    public Guid? EscalatedToAgentId { get; set; }
    public string? EscalationNotes { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime? AgentCompletedAt { get; set; }
    public DateTime? TenantCompletedAt { get; set; }
    public Guid? ClosedByAdminId { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public ICollection<ComplaintAttachment> Attachments { get; set; } = new List<ComplaintAttachment>();
    public ICollection<ComplaintApprovalAction> ApprovalActions { get; set; } = new List<ComplaintApprovalAction>();
    public ICollection<ComplaintStatusHistoryEntry> StatusHistory { get; set; } = new List<ComplaintStatusHistoryEntry>();
}
