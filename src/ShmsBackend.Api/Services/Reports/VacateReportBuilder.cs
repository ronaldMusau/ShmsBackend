using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Builds vacate-request-listing ReportData off VacateQueryService/VacateFilters, mirroring
/// HouseReportBuilder's structure. Columns reflect the fields that actually live on VacateRequest
/// itself (no Settlement/Deposit/Damages breakdown columns — those live on separate VacateSettlement/
/// VacateForfeitedAdvance entities, out of scope for this report; adding them would need their own
/// join, not a mirror of VacateQueryService).
/// </summary>
public class VacateReportBuilder
{
    private readonly VacateQueryService _vacateQueryService;
    private readonly ShmsDbContext _context;

    public VacateReportBuilder(VacateQueryService vacateQueryService, ShmsDbContext context)
    {
        _vacateQueryService = vacateQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(VacateFilters filters)
    {
        var requests = await _vacateQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        var houseIds = requests.Select(v => v.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = requests.Select(v => v.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var tenantIds = requests.Select(v => v.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var rows = requests.Select(v =>
        {
            var houseNumber = houses.GetValueOrDefault(v.HouseId, "-");
            var flatName = flats.GetValueOrDefault(v.FlatId, "-");

            return new Dictionary<string, object?>
            {
                ["tenantName"] = tenants.GetValueOrDefault(v.TenantId, "-"),
                ["flatUnit"] = $"{houseNumber} - {flatName}",
                ["requestDate"] = v.CreatedAt,
                ["status"] = v.Status,
                ["period"] = $"{new DateTime(v.VacateYear, v.VacateMonth, 1):MMMM yyyy}",
                ["sitDeposit"] = v.SitDeposit,
                ["reason"] = v.Reason,
                ["clearDate"] = v.ClosedAt
            };
        }).ToList();

        var flatNameForSummary = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Vacate Requests Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatNameForSummary),
            Columns = new List<ReportColumn>
            {
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "requestDate", Header = "Request Date" },
                new() { Key = "status", Header = "Status" },
                new() { Key = "period", Header = "Vacate Period" },
                new() { Key = "sitDeposit", Header = "Sit Deposit" },
                new() { Key = "reason", Header = "Reason" },
                new() { Key = "clearDate", Header = "Clear Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(VacateFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (!string.IsNullOrWhiteSpace(filters.Status)) parts.Add($"Status: {filters.Status}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All vacate requests" : string.Join(" | ", parts);
    }
}
