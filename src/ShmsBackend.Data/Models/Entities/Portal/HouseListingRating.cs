namespace ShmsBackend.Data.Models.Entities.Portal;

public class HouseListingRating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseId { get; set; }
    public Guid ExplorerId { get; set; }
    public int Stars { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
