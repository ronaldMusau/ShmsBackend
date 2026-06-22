using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Explorer : PortalUser
{
    public Explorer()
    {
        PortalUserType = PortalUserType.Explorer;
    }
}
