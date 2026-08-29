using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// An archived (superseded) version of the terms &amp; conditions for one portal role.
/// A row is written just before <see cref="TermsAndConditions"/> is overwritten, capturing
/// the content/version/authorship that was live up to that point.
/// UpdatedAt = when that version was originally set live; ArchivedAt = when it was replaced.
/// UpdatedByAdminId is a loose reference to Admin.Id (no FK constraint).
/// </summary>
public class TermsHistory
{
    public Guid Id { get; set; }
    public int Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime ArchivedAt { get; set; }
    public Guid? UpdatedByAdminId { get; set; }
}
