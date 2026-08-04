namespace ShmsBackend.Data.Models.Entities.Portal;

public class VacateMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VacateRequestId { get; set; }
    public VacateRequest? VacateRequest { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public Guid SenderUserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsReadByManagement { get; set; } = false;
    public bool IsReadByTenant { get; set; } = false;
}
