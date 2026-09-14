using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Api.Services;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Analytics;

public class LandlordPortfolioResult
{
    public int TotalFlats { get; set; }
    public int TotalHouses { get; set; }
    public int TotalTenants { get; set; }
    public decimal TotalCollected { get; set; }
}

public class LandlordAnalyticsService
{
    private readonly PaymentQueryService _paymentQueryService;
    private readonly ShmsDbContext _context;

    public LandlordAnalyticsService(PaymentQueryService paymentQueryService, ShmsDbContext context)
    {
        _paymentQueryService = paymentQueryService;
        _context = context;
    }

    public async Task<LandlordPortfolioResult> GetPortfolioAsync(Guid? landlordId, DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;
        var fromD = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toD = (toDate ?? now).Date;

        // landlordId null = portfolio-wide across all landlords, not one.
        var flatsQuery = _context.Flats.AsQueryable();
        if (landlordId.HasValue) flatsQuery = flatsQuery.Where(f => f.LandlordId == landlordId.Value);
        var totalFlats = await flatsQuery.CountAsync();

        var housesQuery = _context.Houses.Where(h => h.Flat != null);
        if (landlordId.HasValue) housesQuery = housesQuery.Where(h => h.Flat!.LandlordId == landlordId.Value);
        var totalHouses = await housesQuery.CountAsync();

        var tenantsQuery = _context.Tenants.Where(t => t.House != null && t.House.Flat != null);
        if (landlordId.HasValue) tenantsQuery = tenantsQuery.Where(t => t.House!.Flat!.LandlordId == landlordId.Value);
        var totalTenants = await tenantsQuery.CountAsync();

        // Same RentCollectionHelper "actual cash collected" figure used by Financial Standing.
        var paymentFilters = new PaymentFilters { LandlordId = landlordId, FromDate = fromD, ToDate = toD };
        var rows = await RentCollectionHelper.GetCollectedByFlatAsync(_paymentQueryService.BuildFilteredQuery(paymentFilters));
        var totalCollected = rows.Sum(r => r.RentCollected);

        return new LandlordPortfolioResult
        {
            TotalFlats = totalFlats,
            TotalHouses = totalHouses,
            TotalTenants = totalTenants,
            TotalCollected = totalCollected
        };
    }

    public async Task<AnalyticsTrendResult> GetCollectionTrendAsync(Guid? landlordId, DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;
        var fromD = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toD = (toDate ?? now).Date;

        var paymentFilters = new PaymentFilters { LandlordId = landlordId, FromDate = fromD, ToDate = toD };
        var rows = await RentCollectionHelper.GetCollectedByPaymentAsync(_paymentQueryService.BuildFilteredQuery(paymentFilters));
        var entries = rows
            .Where(r => r.PaidAt.HasValue)
            .Select(r => (Date: r.PaidAt!.Value, Amount: r.RentCollected))
            .ToList();

        var spanDays = (toD - fromD).TotalDays;

        return spanDays <= 90
            ? BuildWeeklyTrend(entries, fromD, toD)
            : BuildMonthlyTrend(entries, fromD, toD);
    }

    private static AnalyticsTrendResult BuildWeeklyTrend(List<(DateTime Date, decimal Amount)> entries, DateTime fromDate, DateTime toDate)
    {
        DateTime StartOfWeek(DateTime d)
        {
            var diff = (7 + (d.DayOfWeek - DayOfWeek.Monday)) % 7;
            return d.Date.AddDays(-diff);
        }

        var sums = entries
            .GroupBy(e => StartOfWeek(e.Date))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var labels = new List<string>();
        var values = new List<decimal>();
        var bucketStart = StartOfWeek(fromDate);
        var bucketEnd = StartOfWeek(toDate);

        for (var week = bucketStart; week <= bucketEnd; week = week.AddDays(7))
        {
            labels.Add($"Week of {week.ToString("MMM d", CultureInfo.InvariantCulture)}");
            values.Add(sums.GetValueOrDefault(week, 0m));
        }

        return new AnalyticsTrendResult { Labels = labels, AmountValues = values, Granularity = "weekly" };
    }

    private static AnalyticsTrendResult BuildMonthlyTrend(List<(DateTime Date, decimal Amount)> entries, DateTime fromDate, DateTime toDate)
    {
        var sums = entries
            .GroupBy(e => new DateTime(e.Date.Year, e.Date.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var labels = new List<string>();
        var values = new List<decimal>();
        var bucketStart = new DateTime(fromDate.Year, fromDate.Month, 1);
        var bucketEnd = new DateTime(toDate.Year, toDate.Month, 1);

        for (var month = bucketStart; month <= bucketEnd; month = month.AddMonths(1))
        {
            labels.Add(month.ToString("MMM yyyy", CultureInfo.InvariantCulture));
            values.Add(sums.GetValueOrDefault(month, 0m));
        }

        return new AnalyticsTrendResult { Labels = labels, AmountValues = values, Granularity = "monthly" };
    }
}
