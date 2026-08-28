using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.DTOs.Auth;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Notifications;

public class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly ShmsDbContext _context;

    public NotificationPreferenceService(ShmsDbContext context)
    {
        _context = context;
    }

    public async Task<NotificationPreference> GetOrCreateAsync(Guid userId, bool isPortalUser)
    {
        var pref = await _context.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.IsPortalUser == isPortalUser);

        if (pref != null)
            return pref;

        pref = new NotificationPreference
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IsPortalUser = isPortalUser,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
            // all channel flags default to true via the entity's property initializers
        };

        _context.NotificationPreferences.Add(pref);
        await _context.SaveChangesAsync();
        return pref;
    }

    public async Task<NotificationPreference> UpdateAsync(Guid userId, bool isPortalUser, NotificationPreferenceDto dto)
    {
        var pref = await GetOrCreateAsync(userId, isPortalUser);

        pref.MasterEmailEnabled = dto.MasterEmailEnabled;
        pref.MasterInAppEnabled = dto.MasterInAppEnabled;
        pref.RentEmailEnabled = dto.RentEmailEnabled;
        pref.RentInAppEnabled = dto.RentInAppEnabled;
        pref.ComplaintsEmailEnabled = dto.ComplaintsEmailEnabled;
        pref.ComplaintsInAppEnabled = dto.ComplaintsInAppEnabled;
        pref.ApprovalsEmailEnabled = dto.ApprovalsEmailEnabled;
        pref.ApprovalsInAppEnabled = dto.ApprovalsInAppEnabled;
        pref.PropertiesEmailEnabled = dto.PropertiesEmailEnabled;
        pref.PropertiesInAppEnabled = dto.PropertiesInAppEnabled;
        pref.AccountEmailEnabled = dto.AccountEmailEnabled;
        pref.AccountInAppEnabled = dto.AccountInAppEnabled;
        pref.TeamActivityEmailEnabled = dto.TeamActivityEmailEnabled;
        pref.TeamActivityInAppEnabled = dto.TeamActivityInAppEnabled;
        pref.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return pref;
    }
}
