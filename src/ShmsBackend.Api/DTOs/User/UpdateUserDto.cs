using System;
using System.ComponentModel.DataAnnotations;
using ShmsBackend.Data.Enums;

namespace ShmsBackend.Api.Models.DTOs.User;

public class UpdateUserDto
{
    [EmailAddress]
    public string? Email { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    [Phone]
    public string? PhoneNumber { get; set; }

    public bool? IsActive { get; set; }

    // Only SuperAdmin can update UserType
    public UserType? UserType { get; set; }

    // Role-specific properties (only updatable by SuperAdmin/Admin)
    public string? Department { get; set; }      // For Admin
    public string? ManagedDepartment { get; set; } // For Manager
    public int? TeamSize { get; set; }            // For Manager
    public string? LicenseNumber { get; set; }    // For Accountant
    public string? OfficeNumber { get; set; }     // For Secretary
}