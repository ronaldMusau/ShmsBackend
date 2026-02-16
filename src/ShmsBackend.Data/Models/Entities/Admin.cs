using System;
using System.Collections.Generic;
using ShmsBackend.Data.Enums;  // Remove the ForeignKey using

namespace ShmsBackend.Data.Models.Entities;

public abstract class Admin
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsEmailVerified { get; set; } = false;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Discriminator to know which type of admin this is
    public UserType UserType { get; set; }

    // Navigation property for who created this user
    public Admin? Creator { get; set; }
}