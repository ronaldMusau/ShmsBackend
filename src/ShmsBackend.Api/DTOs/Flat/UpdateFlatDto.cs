using System;

namespace ShmsBackend.Api.Models.DTOs.Flat;

public class UpdateFlatDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public decimal? Price { get; set; }
    public int? FloorNumber { get; set; }
    public int? Bedrooms { get; set; }
    public int? Bathrooms { get; set; }
    public bool? IsAvailable { get; set; }
    public Guid? HouseId { get; set; }
    public Guid? AgentId { get; set; }
}
