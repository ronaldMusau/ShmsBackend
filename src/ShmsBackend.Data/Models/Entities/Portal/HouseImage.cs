using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class HouseImage
{
    public Guid Id { get; set; }
    public Guid HouseId { get; set; }
    public House? House { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
