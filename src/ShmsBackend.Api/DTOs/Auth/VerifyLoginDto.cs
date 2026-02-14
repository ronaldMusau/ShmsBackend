using System.ComponentModel.DataAnnotations;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class VerifyLoginDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public UserType SelectedUserType { get; set; }  // Added this field

    [Required]
    public string Password { get; set; } = string.Empty;  // Added password field

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Otp { get; set; } = string.Empty;
}