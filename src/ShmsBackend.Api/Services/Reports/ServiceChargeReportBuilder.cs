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
/// Builds service-charge-collected ReportData off ServiceChargeQueryService/ServiceChargeFilters,
/// mirroring PaymentController.GetAllServiceChargesCollected's exact data.
/// </summary>
public class ServiceChargeReportBuilder
{
    private readonly ServiceChargeQueryService _serviceChargeQueryService;
    private readonly ShmsDbContext _context;

    public ServiceChargeReportBuilder(ServiceChargeQueryService serviceChargeQueryService, ShmsDbContext context)
    {
        _serviceChargeQueryService = serviceChargeQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(ServiceChargeFilters filters)
    {
        var payments = await _serviceChargeQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var tenantIds = payments.Select(p => p.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants
            .IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.FirstName, t.LastName })
            .ToDictionaryAsync(t => t.Id);

        var rows = payments.Select(p =>
        {
            tenants.TryGetValue(p.TenantId, out var tenant);
            var houseNumber = p.House?.HouseNumber;
            var flatName = p.House?.Flat?.FlatName;

            return new Dictionary<string, object?>
            {
                ["tenantName"] = tenant != null ? $"{tenant.FirstName} {tenant.LastName}" : "-",
                ["flatUnit"] = $"{houseNumber ?? "-"} - {flatName ?? "-"}",
                ["serviceChargeAmount"] = p.ServiceChargeAmount,
                ["mpesaReceipt"] = p.MpesaReceiptNumber,
                ["paidDate"] = p.PaidAt
            };
        }).ToList();

        var flatName = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Service Charge Collected Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatName),
            Columns = new List<ReportColumn>
            {
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "serviceChargeAmount", Header = "Service Charge Amount" },
                new() { Key = "mpesaReceipt", Header = "Mpesa Receipt" },
                new() { Key = "paidDate", Header = "Paid Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(ServiceChargeFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (filters.TenantId.HasValue) parts.Add($"Tenant: {filters.TenantId}");
        if (filters.Month.HasValue) parts.Add($"Month: {filters.Month}");
        if (filters.Year.HasValue) parts.Add($"Year: {filters.Year}");

        var dateRange = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        if (filters.MinAmount.HasValue) parts.Add($"Amount from {filters.MinAmount:N0}");
        if (filters.MaxAmount.HasValue) parts.Add($"Amount up to {filters.MaxAmount:N0}");

        return parts.Count == 0 ? "All service charges" : string.Join(" | ", parts);
    }
}
