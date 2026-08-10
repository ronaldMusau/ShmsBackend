namespace ShmsBackend.Api.DTOs.Session;

public class RescheduleViewingSessionDto
{
    public DateTime NewScheduledAt { get; set; }
    public string Reason { get; set; } = string.Empty;
}
