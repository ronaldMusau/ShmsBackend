using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Helpers;

/// <summary>Small helpers for complaint lifecycle emails (best-effort — never throw).</summary>
public static class ComplaintMail
{
    public static async Task ToManagementAsync(ShmsDbContext ctx, IEmailService email, ILogger logger, string ticket, string subject, string message)
    {
        try
        {
            var superAdmins = await ctx.SuperAdmins.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var admins = await ctx.AdminUsers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var managers = await ctx.Managers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var secretaries = await ctx.Secretaries.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            foreach (var m in superAdmins.Concat(admins).Concat(managers).Concat(secretaries))
            {
                try { await email.SendComplaintUpdateEmailAsync(m.Email, m.FirstName, ticket, subject, message, m.Id.ToString(), false); }
                catch (Exception ex) { logger.LogError(ex, "Failed to send complaint update email to {Email}", m.Email); }
            }
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to load management recipients for complaint email"); }
    }

    public static async Task ToTenantAsync(ShmsDbContext ctx, IEmailService email, ILogger logger, Guid tenantId, string ticket, string subject, string message)
    {
        try
        {
            var t = await ctx.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tenantId);
            if (t != null) await email.SendComplaintUpdateEmailAsync(t.Email, t.FirstName, ticket, subject, message, t.Id.ToString(), true);
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to send complaint update email to tenant"); }
    }

    public static async Task ToAgentAsync(ShmsDbContext ctx, IEmailService email, ILogger logger, Guid agentId, string ticket, string subject, string message)
    {
        try
        {
            var a = await ctx.Agents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == agentId);
            if (a != null) await email.SendComplaintUpdateEmailAsync(a.Email, a.FirstName, ticket, subject, message, a.Id.ToString(), true);
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to send complaint update email to agent"); }
    }
}
