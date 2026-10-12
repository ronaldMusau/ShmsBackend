using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

// Approval-sequence ApproverIds can point at either a portal user or a staff account (Admins table) —
// in practice they're usually staff. Every approver lookup has to check both tables.
public static class ApproverLookup
{
    public record ApproverContact(Guid Id, string Email, string FirstName, bool IsPortalUser);

    // Display names for approval history. PortalUsers first, then Admins for the rest; Admins ignores the
    // soft-delete filter so an approver deleted later still shows by name. Ids found in neither are omitted.
    public static async Task<Dictionary<Guid, string>> GetNamesAsync(ShmsDbContext context, IEnumerable<Guid> approverIds)
    {
        var ids = approverIds.Distinct().ToList();
        var names = await context.PortalUsers
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}");

        var adminIds = ids.Where(id => !names.ContainsKey(id)).ToList();
        if (adminIds.Count > 0)
        {
            var admins = await context.Admins
                .IgnoreQueryFilters()
                .Where(u => adminIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToListAsync();
            foreach (var admin in admins)
                names[admin.Id] = $"{admin.FirstName} {admin.LastName}";
        }

        return names;
    }

    // Who to email for a "requires your approval" step: PortalUsers first, then Admins. Soft-deleted accounts are
    // excluded (default query filters) — nobody should be emailed on a deleted account. IsPortalUser goes through to
    // the email service, which keys notification preferences and palette on it.
    public static async Task<ApproverContact?> GetContactAsync(ShmsDbContext context, Guid approverId)
    {
        var portalUser = await context.PortalUsers
            .Where(u => u.Id == approverId)
            .Select(u => new ApproverContact(u.Id, u.Email, u.FirstName, true))
            .FirstOrDefaultAsync();
        if (portalUser != null)
            return portalUser;

        return await context.Admins
            .Where(u => u.Id == approverId)
            .Select(u => new ApproverContact(u.Id, u.Email, u.FirstName, false))
            .FirstOrDefaultAsync();
    }
}
