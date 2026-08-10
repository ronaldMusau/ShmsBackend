namespace ShmsBackend.Data.Models.Entities.Portal;

public class HouseListingBookmark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseId { get; set; }
    public Guid ExplorerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
