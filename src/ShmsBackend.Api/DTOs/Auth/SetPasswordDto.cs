using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class SetPasswordDto
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Only required when flow = "new-user" (email verification after account creation).
    /// Not required for "forgot-password" flow since user doesn't remember it.
    /// </summary>
    public string? CurrentPassword { get; set; }

    /// <summary>
    /// Indicates which flow triggered this. Values: "new-user" | "forgot-password"
    /// </summary>
    [Required]
    public string Flow { get; set; } = "new-user";

    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare("NewPassword")]
    public string ConfirmPassword { get; set; } = string.Empty;
}