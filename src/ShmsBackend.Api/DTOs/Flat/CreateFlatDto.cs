using System;
using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Flat;

public class CreateFlatDto
{
    [Required]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    public string Address { get; set; } = string.Empty;

    [Required]
    public string City { get; set; } = string.Empty;

    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }

    [Required]
    public int FloorNumber { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int Bedrooms { get; set; }

    [Required]
    [Range(0, int.MaxValue)]
    public int Bathrooms { get; set; }

    public Guid? HouseId { get; set; }

    [Required]
    public Guid LandlordId { get; set; }

    public Guid? AgentId { get; set; }
}
