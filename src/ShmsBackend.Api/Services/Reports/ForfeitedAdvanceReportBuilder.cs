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
/// Builds forfeited-advance-listing ReportData off ForfeitedAdvanceQueryService/ForfeitedAdvanceFilters,
/// mirroring VacateController.GetAllForfeitedAdvances' data (VacateForfeitedAdvance rows, !IsVoided).
/// </summary>
public class ForfeitedAdvanceReportBuilder
{
    private readonly ForfeitedAdvanceQueryService _forfeitedAdvanceQueryService;
    private readonly ShmsDbContext _context;

    public ForfeitedAdvanceReportBuilder(ForfeitedAdvanceQueryService forfeitedAdvanceQueryService, ShmsDbContext context)
    {
        _forfeitedAdvanceQueryService = forfeitedAdvanceQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(ForfeitedAdvanceFilters filters)
    {
        var advances = await _forfeitedAdvanceQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        var tenantIds = advances.Select(f => f.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var houseIds = advances.Select(f => f.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = advances.Select(f => f.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var rows = advances.Select(f =>
        {
            var houseNumber = houses.GetValueOrDefault(f.HouseId, "-");
            var flatName = flats.GetValueOrDefault(f.FlatId, "-");

            return new Dictionary<string, object?>
            {
                ["tenantName"] = tenants.GetValueOrDefault(f.TenantId, "-"),
                ["houseUnit"] = $"{houseNumber} - {flatName}",
                ["totalAdvance"] = f.TotalAdvanceAmount,
                ["appliedToDamages"] = f.AmountAppliedToDamages,
                ["forfeitedUnused"] = f.AmountForfeitedUnused,
                ["closedDate"] = f.CreatedAt
            };
        }).ToList();

        return new ReportData
        {
            Title = "Forfeited Advances Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters),
            Columns = new List<ReportColumn>
            {
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "houseUnit", Header = "House/Unit" },
                new() { Key = "totalAdvance", Header = "Total Advance" },
                new() { Key = "appliedToDamages", Header = "Applied to Damages" },
                new() { Key = "forfeitedUnused", Header = "Forfeited (Unused)" },
                new() { Key = "closedDate", Header = "Closed Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(ForfeitedAdvanceFilters filters)
    {
        var parts = new List<string>();
        if (filters.TenantId.HasValue) parts.Add($"Tenant: {filters.TenantId}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (filters.MinAmount.HasValue) parts.Add($"Min Amount: {filters.MinAmount}");
        if (filters.MaxAmount.HasValue) parts.Add($"Max Amount: {filters.MaxAmount}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All forfeited advances" : string.Join(" | ", parts);
    }
}
