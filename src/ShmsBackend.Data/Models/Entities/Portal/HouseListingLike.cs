namespace ShmsBackend.Data.Models.Entities.Portal;

public class HouseListingLike
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseId { get; set; }
    public Guid? ExplorerId { get; set; }
    public string? AnonymousDeviceId { get; set; }
    public bool IsLike { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
