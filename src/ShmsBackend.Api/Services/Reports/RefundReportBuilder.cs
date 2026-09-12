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
/// Builds refund-listing ReportData off RefundQueryService/RefundFilters, mirroring
/// VacateController.GetAllRefunds' data (VacateSettlement rows where money flows management -> tenant).
/// </summary>
public class RefundReportBuilder
{
    private readonly RefundQueryService _refundQueryService;
    private readonly ShmsDbContext _context;

    public RefundReportBuilder(RefundQueryService refundQueryService, ShmsDbContext context)
    {
        _refundQueryService = refundQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(RefundFilters filters)
    {
        var settlements = await _refundQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        var tenantIds = settlements.Select(s => s.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");

        var houseIds = settlements.Select(s => s.HouseId).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        var flatIds = settlements.Select(s => s.FlatId).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var rows = settlements.Select(s =>
        {
            var houseNumber = houses.GetValueOrDefault(s.HouseId, "-");
            var flatName = flats.GetValueOrDefault(s.FlatId, "-");

            return new Dictionary<string, object?>
            {
                ["tenantName"] = tenants.GetValueOrDefault(s.TenantId, "-"),
                ["flatUnit"] = $"{houseNumber} - {flatName}",
                ["amount"] = s.Amount,
                ["description"] = s.Description,
                ["status"] = s.PaidAt != null ? "Paid" : "Refundable",
                ["paidDate"] = s.PaidAt,
                ["createdDate"] = s.CreatedAt
            };
        }).ToList();

        var flatNameForSummary = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Refunds Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatNameForSummary),
            Columns = new List<ReportColumn>
            {
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "amount", Header = "Amount" },
                new() { Key = "description", Header = "Description" },
                new() { Key = "status", Header = "Status" },
                new() { Key = "paidDate", Header = "Paid Date" },
                new() { Key = "createdDate", Header = "Created Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(RefundFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (filters.TenantId.HasValue) parts.Add($"Tenant: {filters.TenantId}");
        if (filters.Month.HasValue) parts.Add($"Month: {filters.Month}");
        if (filters.Year.HasValue) parts.Add($"Year: {filters.Year}");
        if (filters.IsPaid.HasValue) parts.Add(filters.IsPaid.Value ? "Status: Paid" : "Status: Refundable");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All refunds" : string.Join(" | ", parts);
    }
}
