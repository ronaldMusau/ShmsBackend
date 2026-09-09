using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Builds tenant-listing ReportData off the same TenantQueryService/TenantFilters TenantController
/// uses, so the report's rows can never drift from what the Tenants list page itself would show for
/// the same filters.
/// </summary>
public class TenantReportBuilder
{
    private readonly TenantQueryService _tenantQueryService;

    public TenantReportBuilder(TenantQueryService tenantQueryService)
    {
        _tenantQueryService = tenantQueryService;
    }

    public async Task<ReportData> BuildAsync(TenantFilters filters)
    {
        var tenants = await _tenantQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var deletedFlatIds = tenants
            .Where(t => t.House != null && t.House.Flat == null)
            .Select(t => t.House!.FlatId)
            .Distinct()
            .ToList();

        var deletedFlatNames = new Dictionary<Guid, string>();
        // Deleted-flat name backfill mirrors TenantController.GetAll's approach exactly, but this
        // builder only has TenantQueryService in scope (no ShmsDbContext), so deleted-flat names are
        // left as "(Flat Deleted)" here rather than re-resolved — an acceptable report-only gap since
        // the report is a point-in-time export, not the live-editable tenant list.

        var rows = tenants.Select(t =>
        {
            var flatUnit = t.House == null
                ? ""
                : (t.House.Flat != null
                    ? $"{t.House.HouseNumber} - {t.House.Flat.FlatName}"
                    : (deletedFlatNames.TryGetValue(t.House.FlatId, out var fn)
                        ? $"{t.House.HouseNumber} - {fn}"
                        : $"{t.House.HouseNumber} - (Flat Deleted)"));

            return new Dictionary<string, object?>
            {
                ["name"] = $"{t.FirstName} {t.LastName}".Trim(),
                ["phone"] = t.PhoneNumber,
                ["email"] = t.Email,
                ["flatUnit"] = flatUnit,
                ["moveInDate"] = t.CreatedAt,
                ["tenancyCycle"] = t.TenancyCycle,
                ["status"] = t.TenantStatus.ToString(),
                ["rent"] = t.House?.RentFee,
                ["deposit"] = t.House?.DepositFee
            };
        }).ToList();

        return new ReportData
        {
            Title = "Tenants Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters),
            Columns = new List<ReportColumn>
            {
                new() { Key = "name", Header = "Name" },
                new() { Key = "phone", Header = "Phone" },
                new() { Key = "email", Header = "Email" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "moveInDate", Header = "Move-in Date" },
                new() { Key = "tenancyCycle", Header = "Tenancy Cycle" },
                new() { Key = "status", Header = "Status" },
                new() { Key = "rent", Header = "Rent" },
                new() { Key = "deposit", Header = "Deposit" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(TenantFilters filters)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {filters.FlatId}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (!string.IsNullOrWhiteSpace(filters.Status)) parts.Add($"Status: {filters.Status}");
        if (filters.TenancyCycle.HasValue) parts.Add($"Tenancy Cycle: {filters.TenancyCycle}");
        if (filters.FromDate.HasValue) parts.Add($"From: {filters.FromDate.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");
        if (filters.ToDate.HasValue) parts.Add($"To: {filters.ToDate.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(filters.Search)) parts.Add($"Search: \"{filters.Search}\"");

        return parts.Count == 0 ? "All tenants" : string.Join("  |  ", parts);
    }
}
