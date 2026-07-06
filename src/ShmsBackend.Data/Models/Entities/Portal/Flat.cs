using System;
using System.Collections.Generic;
using ShmsBackend.Data.Models.Interfaces;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Flat : ISoftDelete
{
    public Guid Id { get; set; }
    public string FlatName { get; set; } = string.Empty;
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }
    public int RentDueDay { get; set; } = 5;
    public Guid LandlordId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    public Landlord? Landlord { get; set; }
    public ICollection<House> Houses { get; set; } = new List<House>();
    public ICollection<AgentFlat> AgentFlats { get; set; } = new List<AgentFlat>();
}
