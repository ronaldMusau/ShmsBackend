using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ShmsBackend.Api.Models.Analytics;
using ShmsBackend.Api.Services.Agreements;

namespace ShmsBackend.Api.Services.Analytics;

/// <summary>
/// No AgreementFilters/query-service exists (confirmed) — GetAllUserAgreementStatusesAsync is the
/// only reusable source of this data, and it already derives Role/Context/FlatId correctly, so this
/// service calls it directly (full, unfiltered by role/flat) and does the date filtering + bucketing
/// itself in-memory rather than duplicating that derivation logic against ShmsDbContext directly.
/// Management-only — confirmed no landlord-facing precedent for this data exists anywhere.
/// </summary>
public class AgreementAnalyticsService
{
    private readonly IAgreementService _agreementService;

    public AgreementAnalyticsService(IAgreementService agreementService)
    {
        _agreementService = agreementService;
    }

    // Rows with no AgreementUploadedAt (NotSent/Sent — never uploaded) are always included regardless
    // of the date range, since they represent an ongoing backlog state, not a dated event — excluding
    // them whenever a date filter is applied would make that backlog invisible in a status snapshot.
    private static IEnumerable<UserAgreementStatusDto> FilterByDate(IEnumerable<UserAgreementStatusDto> source, DateTime? fromDate, DateTime? toDate)
    {
        if (!fromDate.HasValue && !toDate.HasValue) return source;
        return source.Where(d =>
            d.AgreementUploadedAt == null ||
            ((!fromDate.HasValue || d.AgreementUploadedAt >= fromDate.Value) &&
             (!toDate.HasValue || d.AgreementUploadedAt <= toDate.Value.Date.AddDays(1))));
    }

    public async Task<AnalyticsBreakdownResult> GetStatusBreakdownAsync(DateTime? fromDate, DateTime? toDate)
    {
        var all = await _agreementService.GetAllUserAgreementStatusesAsync(null, null);
        var filtered = FilterByDate(all, fromDate, toDate).ToList();

        // Exhaustive AgreementStatus enum values (confirmed 5, not 4 — "Sent" is a real, distinct
        // status between NotSent and PendingVerification, easy to miss if assumed rather than read).
        string[] allStatuses = { "NotSent", "Sent", "PendingVerification", "Verified", "Rejected" };
        var items = allStatuses.Select(status => new AnalyticsCategoryItem
        {
            Label = status,
            Count = filtered.Count(d => d.AgreementStatus == status),
            Amount = null
        }).ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = null
        };
    }

    public async Task<AnalyticsBreakdownResult> GetRoleBreakdownAsync(DateTime? fromDate, DateTime? toDate)
    {
        var all = await _agreementService.GetAllUserAgreementStatusesAsync(null, null);
        var filtered = FilterByDate(all, fromDate, toDate).ToList();

        // Only Landlord/Agent/Tenant are valid roles for an agreement (matches AgreementController's
        // own role validation) — Explorer has no agreement concept and is excluded.
        string[] allRoles = { "Tenant", "Landlord", "Agent" };
        var items = allRoles.Select(role => new AnalyticsCategoryItem
        {
            Label = role,
            Count = filtered.Count(d => d.Role == role),
            Amount = null
        }).ToList();

        return new AnalyticsBreakdownResult
        {
            Items = items,
            TotalCount = items.Sum(i => i.Count),
            TotalAmount = null
        };
    }

    public async Task<AnalyticsTrendResult> GetTrendAsync(DateTime? fromDate, DateTime? toDate)
    {
        var now = DateTime.UtcNow;
        var effectiveFrom = (fromDate ?? new DateTime(now.Year, now.Month, 1)).Date;
        var effectiveTo = (toDate ?? now).Date;

        var all = await _agreementService.GetAllUserAgreementStatusesAsync(null, null);

        // Trend is naturally about "when agreements were uploaded" — rows with no upload date
        // (NotSent/Sent) don't have an event to bucket and are excluded here (unlike the breakdown
        // methods, which always include them as an ongoing-backlog category).
        var dates = all
            .Where(d => d.AgreementUploadedAt.HasValue
                && d.AgreementUploadedAt.Value >= effectiveFrom
                && d.AgreementUploadedAt.Value <= effectiveTo.AddDays(1))
            .Select(d => d.AgreementUploadedAt!.Value)
            .ToList();

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
