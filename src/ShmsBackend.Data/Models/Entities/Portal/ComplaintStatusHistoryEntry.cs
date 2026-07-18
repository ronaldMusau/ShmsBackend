namespace ShmsBackend.Data.Models.Entities.Portal;

public class ComplaintStatusHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Complaint? Complaint { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public Guid? ChangedByAdminId { get; set; }
    public Guid? ChangedByTenantId { get; set; }
    public string? Notes { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
