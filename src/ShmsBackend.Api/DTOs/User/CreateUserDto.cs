using System;
using System.ComponentModel.DataAnnotations;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.DTOs.User;

public class CreateUserDto
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

    [Required]
    public UserType UserType { get; set; }

    // Admin specific
    public string? Department { get; set; }

    // Manager specific
    public string? ManagedDepartment { get; set; }
    public int? TeamSize { get; set; }

    // Accountant specific
    public string? LicenseNumber { get; set; }

    // Secretary specific
    public string? OfficeNumber { get; set; }
}