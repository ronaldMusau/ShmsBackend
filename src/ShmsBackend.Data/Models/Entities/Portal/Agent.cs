using System.Collections.Generic;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Agent : PortalUser
{
    public string? AgencyName { get; set; }
    public string? LicenseNumber { get; set; }

    public ICollection<AgentFlat> AgentFlats { get; set; } = new List<AgentFlat>();

    public Agent()
    {
        PortalUserType = PortalUserType.Agent;
    }
}
