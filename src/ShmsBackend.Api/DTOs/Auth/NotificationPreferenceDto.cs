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
    public bool MasterPushEnabled { get; set; } = true;

    public bool RentEmailEnabled { get; set; } = true;
    public bool RentInAppEnabled { get; set; } = true;
    public bool RentPushEnabled { get; set; } = true;

    public bool ComplaintsEmailEnabled { get; set; } = true;
    public bool ComplaintsInAppEnabled { get; set; } = true;
    public bool ComplaintsPushEnabled { get; set; } = true;

    public bool ApprovalsEmailEnabled { get; set; } = true;
    public bool ApprovalsInAppEnabled { get; set; } = true;
    public bool ApprovalsPushEnabled { get; set; } = true;

    public bool PropertiesEmailEnabled { get; set; } = true;
    public bool PropertiesInAppEnabled { get; set; } = true;
    public bool PropertiesPushEnabled { get; set; } = true;

    public bool AccountEmailEnabled { get; set; } = true;
    public bool AccountInAppEnabled { get; set; } = true;
    public bool AccountPushEnabled { get; set; } = true;

    public bool TeamActivityEmailEnabled { get; set; } = true;
    public bool TeamActivityInAppEnabled { get; set; } = true;
    public bool TeamActivityPushEnabled { get; set; } = true;

    public static NotificationPreferenceDto FromEntity(NotificationPreference p) => new()
    {
        MasterEmailEnabled = p.MasterEmailEnabled,
        MasterInAppEnabled = p.MasterInAppEnabled,
        MasterPushEnabled = p.MasterPushEnabled,
        RentEmailEnabled = p.RentEmailEnabled,
        RentInAppEnabled = p.RentInAppEnabled,
        RentPushEnabled = p.RentPushEnabled,
        ComplaintsEmailEnabled = p.ComplaintsEmailEnabled,
        ComplaintsInAppEnabled = p.ComplaintsInAppEnabled,
        ComplaintsPushEnabled = p.ComplaintsPushEnabled,
        ApprovalsEmailEnabled = p.ApprovalsEmailEnabled,
        ApprovalsInAppEnabled = p.ApprovalsInAppEnabled,
        ApprovalsPushEnabled = p.ApprovalsPushEnabled,
        PropertiesEmailEnabled = p.PropertiesEmailEnabled,
        PropertiesInAppEnabled = p.PropertiesInAppEnabled,
        PropertiesPushEnabled = p.PropertiesPushEnabled,
        AccountEmailEnabled = p.AccountEmailEnabled,
        AccountInAppEnabled = p.AccountInAppEnabled,
        AccountPushEnabled = p.AccountPushEnabled,
        TeamActivityEmailEnabled = p.TeamActivityEmailEnabled,
        TeamActivityInAppEnabled = p.TeamActivityInAppEnabled,
        TeamActivityPushEnabled = p.TeamActivityPushEnabled
    };
}
