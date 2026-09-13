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
/// Builds expense-listing ReportData off ExpenseQueryService/ExpenseFilters, mirroring
/// RefundReportBuilder's correlated-lookup shape — Expense has scalar FlatId/HouseId only (no nav
/// properties), so Flat/House names are resolved via bulk dictionary lookups, same pattern.
/// </summary>
public class ExpenseReportBuilder
{
    private readonly ExpenseQueryService _expenseQueryService;
    private readonly ShmsDbContext _context;

    public ExpenseReportBuilder(ExpenseQueryService expenseQueryService, ShmsDbContext context)
    {
        _expenseQueryService = expenseQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(ExpenseFilters filters)
    {
        var expenses = await _expenseQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(x => x.ExpenseDate)
            .ToListAsync();

        var flatIds = expenses.Where(x => x.FlatId.HasValue).Select(x => x.FlatId!.Value).Distinct().ToList();
        var flats = await _context.Flats
            .Where(f => flatIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var houseIds = expenses.Where(x => x.HouseId.HasValue).Select(x => x.HouseId!.Value).Distinct().ToList();
        var houses = await _context.Houses
            .Where(h => houseIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.HouseNumber);

        // CreatedByUserId can be either a Landlord (self-logged, a PortalUser) or a staff member
        // (SuperAdmin/Admin/Accountant, an Admin) — resolved via both base tables and merged.
        var creatorNames = await ReportBuilderHelpers.ResolveCreatorNamesAsync(
            _context, expenses.Select(x => x.CreatedByUserId));

        var rows = expenses.Select(x => new Dictionary<string, object?>
        {
            ["flat"] = x.FlatId.HasValue ? flats.GetValueOrDefault(x.FlatId.Value, "-") : "",
            ["house"] = x.HouseId.HasValue ? houses.GetValueOrDefault(x.HouseId.Value, "-") : "",
            ["amount"] = x.Amount,
            ["description"] = x.Description,
            ["expenseDate"] = x.ExpenseDate,
            ["loggedBy"] = creatorNames.GetValueOrDefault(x.CreatedByUserId, "-")
        }).ToList();

        var flatNameForSummary = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Expenses Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatNameForSummary),
            Columns = new List<ReportColumn>
            {
                new() { Key = "flat", Header = "Flat" },
                new() { Key = "house", Header = "House" },
                new() { Key = "amount", Header = "Amount" },
                new() { Key = "description", Header = "Description" },
                new() { Key = "expenseDate", Header = "Expense Date" },
                new() { Key = "loggedBy", Header = "Logged By" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(ExpenseFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (filters.MinAmount.HasValue) parts.Add($"Min Amount: {filters.MinAmount}");
        if (filters.MaxAmount.HasValue) parts.Add($"Max Amount: {filters.MaxAmount}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        return parts.Count == 0 ? "All expenses" : string.Join(" | ", parts);
    }
}
