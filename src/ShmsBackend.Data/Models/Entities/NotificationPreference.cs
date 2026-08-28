using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// Per-user notification channel preferences. UserId is a loose reference — it points at
/// either Admin.Id (IsPortalUser == false) or PortalUser.Id (IsPortalUser == true), which
/// are two separate ID spaces, so there is deliberately no FK constraint.
/// Every flag defaults to true (opt-out model). A row is created lazily on first access.
/// </summary>
public class NotificationPreference
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    // true  => UserId refers to a PortalUser (Landlord/Agent/Tenant/Explorer)
    // false => UserId refers to an Admin (SuperAdmin/Admin/Secretary/Manager/Accountant)
    public bool IsPortalUser { get; set; }

    // Master kill switches for each channel
    public bool MasterEmailEnabled { get; set; } = true;
    public bool MasterInAppEnabled { get; set; } = true;

    // Rent / Payments
    public bool RentEmailEnabled { get; set; } = true;
    public bool RentInAppEnabled { get; set; } = true;

    // Complaints
    public bool ComplaintsEmailEnabled { get; set; } = true;
    public bool ComplaintsInAppEnabled { get; set; } = true;

    // Approvals
    public bool ApprovalsEmailEnabled { get; set; } = true;
    public bool ApprovalsInAppEnabled { get; set; } = true;

    // Properties (Flats / Houses)
    public bool PropertiesEmailEnabled { get; set; } = true;
    public bool PropertiesInAppEnabled { get; set; } = true;

    // Account (verification, password, account status)
    public bool AccountEmailEnabled { get; set; } = true;
    public bool AccountInAppEnabled { get; set; } = true;

    // Team activity (new-user / occupancy FYI feed for management)
    public bool TeamActivityEmailEnabled { get; set; } = true;
    public bool TeamActivityInAppEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
