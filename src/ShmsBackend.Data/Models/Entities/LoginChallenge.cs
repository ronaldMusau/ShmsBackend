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
    /// <summary>0 = emailed code, 1 = authenticator code (already enrolled), 2 = authenticator enrolment (after the emailed code).</summary>
    public int Kind { get; set; }
    /// <summary>Enrolment only: the secret being set up, encrypted. Moved to the account once the first code is confirmed.</summary>
    public string? PendingSecretEnc { get; set; }
}
