using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// A time-boxed shared "default" password that subscribed admins may use to sign in.
/// Only one row is ever <see cref="IsActive"/> = true. The plaintext is NEVER stored —
/// only its BCrypt hash. Rotated every 7 days by WeeklyPasswordSchedulerService.
/// </summary>
public class WeeklyDefaultPassword
{
    public Guid Id { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}
