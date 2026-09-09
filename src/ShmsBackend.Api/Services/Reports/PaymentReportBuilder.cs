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
/// Builds payment-listing ReportData off the same PaymentQueryService/PaymentFilters shared by
/// landlord- and admin-scoped payment reports, mirroring TenantReportBuilder's structure.
/// </summary>
public class PaymentReportBuilder
{
    private readonly PaymentQueryService _paymentQueryService;
    private readonly ShmsDbContext _context;

    public PaymentReportBuilder(PaymentQueryService paymentQueryService, ShmsDbContext context)
    {
        _paymentQueryService = paymentQueryService;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(PaymentFilters filters)
    {
        var payments = await _paymentQueryService.BuildFilteredQuery(filters)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        var rows = payments.Select(p => new Dictionary<string, object?>
        {
            ["tenantName"] = p.Tenant != null ? $"{p.Tenant.FirstName} {p.Tenant.LastName}".Trim() : null,
            ["flatUnit"] = p.House != null
                ? (p.House.Flat != null ? $"{p.House.HouseNumber} - {p.House.Flat.FlatName}" : $"{p.House.HouseNumber} - (Flat Deleted)")
                : null,
            ["amount"] = p.Amount,
            ["amountPaid"] = p.AmountPaid,
            ["balance"] = p.Balance,
            ["method"] = p.PaymentMethod?.ToString(),
            ["mpesaReceipt"] = p.MpesaReceiptNumber,
            ["status"] = p.PaymentStatus.ToString(),
            ["type"] = p.PaymentType.ToString(),
            ["dueDate"] = p.DueDate,
            ["paidAt"] = p.PaidAt
        }).ToList();

        string? flatName = null;
        if (filters.FlatId.HasValue)
        {
            flatName = await _context.Flats
                .IgnoreQueryFilters()
                .Where(f => f.Id == filters.FlatId.Value)
                .Select(f => f.FlatName)
                .FirstOrDefaultAsync();
        }

        return new ReportData
        {
            Title = "Payments Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = BuildFilterSummary(filters, flatName),
            Columns = new List<ReportColumn>
            {
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "amount", Header = "Amount" },
                new() { Key = "amountPaid", Header = "Amount Paid" },
                new() { Key = "balance", Header = "Balance" },
                new() { Key = "method", Header = "Method" },
                new() { Key = "mpesaReceipt", Header = "Mpesa Receipt" },
                new() { Key = "status", Header = "Status" },
                new() { Key = "type", Header = "Type" },
                new() { Key = "dueDate", Header = "Due Date" },
                new() { Key = "paidAt", Header = "Paid At" }
            },
            Rows = rows
        };
    }

    private static string BuildFilterSummary(PaymentFilters filters, string? flatName)
    {
        var parts = new List<string>();
        if (filters.FlatId.HasValue) parts.Add($"Flat: {flatName ?? filters.FlatId.ToString()}");
        if (filters.HouseId.HasValue) parts.Add($"House: {filters.HouseId}");
        if (filters.TenantId.HasValue) parts.Add($"Tenant: {filters.TenantId}");
        if (!string.IsNullOrWhiteSpace(filters.Status)) parts.Add($"Status: {filters.Status}");
        if (!string.IsNullOrWhiteSpace(filters.PaymentType)) parts.Add($"Type: {filters.PaymentType}");
        if (!string.IsNullOrWhiteSpace(filters.PaymentMethod)) parts.Add($"Method: {filters.PaymentMethod}");
        if (filters.Month.HasValue) parts.Add($"Month: {filters.Month}");
        if (filters.Year.HasValue) parts.Add($"Year: {filters.Year}");

        var dateRange = TenantReportBuilder.FormatDateRange(filters.FromDate, filters.ToDate);
        if (dateRange != null) parts.Add(dateRange);

        var amountRange = FormatAmountRange(filters.MinAmount, filters.MaxAmount);
        if (amountRange != null) parts.Add(amountRange);

        if (filters.IsInitialPayment.HasValue) parts.Add(filters.IsInitialPayment.Value ? "Stage: Initial" : "Stage: Monthly");

        return parts.Count == 0 ? "All payments" : string.Join(" | ", parts);
    }

    private static string? FormatAmountRange(decimal? min, decimal? max)
    {
        if (min.HasValue && max.HasValue) return $"Amount {min:N0} to {max:N0}";
        if (min.HasValue) return $"Amount from {min:N0}";
        if (max.HasValue) return $"Amount up to {max:N0}";
        return null;
    }
}
