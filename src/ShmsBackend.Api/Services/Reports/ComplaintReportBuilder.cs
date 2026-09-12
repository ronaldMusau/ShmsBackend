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
/// Builds complaint-listing ReportData off ComplaintQueryService/ComplaintFilters, mirroring
/// HouseReportBuilder/VacateReportBuilder's structure.
/// </summary>
public class ComplaintReportBuilder
{
    private readonly ComplaintQueryService _complaintQueryService;
    private readonly ShmsDbContext _context;

    public ComplaintReportBuilder(ComplaintQueryService complaintQueryService, ShmsDbContext context)
    {
        _complaintQueryService = complaintQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(ComplaintFilters filters)
    {
        var complaints = await _complaintQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        var complaintTypeIds = complaints.Select(c => c.ComplaintTypeId).Distinct().ToList();
        var complaintTypes = await _context.ComplaintTypes
            .Where(t => complaintTypeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        var houseIds = complaints.Select(c => c.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = complaints.Select(c => c.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var tenantIds = complaints.Select(c => c.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var rows = complaints.Select(c =>
        {
            var houseNumber = houses.GetValueOrDefault(c.HouseId, "-");
            var flatName = flats.GetValueOrDefault(c.FlatId, "-");

            return new Dictionary<string, object?>
            {
                ["ticketNumber"] = c.TicketNumber,
                ["tenantName"] = tenants.GetValueOrDefault(c.TenantId, "-"),
                ["flatUnit"] = $"{houseNumber} - {flatName}",
                ["category"] = complaintTypes.GetValueOrDefault(c.ComplaintTypeId, "Unknown"),
                ["status"] = c.Status,
                ["billableAmount"] = c.BillableAmount,
                ["raisedDate"] = c.CreatedAt,
                ["closedDate"] = c.ClosedAt
            };
        }).ToList();

        var flatNameForSummary = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Complaints Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatNameForSummary),
            Columns = new List<ReportColumn>
            {
                new() { Key = "ticketNumber", Header = "Ticket Reference" },
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "category", Header = "Category" },
                new() { Key = "status", Header = "Status" },
                new() { Key = "billableAmount", Header = "Billable Amount" },
                new() { Key = "raisedDate", Header = "Raised Date" },
                new() { Key = "closedDate", Header = "Closed Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(ComplaintFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (!string.IsNullOrWhiteSpace(filters.Status)) parts.Add($"Status: {filters.Status}");
        if (filters.IsBillable.HasValue) parts.Add($"Billable: {(filters.IsBillable.Value ? "Yes" : "No")}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All complaints" : string.Join(" | ", parts);
    }
}
