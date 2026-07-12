using System;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Tenant : PortalUser
{
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public Guid? HouseId { get; set; }
    public House? House { get; set; }
    public TenantStatus TenantStatus { get; set; } = TenantStatus.Inactive;
    public bool HasCompletedInitialPayment { get; set; } = false;
    public int TenancyCycle { get; set; } = 1;
    public string? TemporaryInitialPassword { get; set; }
    public DateTime? VerificationEmailSentAt { get; set; }

    public Tenant()
    {
        PortalUserType = PortalUserType.Tenant;
    }
}
