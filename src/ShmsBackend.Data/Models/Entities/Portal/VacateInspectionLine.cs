namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateInspectionLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateRequestId { get; set; }
    public VacateRequest? VacateRequest { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? AssessedAmount { get; set; }
    public int LineOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<VacateInspectionLineAttachment> Attachments { get; set; } = new List<VacateInspectionLineAttachment>();
}
