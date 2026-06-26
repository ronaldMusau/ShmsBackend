using System;
using System.Collections.Generic;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Flat
{
    public Guid Id { get; set; }
    public string FlatName { get; set; } = string.Empty;
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }
    public Guid LandlordId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Landlord? Landlord { get; set; }
    public ICollection<House> Houses { get; set; } = new List<House>();
    public ICollection<AgentFlat> AgentFlats { get; set; } = new List<AgentFlat>();
}
