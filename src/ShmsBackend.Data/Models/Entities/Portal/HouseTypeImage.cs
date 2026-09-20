using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class HouseTypeImage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlatId { get; set; }
    public Guid HouseTypeId { get; set; }
    public string ImagePath { get; set; } = "";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
