using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.DTOs.Auth;

public class PreAuthResponseDto
{
    public string Email { get; set; } = string.Empty;
    public UserType SelectedUserType { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}