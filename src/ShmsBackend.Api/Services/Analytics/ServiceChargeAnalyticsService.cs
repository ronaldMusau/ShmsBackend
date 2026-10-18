using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Context;
using ShmsBackend.Api.Services;
using PaymentEntity = ShmsBackend.Data.Models.Entities.Portal.Payment;

namespace ShmsBackend.Api.Services.Analytics;

public class ServiceChargeAnalyticsService
{
    private readonly PaymentQueryService _paymentQueryService;
    private readonly ShmsDbContext _context;
    private readonly ServiceChargeQueryService _serviceChargeQueryService;

    public ServiceChargeAnalyticsService(PaymentQueryService paymentQueryService, ShmsDbContext context, ServiceChargeQueryService serviceChargeQueryService)
    {
        _serviceChargeQueryService = serviceChargeQueryService;
        _paymentQueryService = paymentQueryService;
        _context = context;
    }

    // The confirmed real filter for "Service Charge" rows (PaymentController.GetAllServiceChargesCollected),
    // NOT PaymentType — that enum value is never actually used anywhere in the codebase.
    private IQueryable<PaymentEntity> BaseQuery(PaymentFilters filters) =>
        _paymentQueryService.BuildFilteredQuery(filters)
            .Where(p => p.ServiceChargeAmount > 0 && p.MpesaReceiptNumber != null);

    private static readonly (string Key, string Label)[] Sources =
    {
        ("Rent", "Rent payments"),
        ("Complaint", "Complaint charges"),
        ("Vacate", "Vacate settlements")
    };

    private static bool Wants(PaymentFilters f, string key) =>
        string.IsNullOrWhiteSpace(f.Source) || f.Source == key;

    private ServiceChargeFilters ToLedgerFilters(PaymentFilters f, string source) => new()
    {
        FlatId = f.FlatId, HouseId = f.HouseId, TenantId = f.TenantId,
        FromDate = f.FromDate, ToDate = f.ToDate, Source = source
    };

    /// <summary>Breakdown by where the service charge was collected: rent payments, complaint charges, vacate settlements.</summary>
    public async Task<AnalyticsBreakdownResult> GetStatusBreakdownAsync(PaymentFilters filters)
    {
        var items = new List<AnalyticsCategoryItem>();

        foreach (var (key, label) in Sources)
        {
            if (!Wants(filters, key)) { items.Add(new AnalyticsCategoryItem { Label = label, Count = 0, Amount = 0m }); continue; }

            if (key == "Rent")
            {
                var rent = BaseQuery(filters);
                // Prorated by what was actually paid (same basis as Financial Standing).
                items.Add(new AnalyticsCategoryItem
                {
                    Label = label,
                    Count = await rent.CountAsync(),
                    Amount = await rent.SumAsync(p => p.Amount > 0 ? (p.ServiceChargeAmount ?? 0m) / p.Amount * p.AmountPaid : 0m)
                });
            }
            else
            {
                var rows = await _serviceChargeQueryService.GetEntriesAsync(ToLedgerFilters(filters, key));
                items.Add(new AnalyticsCategoryItem { Label = label, Count = rows.Count, Amount = rows.Sum(r => r.ServiceChargeAmount) });
            }
        }

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

        var perSource = new Dictionary<string, List<(DateTime Date, decimal Amount)>>();

        foreach (var (key, _) in Sources)
        {
            if (!Wants(filters, key)) { perSource[key] = new(); continue; }

            if (key == "Rent")
            {
                var rows = await BaseQuery(filters)
                    .Where(p => p.PaidAt != null && p.PaidAt >= fromDate && p.PaidAt <= toDate.AddDays(1))
                    .Select(p => new { PaidAt = p.PaidAt!.Value, ServiceChargeAmount = p.Amount > 0 ? (p.ServiceChargeAmount ?? 0m) / p.Amount * p.AmountPaid : 0m })
                    .ToListAsync();
                perSource[key] = rows.Select(r => (r.PaidAt, r.ServiceChargeAmount)).ToList();
            }
            else
            {
                var rows = await _serviceChargeQueryService.GetEntriesAsync(ToLedgerFilters(filters, key));
                perSource[key] = rows.Where(r => r.PaidAt != null && r.PaidAt >= fromDate && r.PaidAt <= toDate.AddDays(1))
                    .Select(r => (r.PaidAt!.Value, r.ServiceChargeAmount)).ToList();
            }
        }

        var all = perSource.Values.SelectMany(v => v).ToList();
        var spanDays = (toDate - fromDate).TotalDays;
        var weekly = spanDays <= 90;

        var total = weekly ? BuildWeeklyTrend(all, fromDate, toDate) : BuildMonthlyTrend(all, fromDate, toDate);
        total.Series = Sources.Select(src =>
        {
            var t = weekly ? BuildWeeklyTrend(perSource[src.Key], fromDate, toDate) : BuildMonthlyTrend(perSource[src.Key], fromDate, toDate);
            return new AnalyticsNamedSeries { Name = src.Label, Values = t.AmountValues ?? new List<decimal>() };
        }).ToList();
        return total;
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
