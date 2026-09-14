using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Analytics;

public class AgentPerformanceResult
{
    // TRUE total sessions handled — ALL ListingViewingSessions in range, regardless of status/rating.
    // Distinct from SessionsRated below (AgentReportBuilder's existing figure only counts
    // Status == "Closed" && AgentRating != null sessions).
    public int TotalSessionsHandled { get; set; }
    public double? AvgRating { get; set; }
    public int SessionsRated { get; set; }
}

public class AgentAnalyticsService
{
    private readonly ShmsDbContext _context;

    public AgentAnalyticsService(ShmsDbContext context)
    {
        _context = context;
    }

    public async Task<AgentPerformanceResult> GetPerformanceAsync(Guid? agentId, DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;
        var fromD = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toD = (toDate ?? now).Date;

        var sessionsQuery = _context.ListingViewingSessions
            .Where(s => s.ScheduledAt >= fromD && s.ScheduledAt <= toD.AddDays(1));

        // agentId null = aggregate portfolio-wide across all agents, not one.
        if (agentId.HasValue)
            sessionsQuery = sessionsQuery.Where(s => s.AgentId == agentId.Value);

        var totalSessionsHandled = await sessionsQuery.CountAsync();

        // Mirrors AgentReportBuilder's exact rating-aggregation predicate.
        var ratedQuery = sessionsQuery.Where(s => s.Status == "Closed" && s.AgentRating != null);
        var sessionsRated = await ratedQuery.CountAsync();
        double? avgRating = sessionsRated > 0
            ? await ratedQuery.AverageAsync(s => (double)s.AgentRating!.Value)
            : null;

        return new AgentPerformanceResult
        {
            TotalSessionsHandled = totalSessionsHandled,
            AvgRating = avgRating,
            SessionsRated = sessionsRated
        };
    }

    public async Task<AnalyticsTrendResult> GetSessionsTrendAsync(Guid? agentId, DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;
        var fromD = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toD = (toDate ?? now).Date;

        var query = _context.ListingViewingSessions
            .Where(s => s.ScheduledAt >= fromD && s.ScheduledAt <= toD.AddDays(1));
        if (agentId.HasValue)
            query = query.Where(s => s.AgentId == agentId.Value);

        var dates = await query.Select(s => s.ScheduledAt).ToListAsync();

        var spanDays = (toD - fromD).TotalDays;

        return spanDays <= 90
            ? BuildWeeklyTrend(dates, fromD, toD)
            : BuildMonthlyTrend(dates, fromD, toD);
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
