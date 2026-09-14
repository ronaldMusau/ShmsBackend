using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Analytics;

public class ComplaintAnalyticsService
{
    private readonly ComplaintQueryService _complaintQueryService;
    private readonly ShmsDbContext _context;

    public ComplaintAnalyticsService(ComplaintQueryService complaintQueryService, ShmsDbContext context)
    {
        _complaintQueryService = complaintQueryService;
        _context = context;
    }

    public async Task<AnalyticsBreakdownResult> GetStatusBreakdownAsync(ComplaintFilters filters)
    {
        var baseQuery = _complaintQueryService.BuildFilteredQuery(filters);

        var grouped = await baseQuery
            .GroupBy(c => c.Status)
            .Select(g => new AnalyticsCategoryItem
            {
                Label = g.Key,
                Count = g.Count(),
                Amount = g.Where(c => c.IsBillable == true).Sum(c => (decimal?)c.BillableAmount) ?? 0m
            })
            .ToListAsync();

        // Zero-fill every known Complaint.Status value (confirmed exhaustive: "Open", "UnderReview",
        // "Approved", "Rejected", "Closed") so the breakdown is dense, not sparse — a status with zero
        // matching rows in this filter still appears with Count = 0, Amount = 0, matching
        // RefundAnalyticsService.GetStatusBreakdownAsync's fixed-bucket convention rather than
        // silently omitting it.
        string[] allStatuses = { "Open", "UnderReview", "Approved", "Rejected", "Closed" };
        var items = allStatuses
            .Select(status => grouped.FirstOrDefault(i => i.Label == status)
                ?? new AnalyticsCategoryItem { Label = status, Count = 0, Amount = 0m })
            .ToList();

        // TotalAmount is the billable total across the WHOLE filtered set, not a sum of the
        // per-status Amounts above (those already sum to the same thing, but computed independently
        // here per the stated requirement so the two never silently drift if the grouping changes).
        var totalAmount = await baseQuery.Where(c => c.IsBillable == true).SumAsync(c => (decimal?)c.BillableAmount) ?? 0m;

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = totalAmount
        };
    }

    public async Task<AnalyticsBreakdownResult> GetTypeBreakdownAsync(ComplaintFilters filters)
    {
        var baseQuery = _complaintQueryService.BuildFilteredQuery(filters);

        // ComplaintTypeId is a non-nullable Guid FK on Complaint (confirmed from the entity) —
        // there is no "uncategorized" case to handle here.
        var grouped = await baseQuery
            .GroupBy(c => c.ComplaintTypeId)
            .Select(g => new
            {
                ComplaintTypeId = g.Key,
                Count = g.Count(),
                Amount = g.Where(c => c.IsBillable == true).Sum(c => (decimal?)c.BillableAmount) ?? 0m
            })
            .ToListAsync();

        var typeIds = grouped.Select(g => g.ComplaintTypeId).Distinct().ToList();

        // ComplaintType names are resolved dynamically from the admin-editable lookup table — never
        // hardcoded. IgnoreQueryFilters() so a type that's since been soft-deleted still resolves its
        // name for historical complaints logged against it, same pattern as
        // ReportBuilderHelpers.ResolveFlatNameAsync.
        var typeNames = await _context.ComplaintTypes
            .IgnoreQueryFilters()
            .Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        var items = grouped.Select(g => new AnalyticsCategoryItem
        {
            Label = typeNames.GetValueOrDefault(g.ComplaintTypeId, "Unknown"),
            Count = g.Count,
            Amount = g.Amount
        }).ToList();

        var totalAmount = await baseQuery.Where(c => c.IsBillable == true).SumAsync(c => (decimal?)c.BillableAmount) ?? 0m;

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = totalAmount
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(ComplaintFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;

        var baseQuery = _complaintQueryService.BuildFilteredQuery(filters);
        var dates = await baseQuery
            .Where(c => c.CreatedAt >= fromDate && c.CreatedAt <= toDate.AddDays(1))
            .Select(c => c.CreatedAt)
            .ToListAsync();

        var spanDays = (toDate - fromDate).TotalDays;

        if (spanDays <= 90)
            return BuildWeeklyTrend(dates, fromDate, toDate);

        return BuildMonthlyTrend(dates, fromDate, toDate);
    }

    private static AnalyticsTrendResult BuildWeeklyTrend(List<DateTime> dates, DateTime fromDate, DateTime toDate)
    {
        // Monday-start ISO week buckets.
        DateTime StartOfWeek(DateTime d)
        {
            var diff = (7 + (d.DayOfWeek - DayOfWeek.Monday)) % 7;
            return d.Date.AddDays(-diff);
        }

        var counts = dates
            .GroupBy(StartOfWeek)
            .ToDictionary(g => g.Key, g => g.Count());

        var labels = new List<string>();
        var values = new List<int>();
        var bucketStart = StartOfWeek(fromDate);
        var bucketEnd = StartOfWeek(toDate);

        for (var week = bucketStart; week <= bucketEnd; week = week.AddDays(7))
        {
            labels.Add($"Week of {week.ToString("MMM d", CultureInfo.InvariantCulture)}");
            values.Add(counts.GetValueOrDefault(week, 0));
        }

        return new AnalyticsTrendResult { Labels = labels, Values = values, Granularity = "weekly" };
    }

    private static AnalyticsTrendResult BuildMonthlyTrend(List<DateTime> dates, DateTime fromDate, DateTime toDate)
    {
        var counts = dates
            .GroupBy(d => new DateTime(d.Year, d.Month, 1))
            .ToDictionary(g => g.Key, g => g.Count());

        var labels = new List<string>();
        var values = new List<int>();
        var bucketStart = new DateTime(fromDate.Year, fromDate.Month, 1);
        var bucketEnd = new DateTime(toDate.Year, toDate.Month, 1);

        for (var month = bucketStart; month <= bucketEnd; month = month.AddMonths(1))
        {
            labels.Add(month.ToString("MMM yyyy", CultureInfo.InvariantCulture));
            values.Add(counts.GetValueOrDefault(month, 0));
        }

        return new AnalyticsTrendResult { Labels = labels, Values = values, Granularity = "monthly" };
    }
}
