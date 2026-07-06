using System;
using ShmsBackend.Data.Models.Interfaces;

namespace ShmsBackend.Data.Models.Entities;

public class ServiceChargeSetting : ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal MinRent { get; set; }
    public decimal MaxRent { get; set; }
    public decimal ServiceCharge { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
