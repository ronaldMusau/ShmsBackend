using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Enums;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Payment;

// Vacate-aware "how much can this tenant still pay" logic. Depends only on the DbContext so RewardService can
// use it directly — RewardService can't take IPaymentService (PaymentService already depends on IRewardService).
// IPaymentService exposes the same methods by delegating here.
public class VacateOwedCalculator
{
    private readonly ShmsDbContext _context;

    public VacateOwedCalculator(ShmsDbContext context)
    {
        _context = context;
    }

    // An approved vacate that hasn't been cleared yet. ClearVacateRequest only sets ClosedAt (Status stays
    // "Approved"), so ClosedAt — not Status — is what marks it finished.
    public async Task<VacateRequest?> GetActiveApprovedVacateRequestAsync(Guid tenantId)
    {
        return await _context.VacateRequests
            .Where(v => v.TenantId == tenantId && v.Status == "Approved" && v.ClosedAt == null && !v.IsDeleted)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();
    }

    // The unapplied rent change in force by the given month: the latest one effective on or before it.
    // GenerateMonthlyPaymentsAsync applies every due change to House.RentFee before billing a month, so the
    // rate it bills is this change's NewRentFee, or the current House.RentFee when there's none.
    public async Task<PendingRentChange?> GetApplicableRentChangeAsync(Guid houseId, int month, int year)
    {
        return await _context.PendingRentChanges
            .Where(pc => pc.HouseId == houseId && pc.AppliedAt == null
                && (pc.EffectiveYear < year || (pc.EffectiveYear == year && pc.EffectiveMonth <= month)))
            .OrderByDescending(pc => pc.EffectiveYear)
            .ThenByDescending(pc => pc.EffectiveMonth)
            .FirstOrDefaultAsync();
    }

    // Rent still owed up to and including the through-month, net of what's paid — the mirror of
    // PaymentService.GetForfeitableAdvanceAsync. Both parts stop at the through-month: a row after it is never
    // owed (it's forfeited at settlement), even once its due date has passed.
    //  - Overdue: existing rows whose due date has passed.
    //  - Remaining: existing rows not yet due, plus an estimate for each month from the current month through
    //    the through-month that has no row yet, billed the way GenerateMonthlyPaymentsAsync would bill it.
    public async Task<VacateOwedBreakdown> GetOwedThroughMonthAsync(
        Guid tenantId, Guid houseId, int tenancyCycle, int throughMonth, int throughYear)
    {
        var today = DateTime.UtcNow.Date;

        var rowsThrough = _context.Payments.Where(p =>
            p.TenantId == tenantId
            && p.HouseId == houseId
            && p.TenancyCycle == tenancyCycle
            && !p.IsDeleted
            && p.PaymentStatus != PaymentTransactionStatus.Cancelled
            && p.PaymentStatus != PaymentTransactionStatus.Failed
            && (p.Year < throughYear || (p.Year == throughYear && p.Month <= throughMonth)));

        var overdue = await rowsThrough
            .Where(p => p.DueDate < today)
            .SumAsync(p => p.Amount - p.AmountPaid > 0 ? p.Amount - p.AmountPaid : 0m);

        var remaining = await rowsThrough
            .Where(p => p.DueDate >= today)
            .SumAsync(p => p.Amount - p.AmountPaid > 0 ? p.Amount - p.AmountPaid : 0m);

        remaining += await EstimateUnbilledMonthsAsync(tenantId, houseId, tenancyCycle, throughMonth, throughYear, today);

        return new VacateOwedBreakdown { Overdue = overdue, Remaining = remaining, Total = overdue + remaining };
    }

    private async Task<decimal> EstimateUnbilledMonthsAsync(
        Guid tenantId, Guid houseId, int tenancyCycle, int throughMonth, int throughYear, DateTime today)
    {
        var house = await _context.Houses.AsNoTracking().FirstOrDefaultAsync(h => h.Id == houseId);
        if (house == null) return 0m;

        var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
        var vacate = await GetActiveApprovedVacateRequestAsync(tenantId);
        var skipThroughMonth = vacate != null && vacate.SitDeposit
            && vacate.VacateMonth == throughMonth && vacate.VacateYear == throughYear;

        // Same "month already covered" check as GenerateMonthlyPaymentsAsync — any non-deleted row, initial included.
        var billedMonths = (await _context.Payments
            .Where(p => p.TenantId == tenantId && p.HouseId == houseId && p.TenancyCycle == tenancyCycle && !p.IsDeleted)
            .Select(p => new { p.Year, p.Month })
            .ToListAsync())
            .Select(p => (p.Year, p.Month))
            .ToHashSet();

        var estimate = 0m;
        var cursorMonth = today.Month;
        var cursorYear = today.Year;
        while (cursorYear < throughYear || (cursorYear == throughYear && cursorMonth <= throughMonth))
        {
            var isThroughMonth = cursorMonth == throughMonth && cursorYear == throughYear;
            // GenerateMonthlyPaymentsAsync bills nothing before the lease start, nor the vacate month when the
            // deposit sits for it.
            var beforeLeaseStart = tenant?.LeaseStartMonth != null && tenant.LeaseStartYear != null
                && (cursorYear < tenant.LeaseStartYear.Value
                    || (cursorYear == tenant.LeaseStartYear.Value && cursorMonth < tenant.LeaseStartMonth.Value));

            if (!billedMonths.Contains((cursorYear, cursorMonth)) && !beforeLeaseStart && !(isThroughMonth && skipThroughMonth))
            {
                var rentChange = await GetApplicableRentChangeAsync(houseId, cursorMonth, cursorYear);
                estimate += rentChange?.NewRentFee ?? house.RentFee;
            }

            cursorMonth++;
            if (cursorMonth > 12) { cursorMonth = 1; cursorYear++; }
        }

        return estimate;
    }
}
