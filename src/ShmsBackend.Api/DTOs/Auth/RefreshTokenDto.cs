using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class RefreshTokenDto
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}