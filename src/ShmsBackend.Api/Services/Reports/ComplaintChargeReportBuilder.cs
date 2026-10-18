using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Reports;

public class ComplaintChargeFilters
{
    public string? State { get; set; }   // Outstanding | Paid | AwaitingTenant | Declined
    public string? Search { get; set; }
    public Guid? FlatId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Builds the management Complaint Charges report. Uses the same filter logic as
/// ComplaintController.GetChargePayments (shared via ApplyFilters). The service charge is never shown.
/// </summary>
public class ComplaintChargeReportBuilder
{
    private readonly ShmsDbContext _context;
    public ComplaintChargeReportBuilder(ShmsDbContext context) { _context = context; }

    public static IQueryable<Complaint> ApplyFilters(ShmsDbContext context, ComplaintChargeFilters f)
    {
        var q = context.Complaints.Where(c => c.IsBillable == true && c.BillableTarget == "Tenant" && c.Status != "Rejected"
                                              && (c.CurrentApprovalStepOrder == null));
        if (f.FlatId.HasValue) q = q.Where(c => c.FlatId == f.FlatId.Value);
        if (f.FromDate.HasValue) q = q.Where(c => c.CreatedAt >= f.FromDate.Value);
        if (f.ToDate.HasValue) q = q.Where(c => c.CreatedAt <= f.ToDate.Value.AddDays(1));
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(c => c.TicketNumber.Contains(s)
                || context.Tenants.Any(t => t.Id == c.TenantId && (t.FirstName.Contains(s) || t.LastName.Contains(s)))
                || context.Houses.Any(h => h.Id == c.HouseId && h.HouseNumber.Contains(s)));
        }
        return f.State switch
        {
            "Paid" => q.Where(c => c.TenantChargePaidAt != null),
            "Outstanding" => q.Where(c => c.TenantChargeDecision == "Accepted" && c.TenantChargePaidAt == null && c.BillableAmount > 0),
            "AwaitingTenant" => q.Where(c => c.TenantChargeDecision == null && c.Status == "Approved"),
            "Declined" => q.Where(c => c.TenantChargeDecision == "Declined"),
            _ => q
        };
    }

    private static string StateLabel(Complaint c)
    {
        if (c.TenantChargePaidAt != null) return "Paid";
        if (c.Status == "Closed" && c.TenantChargeDecision == null) return "Closed - not charged";
        if (c.TenantChargeDecision == "Declined") return "Declined";
        if (c.TenantChargeDecision == "Accepted") return (c.BillableAmount ?? 0) > 0 ? "Awaiting payment" : "Accepted (no charge)";
        return "Awaiting tenant";
    }

    public async Task<ReportData> BuildAsync(ComplaintChargeFilters filters)
    {
        var complaints = await ApplyFilters(_context, filters).OrderByDescending(c => c.CreatedAt).ToListAsync();

        var tenantIds = complaints.Select(c => c.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants.Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => $"{t.FirstName} {t.LastName}");
        var houseIds = complaints.Select(c => c.HouseId).Distinct().ToList();
        var houses = await _context.Houses.Where(h => houseIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, h => h.HouseNumber);
        var flatIds = complaints.Select(c => c.FlatId).Distinct().ToList();
        var flats = await _context.Flats.Where(f => flatIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.FlatName);

        var rows = complaints.Select(c => new Dictionary<string, object?>
        {
            ["ticketNumber"] = c.TicketNumber,
            ["tenantName"] = tenants.GetValueOrDefault(c.TenantId, "-"),
            ["flatUnit"] = $"{houses.GetValueOrDefault(c.HouseId, "-")} - {flats.GetValueOrDefault(c.FlatId, "-")}",
            ["state"] = StateLabel(c),
            ["chargeAmount"] = c.BillableAmount ?? 0m,
            ["paidAmount"] = c.TenantChargePaidAt != null ? (c.BillableAmount ?? 0m) : 0m,
            ["raisedDate"] = c.CreatedAt,
            ["paidDate"] = c.TenantChargePaidAt,
            ["receipt"] = c.TenantChargeReceipt
        }).ToList();

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(filters.State)) parts.Add($"State: {ReportBuilderHelpers.Humanize(filters.State)}");
        if (filters.FlatId.HasValue) parts.Add($"Flat: {await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId) ?? filters.FlatId.ToString()}");
        if (!string.IsNullOrWhiteSpace(filters.Search)) parts.Add($"Search: {filters.Search.Trim()}");
        var range = ReportBuilderHelpers.FormatDateRange(filters.FromDate, filters.ToDate);
        if (range != null) parts.Add(range);

        return new ReportData
        {
            Title = "Complaint Charges Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = parts.Count == 0 ? "All complaint charges" : string.Join(" | ", parts),
            Columns = new List<ReportColumn>
            {
                new() { Key = "ticketNumber", Header = "Ticket Reference" },
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "state", Header = "State" },
                new() { Key = "chargeAmount", Header = "Charge Amount", SumTotal = true },
                new() { Key = "paidAmount", Header = "Amount Paid", SumTotal = true },
                new() { Key = "raisedDate", Header = "Raised Date" },
                new() { Key = "paidDate", Header = "Paid Date" },
                new() { Key = "receipt", Header = "Mpesa Receipt" }
            },
            Rows = rows
        };
    }
}
