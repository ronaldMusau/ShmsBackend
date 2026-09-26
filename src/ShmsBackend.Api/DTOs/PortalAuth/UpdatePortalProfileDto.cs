using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.PortalAuth;

public class UpdatePortalProfileDto
{
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    public string? NewEmail { get; set; }

    // Explorer-only (their declared area of interest for browsing listings) — silently ignored for
    // every other role, see UpdateProfileAsync.
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }
}
