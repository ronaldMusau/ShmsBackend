using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.PortalAuth;

public class PortalChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}
