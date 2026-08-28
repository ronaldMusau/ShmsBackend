using System;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Notifications;

public interface INotificationPreferenceService
{
    // Loads the user's preference row, or lazily creates one with all defaults = true.
    Task<NotificationPreference> GetOrCreateAsync(Guid userId, bool isPortalUser);

    // Loads or creates the row, applies every field from the dto, stamps UpdatedAt, saves.
    Task<NotificationPreference> UpdateAsync(Guid userId, bool isPortalUser, NotificationPreferenceDto dto);
}
