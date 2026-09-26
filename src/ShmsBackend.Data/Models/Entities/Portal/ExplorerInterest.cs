using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class ExplorerInterest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExplorerId { get; set; }
    public Guid HouseId { get; set; }
    public Guid? SessionId { get; set; }  // the closed ListingViewingSession that prompted this, if any
    public string Status { get; set; } = "Pending";  // Pending, Superseded, Converted
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
