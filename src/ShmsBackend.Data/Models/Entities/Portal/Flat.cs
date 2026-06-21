using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Flat
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public decimal Price { get; set; }
    public int FloorNumber { get; set; }
    public int Bedrooms { get; set; }
    public int Bathrooms { get; set; }
    public bool IsAvailable { get; set; } = true;
    public Guid? HouseId { get; set; }
    public Guid LandlordId { get; set; }
    public Guid? AgentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public House? House { get; set; }
    public Landlord? Landlord { get; set; }
    public Agent? Agent { get; set; }
}
