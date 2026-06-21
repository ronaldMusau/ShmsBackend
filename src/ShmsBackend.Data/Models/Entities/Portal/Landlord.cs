using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Landlord : PortalUser
{
    public string? NationalId { get; set; }
    public string? AgencyName { get; set; }

    public Landlord()
    {
        PortalUserType = PortalUserType.Landlord;
    }
}
