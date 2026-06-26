using System;
using System.Collections.Generic;

namespace ShmsBackend.Api.Models.DTOs.Agent;

public class AgentFlatAssignmentDto
{
    public List<Guid> FlatIds { get; set; } = new();
}
