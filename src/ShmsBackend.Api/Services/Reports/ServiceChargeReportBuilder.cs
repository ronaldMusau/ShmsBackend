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
        var entries = await _serviceChargeQueryService.GetEntriesAsync(filters);

        var rows = entries.Select(e => new Dictionary<string, object?>
        {
            ["source"] = e.SourceLabel,
            ["reference"] = e.Reference ?? "-",
            ["tenantName"] = e.TenantName ?? "-",
            ["flatUnit"] = $"{e.HouseNumber ?? "-"} - {e.FlatName ?? "-"}",
            ["serviceChargeAmount"] = e.ServiceChargeAmount,
            ["mpesaReceipt"] = e.MpesaReceiptNumber,
            ["paidDate"] = e.PaidAt
        }).ToList();

        var flatName = await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId);

        return new ReportData
        {
            Title = "Service Charge Collected Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatName),
            Columns = new List<ReportColumn>
            {
                new() { Key = "source", Header = "Collected From" },
                new() { Key = "reference", Header = "Reference" },
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "serviceChargeAmount", Header = "Service Charge Amount", SumTotal = true },
                new() { Key = "mpesaReceipt", Header = "Mpesa Receipt" },
                new() { Key = "paidDate", Header = "Paid Date" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(ServiceChargeFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.Source)) parts.Add($"Source: {filters.Source}");
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
