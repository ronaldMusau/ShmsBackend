using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Api.Services;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Api.Services.Analytics;

public class LandlordPortfolioResult
{
    public int TotalFlats { get; set; }
    public int TotalHouses { get; set; }
    public int TotalTenants { get; set; }
    public decimal TotalCollected { get; set; }
}

public class ExpectationMonthRow
{
    public string Label { get; set; } = "";
    public decimal Expected { get; set; }
    public decimal Collected { get; set; }
    public decimal Shortfall { get; set; }
    public decimal CollectionPercent { get; set; }
    public bool InProgress { get; set; }
}

public class ExpectationFlatRow
{
    public Guid FlatId { get; set; }
    public string FlatName { get; set; } = "";
    public int TotalHouses { get; set; }
    public int OccupiedHouses { get; set; }
    public int VacantHouses { get; set; }
    public decimal OccupancyPercent { get; set; }
    public decimal ExpectedMonthly { get; set; }
    public decimal PotentialMonthly { get; set; }
    public decimal VacantLossMonthly { get; set; }
    public decimal ManagementFeePercentage { get; set; }
    public decimal Collected { get; set; }
    public decimal ExpectedInRange { get; set; }
    public decimal CollectionPercent { get; set; }
}

public class ExpectationResult
{
    public int TotalHouses { get; set; }
    public int OccupiedHouses { get; set; }
    public int VacantHouses { get; set; }
    public decimal OccupancyPercent { get; set; }
    /// <summary>Monthly rent if every house were let.</summary>
    public decimal PotentialMonthly { get; set; }
    /// <summary>Monthly rent expected from houses that are occupied right now.</summary>
    public decimal ExpectedMonthly { get; set; }
    public decimal VacantLossMonthly { get; set; }
    public decimal VacantLossPercent { get; set; }
    public decimal ExpectedInRange { get; set; }
    public decimal CollectedInRange { get; set; }
    public decimal CollectionPercent { get; set; }
    public decimal OutstandingInRange { get; set; }
    public List<ExpectationMonthRow> Months { get; set; } = new();
    public List<ExpectationFlatRow> Flats { get; set; } = new();
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

        // Only tenants actually living in a house count (Active); Pending/Inactive/PaymentFailed records are not occupants.
        var tenantsQuery = _context.Tenants.Where(t => t.House != null && t.House.Flat != null
            && (t.TenantStatus == TenantStatus.Active || t.TenantStatus == TenantStatus.SettlingVacate));
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

    /// <summary>
    /// Expected vs collected rent. "Expected" = rent of the houses that are occupied now (stays the same
    /// every month until a house is let or vacated); "potential" = rent if every house were let. Collected
    /// uses the same RentCollectionHelper figure as Financial Standing, bucketed by when the money moved.
    /// The expected figure is a snapshot of current occupancy (history of occupancy is not recorded).
    /// </summary>
    public async Task<ExpectationResult> GetExpectationAsync(Guid? landlordId, DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;
        var fromD = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toD = (toDate ?? now).Date;

        var houses = await _context.Houses
            .Where(h => !h.IsDeleted && h.Flat != null && !h.Flat.IsDeleted && (!landlordId.HasValue || h.Flat.LandlordId == landlordId.Value))
            .Select(h => new { h.FlatId, FlatName = h.Flat!.FlatName, h.RentFee, h.OccupancyStatus, Fee = h.Flat.ManagementFeePercentage })
            .ToListAsync();

        var payments = await _paymentQueryService.BuildFilteredQuery(new PaymentFilters { LandlordId = landlordId, FromDate = fromD, ToDate = toD })
            .Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid)
            .Where(p => p.Amount > 0)
            .Select(p => new { p.FlatId, p.PaidAt, Rent = (p.RentAmount ?? 0m) / p.Amount * p.AmountPaid })
            .ToListAsync();

        static decimal Pct(decimal part, decimal whole) => whole <= 0 ? 0m : Math.Round(part / whole * 100m, 1);

        var months = new List<DateTime>();
        for (var m = new DateTime(fromD.Year, fromD.Month, 1); m <= new DateTime(toD.Year, toD.Month, 1); m = m.AddMonths(1)) months.Add(m);

        decimal expectedAll = houses.Where(h => h.OccupancyStatus == OccupancyStatus.Occupied).Sum(h => h.RentFee);
        var result = new ExpectationResult
        {
            TotalHouses = houses.Count,
            OccupiedHouses = houses.Count(h => h.OccupancyStatus == OccupancyStatus.Occupied),
            PotentialMonthly = houses.Sum(h => h.RentFee),
            ExpectedMonthly = expectedAll
        };
        result.VacantHouses = result.TotalHouses - result.OccupiedHouses;
        result.OccupancyPercent = Pct(result.OccupiedHouses, result.TotalHouses);
        result.VacantLossMonthly = result.PotentialMonthly - result.ExpectedMonthly;
        result.VacantLossPercent = Pct(result.VacantLossMonthly, result.PotentialMonthly);

        foreach (var m in months)
        {
            var collected = payments.Where(p => p.PaidAt.HasValue && p.PaidAt.Value.Year == m.Year && p.PaidAt.Value.Month == m.Month).Sum(p => p.Rent);
            result.Months.Add(new ExpectationMonthRow
            {
                Label = m.ToString("MMM yyyy", CultureInfo.InvariantCulture),
                Expected = expectedAll,
                Collected = Math.Round(collected, 2),
                Shortfall = Math.Max(0m, Math.Round(expectedAll - collected, 2)),
                CollectionPercent = Pct(collected, expectedAll),
                InProgress = m.Year == now.Year && m.Month == now.Month
            });
        }

        result.ExpectedInRange = expectedAll * months.Count;
        result.CollectedInRange = Math.Round(payments.Sum(p => p.Rent), 2);
        result.OutstandingInRange = Math.Max(0m, result.ExpectedInRange - result.CollectedInRange);
        result.CollectionPercent = Pct(result.CollectedInRange, result.ExpectedInRange);

        foreach (var g in houses.GroupBy(h => new { h.FlatId, h.FlatName, h.Fee }).OrderBy(g => g.Key.FlatName))
        {
            var occ = g.Where(h => h.OccupancyStatus == OccupancyStatus.Occupied).ToList();
            var expected = occ.Sum(h => h.RentFee);
            var potential = g.Sum(h => h.RentFee);
            var collected = payments.Where(p => p.FlatId == g.Key.FlatId).Sum(p => p.Rent);
            result.Flats.Add(new ExpectationFlatRow
            {
                FlatId = g.Key.FlatId,
                FlatName = g.Key.FlatName,
                TotalHouses = g.Count(),
                OccupiedHouses = occ.Count,
                VacantHouses = g.Count() - occ.Count,
                OccupancyPercent = Pct(occ.Count, g.Count()),
                ExpectedMonthly = expected,
                PotentialMonthly = potential,
                VacantLossMonthly = potential - expected,
                ManagementFeePercentage = g.Key.Fee ?? 0m,
                Collected = Math.Round(collected, 2),
                ExpectedInRange = expected * months.Count,
                CollectionPercent = Pct(collected, expected * months.Count)
            });
        }
        return result;
    }
}
