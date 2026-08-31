using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>
/// A time-boxed shared "support" password that lets staff sign into ANY client portal account
/// (Tenant/Landlord/Agent/Explorer) to assist a user. Only one row is ever <see cref="IsActive"/> = true.
/// The plaintext is NEVER stored — only its BCrypt hash. Rotated every 7 days by
/// WeeklyClientPasswordSchedulerService. Completely separate from the management-side WeeklyDefaultPassword.
/// </summary>
public class WeeklyClientPassword
{
    public Guid Id { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
}
