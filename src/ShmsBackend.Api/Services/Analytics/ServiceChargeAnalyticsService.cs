using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Context;
using PaymentEntity = ShmsBackend.Data.Models.Entities.Portal.Payment;

namespace ShmsBackend.Api.Services.Analytics;

public class ServiceChargeAnalyticsService
{
    private readonly PaymentQueryService _paymentQueryService;
    private readonly ShmsDbContext _context;

    public ServiceChargeAnalyticsService(PaymentQueryService paymentQueryService, ShmsDbContext context)
    {
        _paymentQueryService = paymentQueryService;
        _context = context;
    }

    // The confirmed real filter for "Service Charge" rows (PaymentController.GetAllServiceChargesCollected),
    // NOT PaymentType — that enum value is never actually used anywhere in the codebase.
    private IQueryable<PaymentEntity> BaseQuery(PaymentFilters filters) =>
        _paymentQueryService.BuildFilteredQuery(filters)
            .Where(p => p.ServiceChargeAmount > 0 && p.MpesaReceiptNumber != null);

    public async Task<AnalyticsBreakdownResult> GetStatusBreakdownAsync(PaymentFilters filters)
    {
        var baseQuery = BaseQuery(filters);

        var items = await baseQuery
            .GroupBy(p => p.PaymentStatus)
            .Select(g => new AnalyticsCategoryItem
            {
                Label = g.Key.ToString(),
                Count = g.Count(),
                Amount = g.Sum(p => p.ServiceChargeAmount ?? 0m)
            })
            .ToListAsync();

        var totalAmount = await baseQuery.SumAsync(p => p.ServiceChargeAmount ?? 0m);

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = totalAmount
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(PaymentFilters filters)
    {
        var now = DateTime.UtcNow;
        var fromDate = (filters.FromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var toDate = (filters.ToDate ?? now).Date;

        var baseQuery = BaseQuery(filters);
        var rows = await baseQuery
            .Where(p => p.PaidAt != null && p.PaidAt >= fromDate && p.PaidAt <= toDate.AddDays(1))
            .Select(p => new { PaidAt = p.PaidAt!.Value, ServiceChargeAmount = p.ServiceChargeAmount ?? 0m })
            .ToListAsync();

        var entries = rows.Select(r => (Date: r.PaidAt, Amount: r.ServiceChargeAmount)).ToList();
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
