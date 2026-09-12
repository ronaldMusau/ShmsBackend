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
/// Builds viewing-session-listing ReportData off ListingViewingSessionQueryService/SessionFilters,
/// batch-resolving House/Flat/Agent/Explorer the same way SessionController.GetAllSessions does
/// (Include-based batch dictionary lookups, not per-row queries). Deliberately does NOT replicate that
/// controller's live agentDayCount/loadLevel computation — that's a live workload snapshot for the
/// day's active queue, not report data.
/// </summary>
public class SessionReportBuilder
{
    private readonly ListingViewingSessionQueryService _sessionQueryService;
    private readonly ShmsDbContext _context;

    public SessionReportBuilder(ListingViewingSessionQueryService sessionQueryService, ShmsDbContext context)
    {
        _sessionQueryService = sessionQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(SessionFilters filters)
    {
        var sessions = await _sessionQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var houseIds = sessions.Select(s => s.HouseId).Distinct().ToList();
        var agentIds = sessions.Select(s => s.AgentId).Distinct().ToList();
        var explorerIds = sessions.Select(s => s.ExplorerId).Distinct().ToList();

        var houses = await _context.Houses
            .Include(h => h.Flat)
            .Where(h => houseIds.Contains(h.Id))
            .ToListAsync();
        var agents = await _context.Agents
            .Where(a => agentIds.Contains(a.Id))
            .ToListAsync();
        var explorers = await _context.Explorers
            .Where(e => explorerIds.Contains(e.Id))
            .ToListAsync();

        var houseDict = houses.ToDictionary(h => h.Id);
        var agentDict = agents.ToDictionary(a => a.Id);
        var explorerDict = explorers.ToDictionary(e => e.Id);

        var rows = sessions.Select(s =>
        {
            houseDict.TryGetValue(s.HouseId, out var house);
            agentDict.TryGetValue(s.AgentId, out var agent);
            explorerDict.TryGetValue(s.ExplorerId, out var explorer);

            return new Dictionary<string, object?>
            {
                ["houseNumber"] = house?.HouseNumber,
                ["flatName"] = house?.Flat?.FlatName,
                ["explorerName"] = explorer != null ? $"{explorer.FirstName} {explorer.LastName}".Trim() : "",
                ["agentName"] = agent != null ? $"{agent.FirstName} {agent.LastName}".Trim() : "",
                ["scheduledAt"] = s.ScheduledAt,
                ["status"] = s.Status,
                ["declineReason"] = s.Status == "Declined" ? s.DeclineReason : "",
                ["rating"] = s.AgentRating,
                ["closedAt"] = s.ClosedAt
            };
        }).ToList();

        var flatName = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Booked Sessions Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatName),
            Columns = new List<ReportColumn>
            {
                new() { Key = "houseNumber", Header = "House/Unit" },
                new() { Key = "flatName", Header = "Flat" },
                new() { Key = "explorerName", Header = "Explorer" },
                new() { Key = "agentName", Header = "Agent" },
                new() { Key = "scheduledAt", Header = "Scheduled At" },
                new() { Key = "status", Header = "Status" },
                new() { Key = "declineReason", Header = "Decline Reason" },
                new() { Key = "rating", Header = "Rating" },
                new() { Key = "closedAt", Header = "Closed At" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(SessionFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.Status)) parts.Add($"Status: {filters.Status}");
        if (filters.AgentId.HasValue) parts.Add($"Agent: {filters.AgentId}");
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All booked sessions" : string.Join(" | ", parts);
    }
}
