using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.PortalAuth;

public class UpdatePortalProfileDto
{
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    public string? NewEmail { get; set; }
}
