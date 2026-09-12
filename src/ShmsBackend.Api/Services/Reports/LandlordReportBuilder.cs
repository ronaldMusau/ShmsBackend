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
/// Builds landlord-listing ReportData off LandlordQueryService/LandlordFilters, mirroring
/// ComplaintReportBuilder/HouseReportBuilder's structure.
/// </summary>
public class LandlordReportBuilder
{
    private readonly LandlordQueryService _landlordQueryService;
    private readonly ShmsDbContext _context;

    public LandlordReportBuilder(LandlordQueryService landlordQueryService, ShmsDbContext context)
    {
        _landlordQueryService = landlordQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(LandlordFilters filters)
    {
        var landlords = await _landlordQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        var landlordIds = landlords.Select(l => l.Id).ToList();
        var flatCounts = await _context.Flats
            .Where(f => landlordIds.Contains(f.LandlordId))
            .GroupBy(f => f.LandlordId)
            .Select(g => new { LandlordId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.LandlordId, g => g.Count);

        var rows = landlords.Select(l => new Dictionary<string, object?>
        {
            ["name"] = $"{l.FirstName} {l.LastName}".Trim(),
            ["phone"] = l.PhoneNumber,
            ["email"] = l.Email,
            ["agencyName"] = l.AgencyName,
            ["county"] = l.County,
            ["flatCount"] = flatCounts.GetValueOrDefault(l.Id, 0),
            ["registeredDate"] = l.CreatedAt,
            ["status"] = l.IsActive ? "Active" : "Inactive"
        }).ToList();

        return new ReportData
        {
            Title = "Landlords Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters),
            Columns = new List<ReportColumn>
            {
                new() { Key = "name", Header = "Name" },
                new() { Key = "phone", Header = "Phone" },
                new() { Key = "email", Header = "Email" },
                new() { Key = "agencyName", Header = "Agency Name" },
                new() { Key = "county", Header = "County" },
                new() { Key = "flatCount", Header = "No. of Flats" },
                new() { Key = "registeredDate", Header = "Registered Date" },
                new() { Key = "status", Header = "Status" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(LandlordFilters filters)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.Search)) parts.Add($"Search: \"{filters.Search}\"");
        if (!string.IsNullOrWhiteSpace(filters.County)) parts.Add($"County: {filters.County}");
        if (filters.IsActive.HasValue) parts.Add($"Status: {(filters.IsActive.Value ? "Active" : "Inactive")}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All landlords" : string.Join(" | ", parts);
    }
}
