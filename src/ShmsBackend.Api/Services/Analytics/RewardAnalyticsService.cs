using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;

namespace ShmsBackend.Api.Services.Analytics;

public class RewardAnalyticsService
{
    private readonly RewardTransactionQueryService _rewardTransactionQueryService;

    public RewardAnalyticsService(RewardTransactionQueryService rewardTransactionQueryService)
    {
        _rewardTransactionQueryService = rewardTransactionQueryService;
    }

    public async Task<AnalyticsBreakdownResult> GetTypeBreakdownAsync(RewardTransactionFilters filters)
    {
        var baseQuery = _rewardTransactionQueryService.BuildFilteredQuery(filters);

        var grouped = await baseQuery
            .GroupBy(t => t.TransactionType)
            .Select(g => new { Type = g.Key, Count = g.Count(), Amount = g.Sum(t => t.AmountPaidOrRedeemed ?? 0m) })
            .ToListAsync();

        // Both buckets always present — "Earned" and "Redeemed" are the only two TransactionType
        // values (confirmed from the entity), zero-filled even if one has no activity in this filter.
        string[] allTypes = { "Earned", "Redeemed" };
        var items = allTypes
            .Select(type =>
            {
                var match = grouped.FirstOrDefault(g => g.Type == type);
                return new AnalyticsCategoryItem
                {
                    Label = type,
                    Count = match?.Count ?? 0,
                    Amount = match?.Amount ?? 0m
                };
            })
            .ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = items.Sum(i => i.Amount ?? 0m)
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(RewardTransactionFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;

        var baseQuery = _rewardTransactionQueryService.BuildFilteredQuery(filters);
        var rows = await baseQuery
            .Where(t => t.CreatedAt >= fromDate && t.CreatedAt <= toDate.AddDays(1))
            .Select(t => new { t.CreatedAt, Amount = t.AmountPaidOrRedeemed ?? 0m })
            .ToListAsync();

        var entries = rows.Select(r => (Date: r.CreatedAt, r.Amount)).ToList();
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
