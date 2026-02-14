using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class RequestPasswordResetDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}