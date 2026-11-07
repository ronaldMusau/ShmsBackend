using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// One pending staff sign-in code. The code itself is never stored, only a keyed hash (HMAC) bound to the
/// account and this challenge. Single use, expires, limited tries and resends.
/// </summary>
public class LoginChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdminId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime ResendAvailableAt { get; set; }
    public int Attempts { get; set; }
    public int SendCount { get; set; } = 1;
    public DateTime? UsedAt { get; set; }
    public bool Burned { get; set; }
    public string? IpAddress { get; set; }
}
