namespace ShmsBackend.Data.Models.Entities.Portal;

public class ComplaintMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Complaint Complaint { get; set; } = null!;
    public string SenderRole { get; set; } = string.Empty;
    public Guid SenderUserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsReadByManagement { get; set; }
    public bool IsReadByTenant { get; set; }
}
