namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateInspectionLineAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateInspectionLineId { get; set; }
    public VacateInspectionLine? VacateInspectionLine { get; set; }
    public Guid VacateRequestId { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
