using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;

namespace ShmsBackend.Api.Services.Analytics;

public class ForfeitedAdvanceAnalyticsService
{
    private readonly ForfeitedAdvanceQueryService _forfeitedAdvanceQueryService;

    public ForfeitedAdvanceAnalyticsService(ForfeitedAdvanceQueryService forfeitedAdvanceQueryService)
    {
        _forfeitedAdvanceQueryService = forfeitedAdvanceQueryService;
    }

    public async Task<AnalyticsBreakdownResult> GetBreakdownAsync(ForfeitedAdvanceFilters filters)
    {
        var baseQuery = _forfeitedAdvanceQueryService.BuildFilteredQuery(filters);

        // Unlike Refunds' Paid/Refundable split, these two buckets are NOT mutually exclusive per
        // row — every forfeited-advance record has both an AmountAppliedToDamages and an
        // AmountForfeitedUnused component (either can be 0 for a given row). So "Count" per bucket
        // is the same total record count for both — it's a split of one total, not a partition of rows.
        var totalRecordCount = await baseQuery.CountAsync();
        var appliedToDamages = await baseQuery.SumAsync(f => f.AmountAppliedToDamages);
        var forfeitedUnused = await baseQuery.SumAsync(f => f.AmountForfeitedUnused);

        var items = new List<AnalyticsCategoryItem>
        {
            new() { Label = "Applied to Damages", Count = totalRecordCount, Amount = appliedToDamages },
            new() { Label = "Forfeited (Unused)", Count = totalRecordCount, Amount = forfeitedUnused }
        };

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = totalRecordCount,
            TotalAmount = appliedToDamages + forfeitedUnused
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(ForfeitedAdvanceFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;

        var baseQuery = _forfeitedAdvanceQueryService.BuildFilteredQuery(filters);
        // TotalAdvanceAmount is the headline figure tracked over time — the total advance value
        // being processed (split into applied/forfeited) each period, not one of the two sub-components.
        var rows = await baseQuery
            .Where(f => f.CreatedAt >= fromDate && f.CreatedAt <= toDate.AddDays(1))
            .Select(f => new { f.CreatedAt, f.TotalAdvanceAmount })
            .ToListAsync();

        var entries = rows.Select(r => (Date: r.CreatedAt, Amount: r.TotalAdvanceAmount)).ToList();
        var spanDays = (toDate - fromDate).TotalDays;

        return spanDays <= 90
            ? BuildWeeklyTrend(entries, fromDate, toDate)
            : BuildMonthlyTrend(entries, fromDate, toDate);
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
