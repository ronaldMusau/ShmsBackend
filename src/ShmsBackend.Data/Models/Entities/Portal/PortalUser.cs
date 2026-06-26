using System;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Data.Models.Entities.Portal;

public abstract class PortalUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }
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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public PortalUserType PortalUserType { get; set; }
}
