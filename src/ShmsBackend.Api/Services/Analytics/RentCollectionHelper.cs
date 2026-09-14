using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PaymentEntity = ShmsBackend.Data.Models.Entities.Portal.Payment;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Analytics;

public class FlatCollectionRow
{
    public Guid FlatId { get; set; }
    public decimal RentCollected { get; set; }
    public decimal ServiceChargeCollected { get; set; }
}

public class PaymentCollectionRow
{
    public DateTime? PaidAt { get; set; }
    public decimal RentCollected { get; set; }
    public decimal ServiceChargeCollected { get; set; }
}

/// <summary>
/// Shared "actual cash collected" proration logic, extracted verbatim from FinancialStandingService's
/// original Step 1 — same filter chain, same GroupBy, same Select expression, character-for-character.
/// Do not "improve" or re-derive this: it drives real money figures already relied upon by Financial
/// Standing, and any change here changes those figures too.
/// </summary>
public static class RentCollectionHelper
{
    public static async Task<List<FlatCollectionRow>> GetCollectedByFlatAsync(IQueryable<PaymentEntity> filteredPaymentQuery)
    {
        var perFlatRows = await filteredPaymentQuery
            .Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid)
            .Where(p => p.Amount > 0)
            .GroupBy(p => p.FlatId)
            .Select(g => new
            {
                FlatId = g.Key,
                // RentAmount/ServiceChargeAmount are nullable — coalesced to 0 before dividing so a
                // row missing one component contributes $0 for it rather than making the whole Sum
                // nullable (SQL SUM() returns NULL, not 0, when every input row is NULL).
                RentCollected = g.Sum(p => (p.RentAmount ?? 0m) / p.Amount * p.AmountPaid),
                ServiceChargeCollected = g.Sum(p => (p.ServiceChargeAmount ?? 0m) / p.Amount * p.AmountPaid)
            })
            .ToListAsync();

        return perFlatRows.Select(r => new FlatCollectionRow
        {
            FlatId = r.FlatId,
            RentCollected = r.RentCollected,
            ServiceChargeCollected = r.ServiceChargeCollected
        }).ToList();
    }

    /// <summary>
    /// Same per-row proration formula as GetCollectedByFlatAsync (Paid/PartiallyPaid, Amount > 0,
    /// identical RentCollected/ServiceChargeCollected expressions), but returns per-payment rows
    /// (with PaidAt) instead of grouping by FlatId — for trend methods that need to bucket by date.
    /// </summary>
    public static async Task<List<PaymentCollectionRow>> GetCollectedByPaymentAsync(IQueryable<PaymentEntity> filteredPaymentQuery)
    {
        return await filteredPaymentQuery
            .Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid)
            .Where(p => p.Amount > 0)
            .Select(p => new PaymentCollectionRow
            {
                PaidAt = p.PaidAt,
                RentCollected = (p.RentAmount ?? 0m) / p.Amount * p.AmountPaid,
                ServiceChargeCollected = (p.ServiceChargeAmount ?? 0m) / p.Amount * p.AmountPaid
            })
            .ToListAsync();
    }
}
