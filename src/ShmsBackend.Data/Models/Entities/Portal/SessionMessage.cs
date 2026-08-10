namespace ShmsBackend.Data.Models.Entities.Portal;

public class SessionMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public ListingViewingSession? Session { get; set; }
    public Guid AgentId { get; set; }
    public string SenderRole { get; set; } = string.Empty;
    public Guid SenderUserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsReadByAgent { get; set; } = false;
    public bool IsReadByExplorer { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
