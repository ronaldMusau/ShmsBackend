using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShmsBackend.Api.Services.Auth;

public interface IWeeklyPasswordService
{
    /// <summary>
    /// Generates a new strong random password, deactivates the current active row, inserts a new
    /// active row (hash only, 7-day expiry), caches the plaintext in memory for the cycle, and emails
    /// every current subscriber the new plaintext. The plaintext is never returned to callers.
    /// </summary>
    Task GenerateAndRotateAsync();

    /// <summary>Hash of the currently active weekly password, or null if none exists/active.</summary>
    Task<string?> GetCurrentPasswordHashAsync();

    /// <summary>Whether a subscriber row exists for this admin.</summary>
    Task<bool> IsSubscribedAsync(Guid adminId);

    /// <summary>Current subscribers, joined to their Admin details.</summary>
    Task<IReadOnlyList<WeeklyPasswordSubscriberDto>> GetSubscribersAsync();

    /// <summary>Every non-deleted admin with a flag for whether they're currently subscribed.</summary>
    Task<IReadOnlyList<EligibleAdminDto>> GetAllEligibleAdminsAsync();

    /// <summary>
    /// Opt an admin in or out. On opt-in, if the current plaintext is cached in memory, immediately
    /// emails that admin this week's password so they don't have to wait for the next rotation.
    /// </summary>
    Task SetSubscriptionAsync(Guid adminId, bool subscribe);
}

public class WeeklyPasswordSubscriberDto
{
    public Guid AdminId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime SubscribedAt { get; set; }
}

public class EligibleAdminDto
{
    public Guid AdminId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsSubscribed { get; set; }
}
