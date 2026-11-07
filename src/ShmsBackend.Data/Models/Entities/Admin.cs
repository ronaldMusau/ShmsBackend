using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Interfaces;

namespace ShmsBackend.Data.Models.Entities;

public abstract class Admin : ISoftDelete
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsEmailVerified { get; set; } = false;
    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationTokenExpiry { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }

    // Login lockout + reset-attempt hardening
    public int FailedLoginAttempts { get; set; } = 0;
    public bool IsLockedOut { get; set; } = false;

    // Authenticator app (TOTP). The secret is stored encrypted; it is never returned by any endpoint after enrolment.
    public string? TotpSecretEnc { get; set; }
    public DateTime? TotpEnabledAt { get; set; }
    public long TotpLastStep { get; set; }
    /// <summary>Set when the person used Emergency Recovery to ask an administrator to reset their authenticator. Cleared by the reset.</summary>
    public DateTime? RecoveryRequestedAt { get; set; }
    public int PasswordResetAttempts { get; set; } = 0;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    // Discriminator to know which type of admin this is
    public UserType UserType { get; set; }

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    // Navigation property for who created this user
    [ForeignKey("CreatedBy")]
    public Admin? Creator { get; set; }
}