using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Common;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using PaymentRecord = ShmsBackend.Data.Models.Entities.Portal.Payment;

namespace ShmsBackend.Api.Services.Payment;

// Extracted out of PaymentService so RewardService can reuse the same arrears-first
// distribution logic without depending on PaymentService (which itself depends on
// IRewardService to award points on payment) — that pairing would be a circular dependency.
public interface IPaymentDistributionService
{
    Task<List<(int month, int year, decimal applied)>> DistributePaymentAsync(
        Guid tenantId, Guid houseId, decimal excessAmount, int tenancyCycle,
        string? receiptNumber = null, string? checkoutRequestId = null, Guid? excludePaymentId = null,
        string? redemptionReference = null);
}

public class PaymentDistributionService : IPaymentDistributionService
{
    private readonly ShmsDbContext _context;
    private readonly ICacheHelper _cacheHelper;
    private readonly VacateOwedCalculator _vacateOwedCalculator;
    private readonly ILogger<PaymentDistributionService> _logger;

    public PaymentDistributionService(
        ShmsDbContext context,
        ICacheHelper cacheHelper,
        VacateOwedCalculator vacateOwedCalculator,
        ILogger<PaymentDistributionService> logger)
    {
        _context = context;
        _cacheHelper = cacheHelper;
        _vacateOwedCalculator = vacateOwedCalculator;
        _logger = logger;
    }

    private static bool IsAfterMonth(int year, int month, int anchorYear, int anchorMonth)
        => year > anchorYear || (year == anchorYear && month > anchorMonth);

    private async Task<decimal> GetServiceChargeAsync(decimal rentAmount)
    {
        var setting = await _context.ServiceChargeSettings
            .Where(s => s.IsActive && !s.IsDeleted &&
                        s.MinRent <= rentAmount && s.MaxRent >= rentAmount)
            .OrderBy(s => s.MinRent)
            .FirstOrDefaultAsync();
        return setting?.ServiceCharge ?? 0;
    }

    public async Task<List<(int month, int year, decimal applied)>> DistributePaymentAsync(
        Guid tenantId, Guid houseId, decimal excessAmount, int tenancyCycle,
        string? receiptNumber = null, string? checkoutRequestId = null, Guid? excludePaymentId = null,
        string? redemptionReference = null)
    {
        var remaining = excessAmount;
        var itemized = new List<(int month, int year, decimal applied)>();
        var currentDate = DateTime.UtcNow;

        var existingUnpaid = await _context.Payments
            .Where(p => p.TenantId == tenantId && p.HouseId == houseId
                     && p.TenancyCycle == tenancyCycle
                     && p.Balance > 0 && !p.IsInitialPayment && !p.IsDeleted
                     && p.Id != excludePaymentId)
            .OrderBy(p => p.Year).ThenBy(p => p.Month)
            .ToListAsync();

        // With an approved vacate, money only ever goes to months up to and including the vacate month: rows
        // after it are skipped and never created. Without one, nothing below changes.
        var activeVacate = await _vacateOwedCalculator.GetActiveApprovedVacateRequestAsync(tenantId);
        if (activeVacate != null)
            existingUnpaid = existingUnpaid
                .Where(p => !IsAfterMonth(p.Year, p.Month, activeVacate.VacateYear, activeVacate.VacateMonth))
                .ToList();

        var house = await _context.Houses
            .Include(h => h.Flat)
            .FirstOrDefaultAsync(h => h.Id == houseId)
            ?? throw new Exception($"House {houseId} not found during payment distribution");

        foreach (var row in existingUnpaid)
        {
            if (remaining <= 0) break;
            var applyAmount = Math.Min(remaining, row.Balance);
            row.AmountPaid += applyAmount;
            row.Balance = Math.Max(0, row.Amount - row.AmountPaid);
            row.PaymentStatus = row.Balance <= 0
                ? PaymentTransactionStatus.Paid
                : PaymentTransactionStatus.PartiallyPaid;
            if (!string.IsNullOrEmpty(receiptNumber) && applyAmount > 0)
            {
                row.MpesaReceiptNumber = receiptNumber;
                row.PaidAt = DateTime.UtcNow;
                _context.PaymentApplications.Add(new PaymentApplication
                {
                    PaymentId = row.Id,
                    MpesaReceiptNumber = receiptNumber,
                    AmountApplied = applyAmount,
                    CheckoutRequestId = checkoutRequestId,
                    AppliedAt = DateTime.UtcNow
                });
            }
            else if (!string.IsNullOrEmpty(redemptionReference) && applyAmount > 0)
            {
                row.RedemptionReference = redemptionReference;
                row.PaymentMethod = PaymentMethod.PointsRedemption;
                row.PaidAt = DateTime.UtcNow;
                // do NOT create a PaymentApplication row here — that entity is receipt-specific;
                // the RewardTransaction ledger is the audit trail for redemptions.
            }
            remaining -= applyAmount;
            itemized.Add((row.Month, row.Year, applyAmount));
        }

        var cursorMonth = currentDate.Month;
        var cursorYear = currentDate.Year;
        var totalIterations = 0;

        while (remaining > 0)
        {
            if (++totalIterations > 60) break;
            if (itemized.Count > 36) break;
            if (activeVacate != null
                && IsAfterMonth(cursorYear, cursorMonth, activeVacate.VacateYear, activeVacate.VacateMonth))
                break;

            var existingMonthRow = await _context.Payments.FirstOrDefaultAsync(p =>
                p.TenantId == tenantId && p.HouseId == houseId
                && p.TenancyCycle == tenancyCycle
                && p.Month == cursorMonth && p.Year == cursorYear && !p.IsDeleted);

            // The deposit sits for the vacate month, so (like GenerateMonthlyPaymentsAsync) never bill a new
            // row for it — an existing one with a balance is still paid down above.
            var isDepositCoveredVacateMonth = activeVacate != null && activeVacate.SitDeposit
                && cursorMonth == activeVacate.VacateMonth && cursorYear == activeVacate.VacateYear;

            if (existingMonthRow != null && existingMonthRow.Balance > 0)
            {
                // Existing unpaid row — apply funds to it rather than creating a duplicate
                var applyAmount = Math.Min(remaining, existingMonthRow.Balance);
                existingMonthRow.AmountPaid += applyAmount;
                existingMonthRow.Balance = Math.Max(0, existingMonthRow.Amount - existingMonthRow.AmountPaid);
                existingMonthRow.PaymentStatus = existingMonthRow.Balance <= 0
                    ? PaymentTransactionStatus.Paid
                    : PaymentTransactionStatus.PartiallyPaid;
                if (!string.IsNullOrEmpty(receiptNumber) && applyAmount > 0)
                {
                    existingMonthRow.MpesaReceiptNumber = receiptNumber;
                    existingMonthRow.PaidAt = DateTime.UtcNow;
                    _context.PaymentApplications.Add(new PaymentApplication
                    {
                        PaymentId = existingMonthRow.Id,
                        MpesaReceiptNumber = receiptNumber,
                        AmountApplied = applyAmount,
                        CheckoutRequestId = checkoutRequestId,
                        AppliedAt = DateTime.UtcNow
                    });
                }
                else if (!string.IsNullOrEmpty(redemptionReference) && applyAmount > 0)
                {
                    existingMonthRow.RedemptionReference = redemptionReference;
                    existingMonthRow.PaymentMethod = PaymentMethod.PointsRedemption;
                    existingMonthRow.PaidAt = DateTime.UtcNow;
                }
                remaining -= applyAmount;
                itemized.Add((cursorMonth, cursorYear, applyAmount));
            }
            else if (existingMonthRow == null && !isDepositCoveredVacateMonth)
            {
                // No row yet — create one, honoring any rent change effective for this specific month (same
                // "latest applicable, never retroactive" rule CreateInitialPaymentAsync uses) — service charge
                // is tiered by rent amount, so it's recomputed from the effective rate too, not house.RentFee.
                var rentChange = await _vacateOwedCalculator.GetApplicableRentChangeAsync(houseId, cursorMonth, cursorYear);
                var monthlyTotal = rentChange?.NewRentFee ?? house.RentFee;
                var monthlyServiceCharge = await GetServiceChargeAsync(monthlyTotal);
                var applyAmount = Math.Min(remaining, monthlyTotal);
                var newPayment = new PaymentRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    HouseId = houseId,
                    FlatId = house.FlatId,
                    LandlordId = house.Flat?.LandlordId,
                    TenancyCycle = tenancyCycle,
                    Amount = monthlyTotal,
                    AmountPaid = applyAmount,
                    Balance = monthlyTotal - applyAmount,
                    RentAmount = monthlyTotal,
                    ServiceChargeAmount = monthlyServiceCharge,
                    Month = cursorMonth,
                    Year = cursorYear,
                    IsInitialPayment = false,
                    PaymentType = PaymentType.Rent,
                    DueDate = new DateTime(cursorYear, cursorMonth, Math.Min(house.Flat!.RentDueDay, DateTime.DaysInMonth(cursorYear, cursorMonth))),
                    PaymentStatus = applyAmount >= monthlyTotal
                        ? PaymentTransactionStatus.Paid
                        : applyAmount > 0
                            ? PaymentTransactionStatus.PartiallyPaid
                            : PaymentTransactionStatus.Pending,
                    Description = $"Monthly rent - {new DateTime(cursorYear, cursorMonth, 1):MMMM yyyy}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Payments.Add(newPayment);
                if (!string.IsNullOrEmpty(receiptNumber) && applyAmount > 0)
                {
                    newPayment.MpesaReceiptNumber = receiptNumber;
                    newPayment.PaymentMethod = PaymentMethod.Mpesa;
                    newPayment.PaidAt = DateTime.UtcNow;
                    _context.PaymentApplications.Add(new PaymentApplication
                    {
                        PaymentId = newPayment.Id,
                        MpesaReceiptNumber = receiptNumber,
                        AmountApplied = applyAmount,
                        CheckoutRequestId = checkoutRequestId,
                        AppliedAt = DateTime.UtcNow
                    });
                }
                else if (!string.IsNullOrEmpty(redemptionReference) && applyAmount > 0)
                {
                    newPayment.RedemptionReference = redemptionReference;
                    newPayment.PaymentMethod = PaymentMethod.PointsRedemption;
                    newPayment.PaidAt = DateTime.UtcNow;
                }
                remaining -= applyAmount;
                itemized.Add((cursorMonth, cursorYear, applyAmount));
            }
            // else: existingMonthRow.Balance <= 0 — month fully settled, advance cursor only

            cursorMonth++;
            if (cursorMonth > 12) { cursorMonth = 1; cursorYear++; }
        }

        // Upstream caps (self-pay, pay-on-behalf, retry, redemption) should leave nothing here. If something is
        // left anyway, it is received money sitting on no Payment row — surface it for manual review rather than
        // dropping it silently or billing a post-vacate month. Two distinct causes, both logged: capped by an
        // approved vacate month, or capped by the loop's own iteration/row-count safety limits (an
        // unrealistically large overpayment with no vacate involved).
        if (remaining > 0)
        {
            if (activeVacate != null)
                _logger.LogError(
                    "Undistributed remainder of KES {Remaining:N2} for tenant {TenantId}, house {HouseId}, cycle {TenancyCycle}: " +
                    "nothing left owing through approved vacate month {VacateMonth}/{VacateYear} (vacate request {VacateRequestId}). " +
                    "Receipt {ReceiptNumber}, checkout {CheckoutRequestId}, redemption {RedemptionReference}. Needs manual review.",
                    remaining, tenantId, houseId, tenancyCycle, activeVacate.VacateMonth, activeVacate.VacateYear, activeVacate.Id,
                    receiptNumber, checkoutRequestId, redemptionReference);
            else
                _logger.LogError(
                    "Undistributed remainder of KES {Remaining:N2} for tenant {TenantId}, house {HouseId}, cycle {TenancyCycle}: " +
                    "distribution stopped after {Iterations} iterations / {ItemizedCount} rows without exhausting the amount. " +
                    "Receipt {ReceiptNumber}, checkout {CheckoutRequestId}, redemption {RedemptionReference}. Needs manual review.",
                    remaining, tenantId, houseId, tenancyCycle, totalIterations, itemized.Count,
                    receiptNumber, checkoutRequestId, redemptionReference);
        }

        var currentMonthPayment = await _context.Payments
            .Where(p => p.TenantId == tenantId && p.HouseId == houseId && p.TenancyCycle == tenancyCycle
                && p.Month == currentDate.Month && p.Year == currentDate.Year
                && !p.IsInitialPayment && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (currentMonthPayment != null)
        {
            house.PaymentStatus = currentMonthPayment.Balance <= 0
                ? PaymentStatus.Paid
                : (currentMonthPayment.DueDate != default && currentMonthPayment.DueDate < currentDate.AddDays(-3)
                    ? PaymentStatus.Overdue
                    : PaymentStatus.PartiallyPaid);
            house.OccupancyStatus = OccupancyStatus.Occupied;
            house.UpdatedAt = currentDate;
        }

        await _context.SaveChangesAsync();
        await _cacheHelper.InvalidatePublicListingsCacheAsync();
        return itemized;
    }
}
