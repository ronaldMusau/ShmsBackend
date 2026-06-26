using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class AgentFlat
{
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;

    public Guid FlatId { get; set; }
    public Flat Flat { get; set; } = null!;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
