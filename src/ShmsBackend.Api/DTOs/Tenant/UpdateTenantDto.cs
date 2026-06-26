using System;

namespace ShmsBackend.Api.Models.DTOs.Tenant;

public class UpdateTenantDto
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool? IsActive { get; set; }
    public string? NationalId { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
}
