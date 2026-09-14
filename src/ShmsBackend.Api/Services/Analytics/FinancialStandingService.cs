using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Analytics;

public class FinancialStandingFilters
{
    public Guid? FlatId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? LandlordId { get; set; }   // null = Management's own portfolio-wide scope; set = that landlord's scope
}

public class FinancialStandingService
{
    private readonly PaymentQueryService _paymentQueryService;
    private readonly RefundQueryService _refundQueryService;
    private readonly DeductionQueryService _deductionQueryService;
    private readonly ExpenseQueryService _expenseQueryService;
    private readonly RewardTransactionQueryService _rewardTransactionQueryService;
    private readonly ShmsDbContext _context;

    public FinancialStandingService(
        PaymentQueryService paymentQueryService,
        RefundQueryService refundQueryService,
        DeductionQueryService deductionQueryService,
        ExpenseQueryService expenseQueryService,
        RewardTransactionQueryService rewardTransactionQueryService,
        ShmsDbContext context)
    {
        _paymentQueryService = paymentQueryService;
        _refundQueryService = refundQueryService;
        _deductionQueryService = deductionQueryService;
        _expenseQueryService = expenseQueryService;
        _rewardTransactionQueryService = rewardTransactionQueryService;
        _context = context;
    }

    public async Task<FinancialStandingResult> GetFinancialStandingAsync(FinancialStandingFilters filters, bool includeManagementOnlyFigures)
    {
        // ── Step 1: per-flat rent/service-charge collected, prorated against what was actually paid ──
        var paymentFilters = new PaymentFilters
        {
            FlatId = filters.FlatId,
            FromDate = filters.FromDate,
            ToDate = filters.ToDate,
            LandlordId = filters.LandlordId
        };

        // Extracted verbatim into RentCollectionHelper.GetCollectedByFlatAsync — same filter chain,
        // same GroupBy(FlatId), same Select expression as before, just relocated so PaymentAnalyticsService
        // and LandlordAnalyticsService can reuse the identical "actual cash collected" computation.
        var perFlatRows = await RentCollectionHelper.GetCollectedByFlatAsync(
            _paymentQueryService.BuildFilteredQuery(paymentFilters));

        // ── Step 2: apply each flat's own ManagementFeePercentage (in C#, after materialization) ──
        var flatIds = perFlatRows.Select(r => r.FlatId).ToList();
        var feePercentages = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.ManagementFeePercentage);

        decimal totalRent = 0, totalServiceCharge = 0, totalFee = 0;
        foreach (var row in perFlatRows)
        {
            totalRent += row.RentCollected;
            totalServiceCharge += row.ServiceChargeCollected;
            // ManagementFeePercentage is stored as a whole-number percent (e.g. 10 = 10%), not a
            // fraction — null (not yet configured for this flat) contributes $0 fee, never throws
            // and never excludes the flat's rent from the totals above.
            var pct = feePercentages.GetValueOrDefault(row.FlatId) ?? 0m;
            totalFee += row.RentCollected * (pct / 100m);
        }

        // ── Step 3: reuse the existing query services for the rest ──
        var totalRefunds = await _refundQueryService.BuildFilteredQuery(new RefundFilters
        {
            FlatId = filters.FlatId,
            LandlordId = filters.LandlordId,
            FromDate = filters.FromDate,
            ToDate = filters.ToDate
        }).SumAsync(s => s.Amount);

        var totalDeductions = await _deductionQueryService.BuildFilteredQuery(new DeductionFilters
        {
            FlatId = filters.FlatId,
            LandlordId = filters.LandlordId,
            FromDate = filters.FromDate,
            ToDate = filters.ToDate
        }).SumAsync(d => d.Amount);

        // LandlordId = filters.LandlordId here — null correctly means "Management's own log," matching
        // Expense's existing design (LandlordId is the log-scoping discriminator, not a broad filter).
        var totalExpenses = await _expenseQueryService.BuildFilteredQuery(new ExpenseFilters
        {
            FlatId = filters.FlatId,
            LandlordId = filters.LandlordId,
            FromDate = filters.FromDate,
            ToDate = filters.ToDate
        }).SumAsync(x => x.Amount);

        decimal? totalRewardsRedeemed = null;
        if (includeManagementOnlyFigures)
        {
            totalRewardsRedeemed = await _rewardTransactionQueryService.BuildFilteredQuery(new RewardTransactionFilters
            {
                FlatId = filters.FlatId,
                TransactionType = "Redeemed",
                FromDate = filters.FromDate,
                ToDate = filters.ToDate
            }).SumAsync(t => t.AmountPaidOrRedeemed ?? 0);
        }

        return new FinancialStandingResult
        {
            TotalRentCollected = totalRent,
            TotalServiceCharge = includeManagementOnlyFigures ? totalServiceCharge : null,
            TotalExpenses = totalExpenses,
            TotalRefunds = totalRefunds,
            TotalDeductions = totalDeductions,
            TotalManagementFee = totalFee,
            TotalRewardsRedeemed = totalRewardsRedeemed
        };
    }
}
