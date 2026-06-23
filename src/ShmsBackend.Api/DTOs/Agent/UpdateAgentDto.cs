namespace ShmsBackend.Api.Models.DTOs.Agent;

public class UpdateAgentDto
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }
    public bool? IsActive { get; set; }
    public string? AgencyName { get; set; }
    public string? LicenseNumber { get; set; }
}
