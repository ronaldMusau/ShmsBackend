using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.PortalAuth;

public class PortalRefreshTokenDto
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
