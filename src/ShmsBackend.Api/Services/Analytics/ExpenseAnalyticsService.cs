using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Analytics;

public class ExpenseAnalyticsService
{
    private readonly ExpenseQueryService _expenseQueryService;
    private readonly ShmsDbContext _context;

    public ExpenseAnalyticsService(ExpenseQueryService expenseQueryService, ShmsDbContext context)
    {
        _expenseQueryService = expenseQueryService;
        _context = context;
    }

    public async Task<AnalyticsBreakdownResult> GetByFlatAsync(ExpenseFilters filters)
    {
        var baseQuery = _expenseQueryService.BuildFilteredQuery(filters);

        var grouped = await baseQuery
            .GroupBy(x => x.FlatId)
            .Select(g => new { FlatId = g.Key, Count = g.Count(), Amount = g.Sum(x => x.Amount) })
            .ToListAsync();

        var flatIds = grouped.Where(g => g.FlatId.HasValue).Select(g => g.FlatId!.Value).Distinct().ToList();

        // IgnoreQueryFilters() so a since-soft-deleted Flat still resolves its name for historical
        // expenses logged against it — same precedent as ComplaintAnalyticsService's type resolution.
        var flatNames = await _context.Flats
            .IgnoreQueryFilters()
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var items = grouped.Select(g => new AnalyticsCategoryItem
        {
            Label = g.FlatId.HasValue ? flatNames.GetValueOrDefault(g.FlatId.Value, "Unknown") : "Unassigned",
            Count = g.Count,
            Amount = g.Amount
        }).ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = items.Sum(i => i.Amount ?? 0m)
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(ExpenseFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;

        var baseQuery = _expenseQueryService.BuildFilteredQuery(filters);
        var rows = await baseQuery
            .Where(x => x.ExpenseDate >= fromDate && x.ExpenseDate <= toDate.AddDays(1))
            .Select(x => new { x.ExpenseDate, x.Amount })
            .ToListAsync();

        var entries = rows.Select(r => (Date: r.ExpenseDate, Amount: r.Amount)).ToList();
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
