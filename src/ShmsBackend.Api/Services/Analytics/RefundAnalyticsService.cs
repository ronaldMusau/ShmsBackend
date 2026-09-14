using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;

namespace ShmsBackend.Api.Services.Analytics;

public class RefundAnalyticsService
{
    private readonly RefundQueryService _refundQueryService;

    public RefundAnalyticsService(RefundQueryService refundQueryService)
    {
        _refundQueryService = refundQueryService;
    }

    public async Task<AnalyticsBreakdownResult> GetStatusBreakdownAsync(RefundFilters filters)
    {
        var baseQuery = _refundQueryService.BuildFilteredQuery(filters);

        // Two fixed buckets by (PaidAt != null) — same derived Paid/Refundable label convention
        // already used by RefundReportBuilder, not a stored column.
        var paidCount = await baseQuery.CountAsync(s => s.PaidAt != null);
        var paidAmount = await baseQuery.Where(s => s.PaidAt != null).SumAsync(s => s.Amount);
        var refundableCount = await baseQuery.CountAsync(s => s.PaidAt == null);
        var refundableAmount = await baseQuery.Where(s => s.PaidAt == null).SumAsync(s => s.Amount);

        var items = new List<AnalyticsCategoryItem>
        {
            new() { Label = "Paid", Count = paidCount, Amount = paidAmount },
            new() { Label = "Refundable", Count = refundableCount, Amount = refundableAmount }
        };

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = paidCount + refundableCount,
            TotalAmount = paidAmount + refundableAmount
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(RefundFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;

        var baseQuery = _refundQueryService.BuildFilteredQuery(filters);
        var rows = await baseQuery
            .Where(s => s.CreatedAt >= fromDate && s.CreatedAt <= toDate.AddDays(1))
            .Select(s => new { s.CreatedAt, s.Amount })
            .ToListAsync();

        var entries = rows.Select(r => (Date: r.CreatedAt, Amount: r.Amount)).ToList();
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
