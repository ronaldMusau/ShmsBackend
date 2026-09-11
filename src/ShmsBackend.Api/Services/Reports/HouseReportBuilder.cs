using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// Builds house-listing ReportData off the same HouseQueryService/HouseFilters shared by admin-wide
/// and landlord-scoped house reports, mirroring TenantReportBuilder/PaymentReportBuilder's structure.
/// </summary>
public class HouseReportBuilder
{
    private readonly HouseQueryService _houseQueryService;
    private readonly ShmsDbContext _context;

    public HouseReportBuilder(HouseQueryService houseQueryService, ShmsDbContext context)
    {
        _houseQueryService = houseQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(HouseFilters filters)
    {
        var houses = await _houseQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync();

        var houseIds = houses.Select(h => h.Id).ToList();

        // CurrentTenant is resolved here, per-row, after materialization — deliberately NOT via the
        // ambiguous h.Tenants.FirstOrDefault() pattern used elsewhere (e.g. PortalFlatController):
        // during the SettlingVacate window a house can transiently have two non-deleted tenants
        // pointing at it (the outgoing tenant, still SettlingVacate and not yet soft-deleted, and the
        // newly-assigned incoming one) — FirstOrDefault() with no ordering/status filter picks between
        // them non-deterministically. Instead: prefer a non-SettlingVacate tenant (most recently
        // created), falling back to the SettlingVacate one only if that's the only tenant on the house.
        var candidateTenants = await _context.Tenants
            .Where(t => t.HouseId.HasValue && houseIds.Contains(t.HouseId.Value))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        var currentTenantByHouse = candidateTenants
            .GroupBy(t => t.HouseId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.FirstOrDefault(t => t.TenantStatus != TenantStatus.SettlingVacate) ?? g.First());

        var hasMonthFilter = filters.Month.HasValue && filters.Year.HasValue;

        // Batched per-month rent-status resolution — mirrors PaymentDistributionService's
        // TenantId+HouseId+TenancyCycle+Month+Year filter (excluding IsInitialPayment/IsDeleted rows)
        // and its OrderByDescending(CreatedAt) tie-break — but scoped by HouseId only, since a report
        // row cares about "what happened in this house that month", not which tenant's cycle it fell
        // under. Only houses with a currently-resolved tenant are considered; vacant houses always
        // show "—" regardless of this filter.
        var monthlyStatusByHouse = new Dictionary<Guid, string>();
        if (hasMonthFilter)
        {
            var occupiedHouseIds = currentTenantByHouse.Keys.ToList();
            if (occupiedHouseIds.Count > 0)
            {
                var monthPayments = await _context.Payments
                    .Where(p => occupiedHouseIds.Contains(p.HouseId)
                        && p.Month == filters.Month!.Value && p.Year == filters.Year!.Value
                        && !p.IsInitialPayment && !p.IsDeleted)
                    .OrderByDescending(p => p.CreatedAt)
                    .ToListAsync();

                monthlyStatusByHouse = monthPayments
                    .GroupBy(p => p.HouseId)
                    .ToDictionary(g => g.Key, g => g.First().PaymentStatus.ToString());
            }
        }

        var monthlyStatusHeader = hasMonthFilter
            ? $"Status ({new DateTime(filters.Year!.Value, filters.Month!.Value, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture)})"
            : "Current Status";

        var rows = houses.Select(h =>
        {
            currentTenantByHouse.TryGetValue(h.Id, out var tenant);

            string monthlyStatus;
            if (tenant == null)
                monthlyStatus = "—";
            else if (hasMonthFilter)
                monthlyStatus = monthlyStatusByHouse.GetValueOrDefault(h.Id, "No Payment");
            else
                monthlyStatus = h.PaymentStatus.ToString();

            return new Dictionary<string, object?>
            {
                ["houseNumber"] = h.HouseNumber,
                ["flatName"] = h.Flat?.FlatName,
                ["houseType"] = h.HouseTypeRef?.Name,
                ["occupancyStatus"] = h.OccupancyStatus.ToString(),
                ["rent"] = h.RentFee,
                ["deposit"] = h.DepositFee,
                ["currentTenant"] = tenant != null ? $"{tenant.FirstName} {tenant.LastName}".Trim() : "",
                ["monthlyStatus"] = monthlyStatus,
                ["awaitingTenant"] = h.IsAwaitingExistingTenant ? "Yes" : ""
            };
        }).ToList();

        var flatName = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Houses Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatName),
            Columns = new List<ReportColumn>
            {
                new() { Key = "houseNumber", Header = "House Number" },
                new() { Key = "flatName", Header = "Flat" },
                new() { Key = "houseType", Header = "House Type" },
                new() { Key = "occupancyStatus", Header = "Occupancy Status" },
                new() { Key = "rent", Header = "Rent" },
                new() { Key = "deposit", Header = "Deposit" },
                new() { Key = "currentTenant", Header = "Current Tenant" },
                new() { Key = "monthlyStatus", Header = monthlyStatusHeader },
                new() { Key = "awaitingTenant", Header = "Awaiting Tenant" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(HouseFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (!string.IsNullOrWhiteSpace(filters.OccupancyStatus)) parts.Add($"Occupancy Status: {filters.OccupancyStatus}");
        if (filters.IsAwaitingExistingTenant.HasValue)
            parts.Add($"Awaiting Tenant: {(filters.IsAwaitingExistingTenant.Value ? "Yes" : "No")}");
        if (filters.Month.HasValue && filters.Year.HasValue)
            parts.Add($"For {new DateTime(filters.Year.Value, filters.Month.Value, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture)}");

        return parts.Count == 0 ? "All houses" : string.Join(" | ", parts);
    }
}
