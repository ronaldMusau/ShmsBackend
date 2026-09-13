using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Builds deduction-listing ReportData off DeductionQueryService/DeductionFilters, mirroring
/// DeductionController.GetAll's data (Deduction rows scoped by Landlord/Tenant/House/Flat/Year).
/// </summary>
public class DeductionReportBuilder
{
    private readonly DeductionQueryService _deductionQueryService;
    private readonly ShmsDbContext _context;

    public DeductionReportBuilder(DeductionQueryService deductionQueryService, ShmsDbContext context)
    {
        _deductionQueryService = deductionQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(DeductionFilters filters)
    {
        var deductions = await _deductionQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        var landlordIds = deductions.Select(d => d.LandlordId).Distinct().ToList();
        var landlords = await _context.Landlords
            .Where(l => landlordIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => $"{l.FirstName} {l.LastName}");

        var tenantIds = deductions.Select(d => d.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var houseIds = deductions.Select(d => d.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = deductions.Select(d => d.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var rows = deductions.Select(d =>
        {
            var houseNumber = houses.GetValueOrDefault(d.HouseId, "-");
            var flatName = flats.GetValueOrDefault(d.FlatId, "-");

            return new Dictionary<string, object?>
            {
                ["landlordName"] = landlords.GetValueOrDefault(d.LandlordId, "-"),
                ["tenantName"] = tenants.GetValueOrDefault(d.TenantId, "-"),
                ["houseUnit"] = $"{houseNumber} - {flatName}",
                ["amount"] = d.Amount,
                ["description"] = d.Description,
                ["monthYear"] = new DateTime(d.DeductionYear, d.DeductionMonth, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture),
                ["createdDate"] = d.CreatedAt
            };
        }).ToList();

        var flatNameForSummary = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Deductions Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatNameForSummary),
            Columns = new List<ReportColumn>
            {
                new() { Key = "landlordName", Header = "Landlord Name" },
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "houseUnit", Header = "House/Unit" },
                new() { Key = "amount", Header = "Amount" },
                new() { Key = "description", Header = "Description" },
                new() { Key = "monthYear", Header = "Month/Year" },
                new() { Key = "createdDate", Header = "Created Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(DeductionFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (filters.LandlordId.HasValue) parts.Add($"Landlord: {filters.LandlordId}");
        if (filters.TenantId.HasValue) parts.Add($"Tenant: {filters.TenantId}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (filters.Year.HasValue) parts.Add($"Year: {filters.Year}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All deductions" : string.Join(" | ", parts);
    }
}
