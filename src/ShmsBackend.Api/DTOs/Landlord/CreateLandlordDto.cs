using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Landlord;

public class CreateLandlordDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    public string LastName { get; set; } = string.Empty;

    [Phone]
    public string? PhoneNumber { get; set; }

    public string? NationalId { get; set; }

    public string? AgencyName { get; set; }
}
