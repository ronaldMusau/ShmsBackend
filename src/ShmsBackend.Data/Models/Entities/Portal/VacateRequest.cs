using ShmsBackend.Data.Models.Interfaces;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateRequest : ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid HouseId { get; set; }
    public Guid FlatId { get; set; }
    public Guid LandlordId { get; set; }
    public string Status { get; set; } = "Open";
    public bool NeedsResubmission { get; set; } = false;
    public int? CurrentApprovalStepOrder { get; set; }
    public int ApprovalAttemptNumber { get; set; } = 1;
    public int VacateMonth { get; set; }
    public int VacateYear { get; set; }
    public bool SitDeposit { get; set; }
    public Guid? AssignedAgentId { get; set; }
    public DateTime? InspectionAssignedAt { get; set; }
    public DateTime? InspectionSubmittedAt { get; set; }
    public string? FinalRemarks { get; set; }
    public DateTime? FinalRemarksAt { get; set; }
    public Guid? FinalRemarksByAdminId { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid? ClosedByAdminId { get; set; }
    public DateTime? SettledAt { get; set; }
    public Guid? ReviewedByAdminId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? LastReminderSentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public ICollection<VacateInspectionLine> InspectionLines { get; set; } = new List<VacateInspectionLine>();
    public ICollection<VacateApprovalAction> ApprovalActions { get; set; } = new List<VacateApprovalAction>();
    public ICollection<VacateAppeal> Appeals { get; set; } = new List<VacateAppeal>();
    public ICollection<VacateMessage> Messages { get; set; } = new List<VacateMessage>();
}
