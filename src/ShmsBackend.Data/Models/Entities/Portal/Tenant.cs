using System;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Tenant : PortalUser
{
    public DateTime? DateOfBirth { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public Guid? HouseId { get; set; }
    public House? House { get; set; }

    public Tenant()
    {
        PortalUserType = PortalUserType.Tenant;
    }
}
