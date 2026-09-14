using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Analytics;

public class PaymentAnalyticsService
{
    private readonly PaymentQueryService _paymentQueryService;

    public PaymentAnalyticsService(PaymentQueryService paymentQueryService)
    {
        _paymentQueryService = paymentQueryService;
    }

    public async Task<AnalyticsBreakdownResult> GetStatusBreakdownAsync(PaymentFilters filters)
    {
        // Need to see every status here (including Pending/Processing/Failed/Cancelled), not just the
        // default Paid/PartiallyPaid/Overdue subset — opt in via the new flag.
        filters.IncludeAllStatuses = true;
        var baseQuery = _paymentQueryService.BuildFilteredQuery(filters);

        var grouped = await baseQuery
            .GroupBy(p => p.PaymentStatus)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(p => p.AmountPaid) })
            .ToListAsync();

        string[] allStatuses = { "Pending", "Processing", "Paid", "PartiallyPaid", "Overdue", "Failed", "Cancelled" };
        var items = allStatuses.Select(status =>
        {
            var match = grouped.FirstOrDefault(g => g.Status.ToString() == status);
            return new AnalyticsCategoryItem
            {
                Label = status,
                Count = match?.Count ?? 0,
                Amount = match?.Amount ?? 0m
            };
        }).ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = items.Sum(i => i.Amount ?? 0m)
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(PaymentFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;
        filters.FromDate = fromDate;
        filters.ToDate = toDate;

        // Same "actual cash collected, including partials" philosophy as Financial Standing — RentCollected
        // per payment row, NOT the older Payments-list-page "Paid status only" convention.
        var rows = await RentCollectionHelper.GetCollectedByPaymentAsync(_paymentQueryService.BuildFilteredQuery(filters));
        var entries = rows
            .Where(r => r.PaidAt.HasValue)
            .Select(r => (Date: r.PaidAt!.Value, Amount: r.RentCollected))
            .ToList();

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
