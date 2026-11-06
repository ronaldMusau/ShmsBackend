using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// One security-relevant occurrence anywhere in the system (login lockout, token reuse,
/// payment limit hit, forbidden access spike, M-Pesa mismatch, ...). Never holds secrets,
/// codes, tokens or full phone numbers.
/// </summary>
public class SecurityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Info, Warning, High, Critical</summary>
    public string Severity { get; set; } = "Info";

    /// <summary>Auth, Session, Payment, Mpesa, Access, Data, System</summary>
    public string Source { get; set; } = "System";

    /// <summary>Short machine key, e.g. LoginLockout, TokenReuse, PaymentLimitHit.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Plain-language, already masked.</summary>
    public string Summary { get; set; } = string.Empty;

    public string? ActorUserId { get; set; }
    public string? ActorRole { get; set; }
    public string? IpAddress { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    public bool IsAcknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
