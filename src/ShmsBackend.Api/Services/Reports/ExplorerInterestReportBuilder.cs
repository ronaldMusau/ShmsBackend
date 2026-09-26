using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Builds ExplorerInterest-listing ReportData off ExplorerInterestQueryService/ExplorerFilters,
/// batch-resolving Explorer, House/Flat, the originating viewing session's date (if any), and the
/// resulting Tenant (only for Converted rows, via SourceExplorerInterestId reverse-lookup) —
/// same Include-based batch dictionary approach as SessionReportBuilder, not per-row queries.
/// </summary>
public class ExplorerInterestReportBuilder
{
    private readonly ExplorerInterestQueryService _explorerInterestQueryService;
    private readonly ShmsDbContext _context;

    public ExplorerInterestReportBuilder(ExplorerInterestQueryService explorerInterestQueryService, ShmsDbContext context)
    {
        _explorerInterestQueryService = explorerInterestQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(ExplorerFilters filters)
    {
        var interests = await _explorerInterestQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(ei => ei.CreatedAt)
            .ToListAsync();

        var explorerIds = interests.Select(ei => ei.ExplorerId).Distinct().ToList();
        var houseIds = interests.Select(ei => ei.HouseId).Distinct().ToList();
        var sessionIds = interests.Where(ei => ei.SessionId.HasValue).Select(ei => ei.SessionId!.Value).Distinct().ToList();
        var interestIds = interests.Select(ei => ei.Id).ToList();

        var explorers = await _context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .ToListAsync();
        var houses = await _context.Houses
            .Include(h => h.Flat)
            .Where(h => houseIds.Contains(h.Id))
            .ToListAsync();
        var sessions = await _context.ListingViewingSessions
            .Where(s => sessionIds.Contains(s.Id))
            .ToListAsync();

        // Only Converted rows have a resulting Tenant — reverse-lookup via SourceExplorerInterestId.
        var tenants = await _context.Tenants
            .Where(t => t.SourceExplorerInterestId != null && interestIds.Contains(t.SourceExplorerInterestId.Value))
            .ToListAsync();

        var explorerDict = explorers.ToDictionary(e => e.Id);
        var houseDict = houses.ToDictionary(h => h.Id);
        var sessionDict = sessions.ToDictionary(s => s.Id);
        var tenantByInterest = tenants.ToDictionary(t => t.SourceExplorerInterestId!.Value);

        var rows = interests.Select(ei =>
        {
            explorerDict.TryGetValue(ei.ExplorerId, out var explorer);
            houseDict.TryGetValue(ei.HouseId, out var house);
            ListingViewingSession? session = ei.SessionId.HasValue ? sessionDict.GetValueOrDefault(ei.SessionId.Value) : null;
            var tenant = ei.Status == "Converted" ? tenantByInterest.GetValueOrDefault(ei.Id) : null;

            return new Dictionary<string, object?>
            {
                ["explorerName"] = explorer != null ? $"{explorer.FirstName} {explorer.LastName}".Trim() : "",
                ["explorerEmail"] = explorer?.Email,
                ["explorerPhone"] = explorer?.PhoneNumber,
                ["houseNumber"] = house?.HouseNumber,
                ["flatName"] = house?.Flat?.FlatName,
                ["status"] = ei.Status,
                ["sessionDate"] = session?.ScheduledAt,
                ["tenantName"] = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "",
                ["createdAt"] = ei.CreatedAt
            };
        }).ToList();

        var flatName = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Explorer Interest Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatName),
            Columns = new List<ReportColumn>
            {
                new() { Key = "explorerName", Header = "Explorer" },
                new() { Key = "explorerEmail", Header = "Email" },
                new() { Key = "explorerPhone", Header = "Phone" },
                new() { Key = "houseNumber", Header = "House/Unit" },
                new() { Key = "flatName", Header = "Flat" },
                new() { Key = "status", Header = "Status" },
                new() { Key = "sessionDate", Header = "Session Date" },
                new() { Key = "tenantName", Header = "Converted Tenant" },
                new() { Key = "createdAt", Header = "Created At" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(ExplorerFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.Status)) parts.Add($"Status: {filters.Status}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (filters.ExplorerId.HasValue) parts.Add($"Explorer: {filters.ExplorerId}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All explorer interests" : string.Join(" | ", parts);
    }
}
