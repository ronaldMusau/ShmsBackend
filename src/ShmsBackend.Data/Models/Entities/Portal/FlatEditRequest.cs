using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class FlatEditRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlatId { get; set; }
    public Flat? Flat { get; set; }
    public string? ProposedFlatName { get; set; }
    public string? ProposedCounty { get; set; }
    public string? ProposedConstituency { get; set; }
    public string? ProposedWard { get; set; }
    public int? ProposedRentDueDay { get; set; }
    public int? ProposedBillableGracePeriodMonths { get; set; }
    public string? ProposedGoogleMapsLink { get; set; }
    public Guid? ProposedAgentId { get; set; }
    public bool ClearAgent { get; set; } = false;
    public Guid RequestedByUserId { get; set; }
    public string Status { get; set; } = "Pending";
    public int? CurrentApprovalStepOrder { get; set; }
    public int ApprovalAttemptNumber { get; set; } = 1;
    public string? RejectionReason { get; set; }
    public string? LandlordDecision { get; set; }
    public string? LandlordDecisionNotes { get; set; }
    public DateTime? LandlordActionedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
