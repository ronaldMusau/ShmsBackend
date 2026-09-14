using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Analytics;

/// <summary>
/// Mirrors OverdueReportBuilder's exact approach — there is no dedicated OverdueTenant entity or
/// query service; "overdue" is derived live from Payments (materialized, then TenancyCycle-filtered
/// in-memory) joined against TenantWarnings (also materialized), with Warning Stage derived per
/// tenant via the same C# conditional the report builder uses. This intentionally does NOT push the
/// computation into a single SQL aggregate — the report builder can't either, for the same reason.
/// </summary>
public class OverdueAnalyticsService
{
    private readonly ShmsDbContext _context;

    public OverdueAnalyticsService(ShmsDbContext context)
    {
        _context = context;
    }

    private class TenantOverdueGroup
    {
        public Guid TenantId { get; set; }
        public DateTime OldestUnpaidDueDate { get; set; }
        public decimal TotalArrears { get; set; }
        public int OverdueDays { get; set; }
    }

    private async Task<List<(TenantOverdueGroup Group, string WarningStage)>> ComputeOverdueGroupsAsync(
        Guid? flatId, Guid? landlordId, DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;

        var overdueQuery = _context.Payments
            .Include(p => p.Tenant)
            .Where(p => (p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                    || p.PaymentStatus == PaymentTransactionStatus.Overdue)
                && p.DueDate < now
                && !p.IsDeleted);

        if (flatId.HasValue) overdueQuery = overdueQuery.Where(p => p.FlatId == flatId.Value);
        if (landlordId.HasValue) overdueQuery = overdueQuery.Where(p => p.LandlordId == landlordId.Value);
        if (fromDate.HasValue) overdueQuery = overdueQuery.Where(p => p.DueDate >= fromDate.Value);
        if (toDate.HasValue) overdueQuery = overdueQuery.Where(p => p.DueDate <= toDate.Value.Date.AddDays(1));

        var overduePayments = (await overdueQuery.ToListAsync())
            .Where(p => p.Tenant != null && p.TenancyCycle == p.Tenant.TenancyCycle)
            .ToList();

        var groups = overduePayments
            .GroupBy(p => p.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                OldestUnpaidDueDate = g.Min(p => p.DueDate),
                TotalArrears = g.Sum(p => p.Balance)
            })
            .Select(g => new TenantOverdueGroup
            {
                TenantId = g.TenantId,
                OldestUnpaidDueDate = g.OldestUnpaidDueDate,
                TotalArrears = g.TotalArrears,
                OverdueDays = (int)(now - g.OldestUnpaidDueDate).TotalDays
            })
            .Where(g => g.OverdueDays > 0)
            .ToList();

        var tenantIds = groups.Select(g => g.TenantId).ToList();
        var warnings = await _context.TenantWarnings
            .Where(w => tenantIds.Contains(w.TenantId) && !w.IsDeleted)
            .ToListAsync();
        var warningsByTenant = warnings.GroupBy(w => w.TenantId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<(TenantOverdueGroup, string)>();
        foreach (var g in groups)
        {
            warningsByTenant.TryGetValue(g.TenantId, out var tenantWarnings);
            var warning1 = tenantWarnings?.Where(w => w.WarningNumber == 1).OrderByDescending(w => w.SentAt).FirstOrDefault();
            var warning2 = tenantWarnings?.Where(w => w.WarningNumber == 2).OrderByDescending(w => w.SentAt).FirstOrDefault();

            var warningStage = warning2 != null ? "Warning 2" : warning1 != null ? "Warning 1" : "None";
            result.Add((g, warningStage));
        }

        return result;
    }

    public async Task<AnalyticsBreakdownResult> GetWarningStageBreakdownAsync(Guid? flatId, Guid? landlordId, DateTime? fromDate, DateTime? toDate)
    {
        var groups = await ComputeOverdueGroupsAsync(flatId, landlordId, fromDate, toDate);

        string[] allStages = { "None", "Warning 1", "Warning 2" };
        var items = allStages.Select(stage =>
        {
            var matching = groups.Where(g => g.WarningStage == stage).ToList();
            return new AnalyticsCategoryItem
            {
                Label = stage,
                Count = matching.Count,
                Amount = matching.Sum(g => g.Group.TotalArrears)
            };
        }).ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = items.Sum(i => i.Amount ?? 0m)
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(Guid? flatId, Guid? landlordId, DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;
        var effectiveFrom = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var effectiveTo = (toDate ?? now).Date;

        var groups = await ComputeOverdueGroupsAsync(flatId, landlordId, effectiveFrom, effectiveTo);
        var dates = groups.Select(g => g.Group.OldestUnpaidDueDate).ToList();

        var spanDays = (effectiveTo - effectiveFrom).TotalDays;

        return spanDays <= 90
            ? BuildWeeklyTrend(dates, effectiveFrom, effectiveTo)
            : BuildMonthlyTrend(dates, effectiveFrom, effectiveTo);
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
