namespace ShmsBackend.Data.Models.Entities.Portal;

public class HouseListingComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseId { get; set; }
    public Guid? ExplorerId { get; set; }
    public string? AnonymousDeviceId { get; set; }
    public string CommenterName { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsHidden { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
