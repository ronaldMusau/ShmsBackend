using System.ComponentModel.DataAnnotations;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class LoginDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public UserType SelectedUserType { get; set; }  // User selects which role to login as

    /// <summary>True only from the separate staff sign-in page. SuperAdmin and Admin can only sign in there; everyone else only on the public page.</summary>
    public bool StaffPortal { get; set; }
}