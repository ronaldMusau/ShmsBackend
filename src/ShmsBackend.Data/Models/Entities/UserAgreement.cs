using System;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// One portal user's signable-agreement state. One row per portal user (PortalUserId is a loose
/// reference to PortalUser.Id — no FK). TemplateVersion records which <see cref="AgreementTemplate"/>
/// version was in force when it was sent. UploadedFilePath is null until the user uploads their
/// signed copy.
/// </summary>
public class UserAgreement
{
    public Guid Id { get; set; }
    public Guid PortalUserId { get; set; }
    public int TemplateVersion { get; set; }
    public string? UploadedFilePath { get; set; }
    public DateTime? UploadedAt { get; set; }
    public AgreementStatus Status { get; set; } = AgreementStatus.NotSent;
    public string? RejectionReason { get; set; }
    public Guid? VerifiedByAdminId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? LastReminderSentAt { get; set; }
}
