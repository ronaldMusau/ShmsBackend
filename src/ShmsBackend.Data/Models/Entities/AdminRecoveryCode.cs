using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>One single-use recovery code for a staff account's authenticator. Only a keyed hash is stored.</summary>
public class AdminRecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdminId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
}
