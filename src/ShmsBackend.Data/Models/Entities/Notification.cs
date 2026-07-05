using System;
using ShmsBackend.Data.Models.Interfaces;

namespace ShmsBackend.Data.Models.Entities;

public enum NotificationAudience
{
    // Management portal roles
    SuperAdmin,
    Admin,
    Secretary,
    Manager,
    Accountant,
    // Portal user roles
    Landlord,
    Agent,
    Tenant,
    Explorer,
    // Specific user by ID
    SpecificUser
}

public class Notification : ISoftDelete
{
    public Guid Id { get; set; }

    // If Audience == SpecificUser, this holds the target userId (works for both admin and portal users)
    // If Audience is a role, this is null — the notification goes to all users of that role
    public string? TargetUserId { get; set; }

    // The role audience — null when TargetUserId is set
    public NotificationAudience? Audience { get; set; }

    public string Message { get; set; } = string.Empty;

    // Category for icon/colour on the frontend
    // Categories: general, user, property, payment, security
    public string Category { get; set; } = "general";

    public bool IsRead { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // When a specific user marks it read
    public DateTime? ReadAt { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
}
