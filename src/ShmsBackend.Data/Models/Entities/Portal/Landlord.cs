using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class Landlord : PortalUser
{
    public string? AgencyName { get; set; }
    public string? TemporaryInitialPassword { get; set; }
    public DateTime? VerificationEmailSentAt { get; set; }

    public Landlord()
    {
        PortalUserType = PortalUserType.Landlord;
    }
}
