using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Models.DTOs.Auth;

/// <summary>
/// Notification channel preferences for the authenticated user.
/// Id / UserId / IsPortalUser are resolved server-side from the auth token.
/// </summary>
public class NotificationPreferenceDto
{
    public bool MasterEmailEnabled { get; set; } = true;
    public bool MasterInAppEnabled { get; set; } = true;

    public bool RentEmailEnabled { get; set; } = true;
    public bool RentInAppEnabled { get; set; } = true;

    public bool ComplaintsEmailEnabled { get; set; } = true;
    public bool ComplaintsInAppEnabled { get; set; } = true;

    public bool ApprovalsEmailEnabled { get; set; } = true;
    public bool ApprovalsInAppEnabled { get; set; } = true;

    public bool PropertiesEmailEnabled { get; set; } = true;
    public bool PropertiesInAppEnabled { get; set; } = true;

    public bool AccountEmailEnabled { get; set; } = true;
    public bool AccountInAppEnabled { get; set; } = true;

    public bool TeamActivityEmailEnabled { get; set; } = true;
    public bool TeamActivityInAppEnabled { get; set; } = true;

    public static NotificationPreferenceDto FromEntity(NotificationPreference p) => new()
    {
        MasterEmailEnabled = p.MasterEmailEnabled,
        MasterInAppEnabled = p.MasterInAppEnabled,
        RentEmailEnabled = p.RentEmailEnabled,
        RentInAppEnabled = p.RentInAppEnabled,
        ComplaintsEmailEnabled = p.ComplaintsEmailEnabled,
        ComplaintsInAppEnabled = p.ComplaintsInAppEnabled,
        ApprovalsEmailEnabled = p.ApprovalsEmailEnabled,
        ApprovalsInAppEnabled = p.ApprovalsInAppEnabled,
        PropertiesEmailEnabled = p.PropertiesEmailEnabled,
        PropertiesInAppEnabled = p.PropertiesInAppEnabled,
        AccountEmailEnabled = p.AccountEmailEnabled,
        AccountInAppEnabled = p.AccountInAppEnabled,
        TeamActivityEmailEnabled = p.TeamActivityEmailEnabled,
        TeamActivityInAppEnabled = p.TeamActivityInAppEnabled
    };
}
