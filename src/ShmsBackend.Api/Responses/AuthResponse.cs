using System;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.Responses;

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public UserType UserType { get; set; }  // Single type instead of list
    public DateTime ExpiresAt { get; set; }
}