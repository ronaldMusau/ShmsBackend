using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;

namespace ShmsBackend.Api.Services.Analytics;

public class SessionAnalyticsService
{
    private readonly ListingViewingSessionQueryService _sessionQueryService;

    public SessionAnalyticsService(ListingViewingSessionQueryService sessionQueryService)
    {
        _sessionQueryService = sessionQueryService;
    }

    public async Task<AnalyticsBreakdownResult> GetStatusBreakdownAsync(SessionFilters filters)
    {
        var baseQuery = _sessionQueryService.BuildFilteredQuery(filters);

        var grouped = await baseQuery
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        // Exhaustive, re-confirmed set of ListingViewingSession.Status string literals.
        string[] allStatuses = { "Pending", "Accepted", "Declined", "AwaitingFeedback", "Forfeited", "Closed" };
        var items = allStatuses
            .Select(status => new AnalyticsCategoryItem
            {
                Label = status,
                Count = grouped.FirstOrDefault(g => g.Status == status)?.Count ?? 0,
                Amount = null // no natural monetary figure for this domain
            })
            .ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = null
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(SessionFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;

        var baseQuery = _sessionQueryService.BuildFilteredQuery(filters);
        var dates = await baseQuery
            .Where(s => s.ScheduledAt >= fromDate && s.ScheduledAt <= toDate.AddDays(1))
            .Select(s => s.ScheduledAt)
            .ToListAsync();

        var spanDays = (toDate - fromDate).TotalDays;

        return spanDays <= 90
            ? BuildWeeklyTrend(dates, fromDate, toDate)
            : BuildMonthlyTrend(dates, fromDate, toDate);
    }

    private static AnalyticsTrendResult BuildWeeklyTrend(List<DateTime> dates, DateTime fromDate, DateTime toDate)
    {
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
