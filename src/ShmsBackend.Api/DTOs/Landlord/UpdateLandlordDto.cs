namespace ShmsBackend.Api.Models.DTOs.Landlord;

public class UpdateLandlordDto
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool? IsActive { get; set; }
    public string? NationalId { get; set; }
    public string? AgencyName { get; set; }
}
