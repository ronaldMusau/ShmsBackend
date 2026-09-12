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
/// Builds agent-listing ReportData off AgentQueryService/AgentFilters. Replicates
/// AgentController.GetAll's exact in-memory rating aggregation (ListingViewingSessions where
/// Status=="Closed" && AgentRating != null, grouped per agent) rather than reinventing it.
/// </summary>
public class AgentReportBuilder
{
    private readonly AgentQueryService _agentQueryService;
    private readonly ShmsDbContext _context;

    public AgentReportBuilder(AgentQueryService agentQueryService, ShmsDbContext context)
    {
        _agentQueryService = agentQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(AgentFilters filters)
    {
        var agents = await _agentQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        var agentIds = agents.Select(a => a.Id).ToList();
        var ratingAggregates = await _context.ListingViewingSessions
            .Where(s => agentIds.Contains(s.AgentId) && s.Status == "Closed" && s.AgentRating != null)
            .GroupBy(s => s.AgentId)
            .Select(g => new { AgentId = g.Key, Avg = g.Average(s => (double)s.AgentRating!.Value), Count = g.Count() })
            .ToListAsync();
        var ratingDict = ratingAggregates.ToDictionary(x => x.AgentId, x => x);

        var rows = agents.Select(a =>
        {
            ratingDict.TryGetValue(a.Id, out var r);
            return new Dictionary<string, object?>
            {
                ["name"] = $"{a.FirstName} {a.LastName}".Trim(),
                ["phone"] = a.PhoneNumber,
                ["email"] = a.Email,
                ["agencyName"] = a.AgencyName,
                ["licenseNumber"] = a.LicenseNumber,
                ["avgRating"] = r != null ? (double?)r.Avg : null,
                ["sessionsRated"] = r?.Count ?? 0,
                ["registeredDate"] = a.CreatedAt,
                ["status"] = a.IsActive ? "Active" : "Inactive"
            };
        }).ToList();

        return new ReportData
        {
            Title = "Agents Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters),
            Columns = new List<ReportColumn>
            {
                new() { Key = "name", Header = "Name" },
                new() { Key = "phone", Header = "Phone" },
                new() { Key = "email", Header = "Email" },
                new() { Key = "agencyName", Header = "Agency Name" },
                new() { Key = "licenseNumber", Header = "License Number" },
                new() { Key = "avgRating", Header = "Avg Rating" },
                new() { Key = "sessionsRated", Header = "Sessions Rated" },
                new() { Key = "registeredDate", Header = "Registered Date" },
                new() { Key = "status", Header = "Status" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(AgentFilters filters)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.Search)) parts.Add($"Search: \"{filters.Search}\"");
        if (!string.IsNullOrWhiteSpace(filters.County)) parts.Add($"County: {filters.County}");
        if (filters.IsActive.HasValue) parts.Add($"Status: {(filters.IsActive.Value ? "Active" : "Inactive")}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All agents" : string.Join(" | ", parts);
    }
}
