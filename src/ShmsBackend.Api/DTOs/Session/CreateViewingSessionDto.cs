namespace ShmsBackend.Api.DTOs.Session;

public class CreateViewingSessionDto
{
    public Guid HouseId { get; set; }
    public DateTime ScheduledAt { get; set; }
}
