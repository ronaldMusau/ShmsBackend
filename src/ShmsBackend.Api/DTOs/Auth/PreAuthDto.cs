using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class PreAuthDto
{
    public string Email { get; set; } = string.Empty;
    public UserType SelectedUserType { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool RequiresOtp { get; set; } = true;
}