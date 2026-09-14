using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Analytics;

public class DeductionAnalyticsService
{
    private readonly DeductionQueryService _deductionQueryService;
    private readonly ShmsDbContext _context;

    public DeductionAnalyticsService(DeductionQueryService deductionQueryService, ShmsDbContext context)
    {
        _deductionQueryService = deductionQueryService;
        _context = context;
    }

    public async Task<AnalyticsBreakdownResult> GetByCategoryAsync(DeductionFilters filters)
    {
        var baseQuery = _deductionQueryService.BuildFilteredQuery(filters);

        var rows = await baseQuery
            .Select(d => new { d.ComplaintId, d.Amount })
            .ToListAsync();

        var complaintIds = rows.Select(r => r.ComplaintId).Distinct().ToList();

        // Deduction.ComplaintId is a non-nullable FK (confirmed) — every deduction is complaint-linked.
        // IgnoreQueryFilters() on both hops (Complaint, then ComplaintType) so a since-closed complaint
        // or a later-deleted ComplaintType still resolves its category name for historical deductions —
        // same precedent as ComplaintAnalyticsService.GetTypeBreakdownAsync.
        var complaintTypeIds = await _context.Complaints
            .IgnoreQueryFilters()
            .Where(c => complaintIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.ComplaintTypeId);

        var typeIds = complaintTypeIds.Values.Distinct().ToList();
        var typeNames = await _context.ComplaintTypes
            .IgnoreQueryFilters()
            .Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        string ResolveCategory(Guid complaintId) =>
            complaintTypeIds.TryGetValue(complaintId, out var typeId)
                ? typeNames.GetValueOrDefault(typeId, "Unknown")
                : "Unknown";

        var items = rows
            .GroupBy(r => ResolveCategory(r.ComplaintId))
            .Select(g => new AnalyticsCategoryItem
            {
                Label = g.Key,
                Count = g.Count(),
                Amount = g.Sum(x => x.Amount)
            })
            .ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = items.Sum(i => i.Amount ?? 0m)
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(DeductionFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;

        var baseQuery = _deductionQueryService.BuildFilteredQuery(filters);
        var rows = await baseQuery
            .Where(d => d.CreatedAt >= fromDate && d.CreatedAt <= toDate.AddDays(1))
            .Select(d => new { d.CreatedAt, d.Amount })
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
