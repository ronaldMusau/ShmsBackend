using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// Records that a portal user accepted a specific version of the terms for their role.
/// PortalUserId is a loose reference to PortalUser.Id (no FK constraint).
/// One row per (PortalUserId, Role, Version).
/// </summary>
public class TermsAcceptance
{
    public Guid Id { get; set; }
    public Guid PortalUserId { get; set; }
    public int Role { get; set; }
    public int Version { get; set; }
    public DateTime AcceptedAt { get; set; }
}
