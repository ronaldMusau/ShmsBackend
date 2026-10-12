namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateAppeal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateRequestId { get; set; }
    public VacateRequest? VacateRequest { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public string? Outcome { get; set; } // null while pending; "Denied" once management rejects the appeal
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedByAdminId { get; set; }
    public string? Response { get; set; } // management's explanation to the tenant, optional
    public ICollection<VacateAppealLine> Lines { get; set; } = new List<VacateAppealLine>();
}
