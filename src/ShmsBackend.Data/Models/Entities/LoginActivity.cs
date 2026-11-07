using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>One login attempt. Also the basis for "My Devices" and the new-device email later. Never stores passwords or codes.</summary>
public class LoginActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
    public bool Success { get; set; }
    /// <summary>The role that was signed in to (SuperAdmin, Admin, Manager, Accountant, Secretary, Landlord, Agent, Tenant, Explorer). The same email can exist under several roles.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(30)]
    public string? Role { get; set; }
}
