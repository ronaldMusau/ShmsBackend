using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// The current signable agreement PDF template for one portal role (Role matches
/// <see cref="ShmsBackend.Data.Enums.PortalUserType"/> — Landlord/Agent/Tenant only).
/// One row per role, updated in place — Version is bumped on every re-upload.
/// The previous file is archived into <see cref="AgreementTemplateHistory"/> first.
/// UploadedByAdminId is a loose reference to Admin.Id (no FK constraint).
/// </summary>
public class AgreementTemplate
{
    public Guid Id { get; set; }
    public int Role { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTime UploadedAt { get; set; }
    public Guid? UploadedByAdminId { get; set; }
}
