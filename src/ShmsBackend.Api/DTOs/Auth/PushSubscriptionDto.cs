using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Auth;

/// <summary>
/// A browser Web Push subscription payload sent by the frontend after the user grants
/// notification permission. Mirrors the shape of the browser's PushSubscription
/// (endpoint + keys.p256dh + keys.auth). UserId / IsPortalUser are resolved server-side
/// from the token.
/// </summary>
public class PushSubscriptionDto
{
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public PushSubscriptionKeysDto Keys { get; set; } = new();
}

public class PushSubscriptionKeysDto
{
    [Required]
    public string P256dh { get; set; } = string.Empty;

    [Required]
    public string Auth { get; set; } = string.Empty;
}
