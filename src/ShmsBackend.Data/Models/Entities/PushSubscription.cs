using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// A single browser Web Push subscription for a user. UserId is a loose reference to
/// either Admin.Id (IsPortalUser == false) or PortalUser.Id (IsPortalUser == true) —
/// two separate ID spaces, so there is deliberately no FK constraint.
/// A user may have many subscriptions (one per device / browser), so UserId is NOT unique.
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    // true  => UserId refers to a PortalUser (Landlord/Agent/Tenant/Explorer)
    // false => UserId refers to an Admin (SuperAdmin/Admin/Secretary/Manager/Accountant)
    public bool IsPortalUser { get; set; }

    // Web Push endpoint URL and the two subscription keys from the browser's PushSubscription.
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
