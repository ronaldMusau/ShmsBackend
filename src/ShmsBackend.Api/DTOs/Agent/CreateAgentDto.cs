using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Agent;

public class CreateAgentDto
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

    public DateTime? DateOfBirth { get; set; }

    public string? LicenseNumber { get; set; }

    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }

    public List<Guid> FlatIds { get; set; } = new();
}
