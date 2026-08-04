namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateAppeal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateRequestId { get; set; }
    public VacateRequest? VacateRequest { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public ICollection<VacateAppealLine> Lines { get; set; } = new List<VacateAppealLine>();
}
