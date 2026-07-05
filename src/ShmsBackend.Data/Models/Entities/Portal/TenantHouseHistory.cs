using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class TenantHouseHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseId { get; set; }
    public Guid TenantId { get; set; }
    public string TenantFirstName { get; set; } = string.Empty;
    public string TenantLastName { get; set; } = string.Empty;
    public string TenantEmail { get; set; } = string.Empty;
    public string? TenantPhone { get; set; }
    public string HouseNumber { get; set; } = string.Empty;
    public string FlatName { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RemovedAt { get; set; }

    // Navigation
    public House? House { get; set; }
}
