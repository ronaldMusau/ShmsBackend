using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Api.Services.Payment;

/// <summary>
/// The single place a vacating tenancy is closed out: tenant moves to the Vacated list (soft-deleted, login
/// revoked), the tenant-house history is closed and the house goes Vacant — unless a pre-registered
/// successor is already attached to it. Used by management's manual "Clear" and by the daily auto-close sweep.
/// </summary>
public class VacateClosureService
{
    private readonly ShmsDbContext _context;
    private readonly INotificationService _notificationService;
    private readonly IEmailService _emailService;
    private readonly ICacheHelper _cacheHelper;
    private readonly ILogger<VacateClosureService> _logger;

    public VacateClosureService(
        ShmsDbContext context,
        INotificationService notificationService,
        IEmailService emailService,
        ICacheHelper cacheHelper,
        ILogger<VacateClosureService> logger)
    {
        _context = context;
        _notificationService = notificationService;
        _emailService = emailService;
        _cacheHelper = cacheHelper;
        _logger = logger;
    }

    /// <summary>Unpaid rent due up to and including the vacate month (strictly overdue rows only).</summary>
    public async Task<decimal> GetArrearsAsync(VacateRequest vacateRequest, Tenant tenant)
    {
        var todayUtc = DateTime.UtcNow.Date;
        return await _context.Payments
            .Where(p => p.TenantId == vacateRequest.TenantId
                && p.TenancyCycle == tenant.TenancyCycle
                && !p.IsDeleted
                && p.DueDate < todayUtc
                && (p.Year < vacateRequest.VacateYear
                    || (p.Year == vacateRequest.VacateYear && p.Month <= vacateRequest.VacateMonth))
                && (p.PaymentStatus == PaymentTransactionStatus.Overdue
                    || p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid))
            .SumAsync(p => p.Balance);
    }

    /// <summary>True when the tenant owes nothing: no arrears and no unpaid tenant-owed settlement.
    /// A refund Management owes the tenant does not block closing.</summary>
    public async Task<bool> IsClearableAsync(VacateRequest vacateRequest, Tenant tenant)
    {
        if (await GetArrearsAsync(vacateRequest, tenant) > 0) return false;
        var settlement = await _context.VacateSettlements
            .FirstOrDefaultAsync(s => s.VacateRequestId == vacateRequest.Id && !s.IsVoided);
        // A settlement either way (tenant owes, or management refunds) must be marked paid before the tenant
        // is moved to Vacated. Refunds are paid outside the system and marked paid on the Refunds list.
        return settlement == null || settlement.PaidAt != null;
    }

    /// <summary>
    /// Closes one tenant straight away if they are in SettlingVacate and nothing is left to pay. Called when a
    /// refund is marked paid so the tenant doesn't wait for the next sweep. Returns true if closed.
    /// </summary>
    public async Task<bool> TryCloseForTenantAsync(Guid tenantId)
    {
        var tenant = await _context.Tenants.Include(t => t.House)
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted && t.TenantStatus == TenantStatus.SettlingVacate);
        if (tenant == null) return false;

        var request = await _context.VacateRequests
            .Where(v => v.TenantId == tenant.Id && !v.IsDeleted && v.Status == "Approved" && v.ClosedAt == null)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();
        if (request == null || !await IsClearableAsync(request, tenant)) return false;

        await CloseAsync(request, tenant, null);
        return true;
    }

    /// <summary>
    /// Frees the house of a tenant who has entered SettlingVacate (move-out month passed). The house becomes
    /// Vacant unless someone else (e.g. a pre-registered successor) is attached to it.
    /// </summary>
    public async Task ReleaseHouseAsync(Guid tenantId)
    {
        var tenant = await _context.Tenants.Include(t => t.House)
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted);
        if (tenant?.House == null) return;

        var hasOtherOccupant = await _context.Tenants.AnyAsync(t =>
            t.Id != tenant.Id && t.HouseId == tenant.House.Id && !t.IsDeleted
            && t.TenantStatus != TenantStatus.SettlingVacate);
        if (hasOtherOccupant) return;

        tenant.House.OccupancyStatus = OccupancyStatus.Vacant;
        tenant.House.PaymentStatus = PaymentStatus.NotPaid;
        tenant.House.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _cacheHelper.InvalidatePublicListingsCacheAsync();
    }

    public async Task CloseAsync(VacateRequest vacateRequest, Tenant tenant, Guid? closedByAdminId)
    {
        var houseNumber = "";

        if (tenant.House == null && tenant.HouseId.HasValue)
            tenant.House = await _context.Houses.FirstOrDefaultAsync(h => h.Id == tenant.HouseId.Value);

        if (tenant.House != null)
        {
            houseNumber = tenant.House.HouseNumber;

            // Anyone else attached to the house (e.g. a pre-registered successor, paid or not yet verified)
            // keeps it occupied — only the outgoing tenant is excused.
            var hasOtherOccupant = await _context.Tenants.AnyAsync(t =>
                t.Id != tenant.Id
                && t.HouseId == tenant.House.Id
                && !t.IsDeleted
                && t.TenantStatus != TenantStatus.SettlingVacate);

            if (!hasOtherOccupant)
            {
                tenant.House.OccupancyStatus = OccupancyStatus.Vacant;
                tenant.House.PaymentStatus = PaymentStatus.NotPaid;
                tenant.House.UpdatedAt = DateTime.UtcNow;
            }
        }

        var openHistory = await _context.TenantHouseHistories
            .Where(h => h.TenantId == tenant.Id && h.RemovedAt == null)
            .FirstOrDefaultAsync();
        if (openHistory != null)
            openHistory.RemovedAt = DateTime.UtcNow;

        tenant.IsDeleted = true;
        tenant.DeletedAt = DateTime.UtcNow;
        tenant.IsActive = false;
        // Revoke the session so the vacated tenant cannot refresh into the portal.
        tenant.RefreshToken = null;
        tenant.RefreshTokenExpiryTime = null;

        vacateRequest.ClosedAt = DateTime.UtcNow;
        vacateRequest.ClosedByAdminId = closedByAdminId;

        await _context.SaveChangesAsync();
        await _cacheHelper.InvalidatePublicListingsCacheAsync();

        try { await _notificationService.SendToUserAsync(tenant.Id.ToString(), "Your tenancy has been formally closed out. Thank you for your tenancy.", "property", "Vacate", vacateRequest.Id.ToString()); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify tenant of vacate clearance"); }

        try
        {
            await _notificationService.SendToRolesAsync(
                new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Secretary, NotificationAudience.Manager },
                $"Vacate request for house {houseNumber} has been fully closed out.",
                "property", "Vacate", vacateRequest.Id.ToString());
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to notify management of vacate clearance"); }

        try
        {
            var superAdmins = await _context.SuperAdmins.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var adminUsers = await _context.AdminUsers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var managers = await _context.Managers.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var secretaries = await _context.Secretaries.Select(u => new { u.Id, u.Email, u.FirstName }).ToListAsync();
            var managementUsers = superAdmins.Concat(adminUsers).Concat(managers).Concat(secretaries).ToList();
            foreach (var mgr in managementUsers)
            {
                try { await _emailService.SendVacateClosedManagementEmailAsync(mgr.Email, mgr.FirstName, houseNumber, mgr.Id.ToString(), false); }
                catch (Exception ex) { _logger.LogError(ex, "Failed to send vacate closed email to {Email}", ShmsBackend.Api.Common.LogMask.Email(mgr.Email)); }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to query management users for vacate closed email"); }
    }

    /// <summary>
    /// Daily sweep: a tenant whose vacate month has passed (status SettlingVacate), whose approved vacate has
    /// nothing left to pay, is closed out automatically — moved to Vacated, house freed. Returns how many closed.
    /// </summary>
    public async Task<int> RunAutoCloseSweepAsync()
    {
        var settling = await _context.Tenants
            .Include(t => t.House)
            .Where(t => t.TenantStatus == TenantStatus.SettlingVacate && !t.IsDeleted)
            .ToListAsync();

        var closed = 0;
        foreach (var tenant in settling)
        {
            try
            {
                var request = await _context.VacateRequests
                    .Where(v => v.TenantId == tenant.Id && !v.IsDeleted && v.Status == "Approved" && v.ClosedAt == null)
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstOrDefaultAsync();
                if (request == null) continue;

                // Idempotent: makes sure the house is released even for tenants flipped before this rule existed.
                await ReleaseHouseAsync(tenant.Id);

                if (!await IsClearableAsync(request, tenant)) continue;

                await CloseAsync(request, tenant, null);
                closed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-close failed for tenant {TenantId}", tenant.Id);
            }
        }

        _logger.LogInformation("Vacate auto-close sweep closed {Count} tenancy(ies)", closed);
        return closed;
    }
}
