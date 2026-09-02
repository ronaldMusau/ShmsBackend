using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// An archived (superseded) version of the agreement PDF template for one role.
/// A row is written just before <see cref="AgreementTemplate"/> is overwritten.
/// UploadedAt = when that version was originally set live; ArchivedAt = when it was replaced.
/// UploadedByAdminId is a loose reference to Admin.Id (no FK constraint).
/// </summary>
public class AgreementTemplateHistory
{
    public Guid Id { get; set; }
    public int Role { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime ArchivedAt { get; set; }
    public Guid? UploadedByAdminId { get; set; }
}
