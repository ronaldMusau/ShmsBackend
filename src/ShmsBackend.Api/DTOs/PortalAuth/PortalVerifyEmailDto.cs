using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.PortalAuth;

public class PortalVerifyEmailDto
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
