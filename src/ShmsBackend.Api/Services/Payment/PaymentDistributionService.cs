using Microsoft.EntityFrameworkCore;
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

    public PaymentDistributionService(ShmsDbContext context)
    {
        _context = context;
    }

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

        var serviceCharge = await GetServiceChargeAsync(house.RentFee);
        var cursorMonth = currentDate.Month;
        var cursorYear = currentDate.Year;
        var totalIterations = 0;

        while (remaining > 0)
        {
            if (++totalIterations > 60) break;
            if (itemized.Count > 36) break;

            var existingMonthRow = await _context.Payments.FirstOrDefaultAsync(p =>
                p.TenantId == tenantId && p.HouseId == houseId
                && p.TenancyCycle == tenancyCycle
                && p.Month == cursorMonth && p.Year == cursorYear && !p.IsDeleted);

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
            else if (existingMonthRow == null)
            {
                // No row yet — create one
                var monthlyTotal = house.RentFee;
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
                    RentAmount = house.RentFee,
                    ServiceChargeAmount = serviceCharge,
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
        return itemized;
    }
}
