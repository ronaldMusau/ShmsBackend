using System.ComponentModel.DataAnnotations;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class LoginDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public UserType SelectedUserType { get; set; }  // User selects which role to login as
}