using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Reports;

public class OverdueFilters
{
    public Guid? FlatId { get; set; }
    public string? WarningStage { get; set; }   // "None", "Warning 1", "Warning 2"
    public decimal? MinAmountOwed { get; set; }
    public decimal? MaxAmountOwed { get; set; }
}

/// <summary>
/// Builds overdue-tenants ReportData by replicating OverdueController.GetOverdueTenants' exact
/// computation — there is no dedicated OverdueTenant entity; "overdue" is derived live from Payments
/// (Pending/PartiallyPaid/Overdue rows past DueDate, scoped to the tenant's current TenancyCycle) and
/// "warning stage" from TenantWarnings. This intentionally duplicates that predicate rather than
/// reinterpreting it, so the report can never disagree with the live Overdue Tenants list.
/// </summary>
public class OverdueReportBuilder
{
    private readonly ShmsDbContext _context;

    public OverdueReportBuilder(ShmsDbContext context)
    {
        _context = context;
    }

    public async Task<ReportData> BuildAsync(OverdueFilters filters)
    {
        var now = DateTime.UtcNow;

        var overdueQuery = _context.Payments
            .Include(p => p.Tenant)
            .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
            .Where(p => (p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                    || p.PaymentStatus == PaymentTransactionStatus.Overdue)
                && p.DueDate < now
                && !p.IsDeleted);

        if (filters.FlatId.HasValue)
            overdueQuery = overdueQuery.Where(p => p.FlatId == filters.FlatId.Value);

        var overduePayments = (await overdueQuery.ToListAsync())
            .Where(p => p.Tenant != null && p.TenancyCycle == p.Tenant.TenancyCycle)
            .ToList();

        var groups = overduePayments
            .GroupBy(p => p.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Tenant = g.First().Tenant!,
                House = g.First().House,
                OldestUnpaidDueDate = g.Min(p => p.DueDate),
                TotalArrears = g.Sum(p => p.Balance)
            })
            .Select(g => new
            {
                g.TenantId,
                g.Tenant,
                g.House,
                g.OldestUnpaidDueDate,
                g.TotalArrears,
                OverdueDays = (int)(now - g.OldestUnpaidDueDate).TotalDays
            })
            .Where(g => g.OverdueDays > 0)
            .ToList();

        if (filters.MinAmountOwed.HasValue)
            groups = groups.Where(g => g.TotalArrears >= filters.MinAmountOwed.Value).ToList();
        if (filters.MaxAmountOwed.HasValue)
            groups = groups.Where(g => g.TotalArrears <= filters.MaxAmountOwed.Value).ToList();

        var tenantIds = groups.Select(g => g.TenantId).ToList();
        var warnings = await _context.TenantWarnings
            .Where(w => tenantIds.Contains(w.TenantId) && !w.IsDeleted)
            .ToListAsync();
        var warningsByTenant = warnings.GroupBy(w => w.TenantId).ToDictionary(g => g.Key, g => g.ToList());

        var normalizedStageFilter = string.IsNullOrWhiteSpace(filters.WarningStage)
            ? null
            : filters.WarningStage.Replace(" ", "").ToLowerInvariant();

        var rows = new List<Dictionary<string, object?>>();
        foreach (var g in groups)
        {
            warningsByTenant.TryGetValue(g.TenantId, out var tenantWarnings);
            var warning1 = tenantWarnings?.Where(w => w.WarningNumber == 1).OrderByDescending(w => w.SentAt).FirstOrDefault();
            var warning2 = tenantWarnings?.Where(w => w.WarningNumber == 2).OrderByDescending(w => w.SentAt).FirstOrDefault();

            var warningStage = warning2 != null ? "Warning 2" : warning1 != null ? "Warning 1" : "None";
            var lastWarningDate = warning2?.SentAt ?? warning1?.SentAt;

            if (normalizedStageFilter != null && normalizedStageFilter != warningStage.Replace(" ", "").ToLowerInvariant())
                continue;

            rows.Add(new Dictionary<string, object?>
            {
                ["tenantName"] = $"{g.Tenant.FirstName} {g.Tenant.LastName}".Trim(),
                ["flatUnit"] = g.House != null ? $"{g.House.HouseNumber} - {(g.House.Flat?.FlatName ?? "-")}" : "-",
                ["daysOverdue"] = g.OverdueDays,
                ["amountOwed"] = g.TotalArrears,
                ["warningStage"] = warningStage,
                ["lastWarningDate"] = lastWarningDate
            });
        }

        var flatName = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Overdue Tenants Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatName),
            Columns = new List<ReportColumn>
            {
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "daysOverdue", Header = "Days Overdue" },
                new() { Key = "amountOwed", Header = "Amount Owed" },
                new() { Key = "warningStage", Header = "Warning Stage" },
                new() { Key = "lastWarningDate", Header = "Last Warning Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(OverdueFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (!string.IsNullOrWhiteSpace(filters.WarningStage)) parts.Add($"Warning Stage: {filters.WarningStage}");
        if (filters.MinAmountOwed.HasValue) parts.Add($"Amount Owed from {filters.MinAmountOwed:N0}");
        if (filters.MaxAmountOwed.HasValue) parts.Add($"Amount Owed up to {filters.MaxAmountOwed:N0}");

        return parts.Count == 0 ? "All overdue tenants" : string.Join(" | ", parts);
    }
}
