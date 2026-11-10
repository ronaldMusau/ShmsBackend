using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Security;

/// <summary>Shows staff by name in approval trails and notifications, falling back to email then role.</summary>
public static class StaffName
{
    public static async Task<string> ResolveAsync(ShmsDbContext db, ClaimsPrincipal user)
    {
        var fallback = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue(ClaimTypes.Role) ?? "staff";
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return fallback;
        var n = await db.Admins.AsNoTracking().Where(a => a.Id == id).Select(a => (a.FirstName + " " + a.LastName).Trim()).FirstOrDefaultAsync();
        return string.IsNullOrWhiteSpace(n) ? fallback : n;
    }
}
