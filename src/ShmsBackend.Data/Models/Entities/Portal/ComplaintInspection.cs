namespace ShmsBackend.Data.Models.Entities.Portal;

/// <summary>
/// A pre-billing site visit: management sends the flat's agent to assess the extent of damage so the
/// billable amount can be set accurately. Separate from the repair escalation (no payment gate, no tenant verification).
/// </summary>
public class ComplaintInspection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Complaint? Complaint { get; set; }
    public int RoundNumber { get; set; } = 1;
    public Guid AgentId { get; set; }
    public Guid? RequestedByAdminId { get; set; }
    public string? Instructions { get; set; }
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public string? ReportNotes { get; set; }
    public DateTime? ReportedAt { get; set; }
}
