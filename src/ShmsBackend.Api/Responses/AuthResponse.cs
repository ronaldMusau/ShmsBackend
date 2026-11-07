using System;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.Responses;

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public UserType UserType { get; set; }  // Single type instead of list
    public DateTime ExpiresAt { get; set; }

    // Staff sign-in code step: when RequiresOtp is true the tokens above are empty and the client must verify the emailed code.
    public bool RequiresOtp { get; set; }
    public Guid? ChallengeId { get; set; }
    public int ResendAfterSeconds { get; set; }
    public int CodeValidMinutes { get; set; }
    public string? MaskedEmail { get; set; }
    /// <summary>Which code the user must enter next: email or authenticator.</summary>
    public string? Method { get; set; }
    /// <summary>True when the account must set up its authenticator app before it can sign in.</summary>
    public bool RequiresEnrolment { get; set; }
    /// <summary>Shown exactly once, right after the authenticator is activated. Never stored in readable form.</summary>
    public List<string>? RecoveryCodes { get; set; }
}