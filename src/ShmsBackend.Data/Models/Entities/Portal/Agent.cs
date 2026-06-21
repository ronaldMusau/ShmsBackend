using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Agent : PortalUser
{
    public string? AgencyName { get; set; }
    public string? LicenseNumber { get; set; }

    public Agent()
    {
        PortalUserType = PortalUserType.Agent;
    }
}
