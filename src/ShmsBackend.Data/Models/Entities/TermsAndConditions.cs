using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// The current terms &amp; conditions text for one portal role. One row per role
/// (Role matches <see cref="ShmsBackend.Data.Enums.PortalUserType"/> numeric values),
/// updated in place — Version is a counter bumped on every edit, not a history of rows.
/// UpdatedByAdminId is a loose reference to Admin.Id (no FK constraint).
/// </summary>
public class TermsAndConditions
{
    public Guid Id { get; set; }
    public int Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedByAdminId { get; set; }
}
