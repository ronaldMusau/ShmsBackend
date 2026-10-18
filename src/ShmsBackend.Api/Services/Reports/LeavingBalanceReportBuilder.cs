using ShmsBackend.Api.Models.Reports;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>Report of leaving (SettlingVacate) tenants and what they still owe. Mirrors GET /api/vacate/leaving-balances.</summary>
public class LeavingBalanceReportBuilder
{
    private static readonly string[] MonthNames =
        { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

    private readonly LeavingBalanceService _service;
    private readonly ShmsDbContext _context;

    public LeavingBalanceReportBuilder(LeavingBalanceService service, ShmsDbContext context)
    {
        _service = service;
        _context = context;
    }

    public async Task<ReportData> BuildAsync(LeavingBalanceFilters filters)
    {
        var items = await _service.GetAsync(filters);

        var rows = items.Select(r => new Dictionary<string, object?>
        {
            ["tenantName"] = r.TenantName,
            ["phone"] = r.PhoneNumber ?? "-",
            ["flatUnit"] = $"{r.HouseNumber ?? "-"} - {r.FlatName ?? "-"}",
            ["movedOut"] = $"{MonthNames[Math.Clamp(r.VacateMonth, 1, 12) - 1]} {r.VacateYear}",
            ["monthsOwed"] = r.ArrearsRows.Count,
            ["unpaidRent"] = r.ArrearsTotal,
            ["settlementOwed"] = r.SettlementRequiresPayment ? r.SettlementAmount ?? 0 : 0m,
            ["status"] = r.Status
        }).ToList();

        var parts = new List<string>();
        if (filters.FlatId.HasValue)
            parts.Add($"Flat: {await ReportBuilderHelpers.ResolveFlatNameAsync(_context, filters.FlatId) ?? filters.FlatId.ToString()}");
        if (!string.IsNullOrWhiteSpace(filters.Status))
            parts.Add("Status: " + (filters.Status == "rent" ? "Owes rent" : filters.Status == "settlement" ? "Owes settlement" : "Refund pending"));
        if (!string.IsNullOrWhiteSpace(filters.Search)) parts.Add($"Search: {filters.Search}");

        return new ReportData
        {
            Title = "Leaving Tenants Report",
            GeneratedAt = DateTime.UtcNow,
            FilterSummary = parts.Count == 0 ? "All leaving tenants" : string.Join(" | ", parts),
            Columns = new List<ReportColumn>
            {
                new() { Key = "tenantName", Header = "Tenant Name" },
                new() { Key = "phone", Header = "Phone" },
                new() { Key = "flatUnit", Header = "Flat/Unit" },
                new() { Key = "movedOut", Header = "Moved Out" },
                new() { Key = "monthsOwed", Header = "Months Owed", Numeric = true },
                new() { Key = "unpaidRent", Header = "Unpaid Rent", SumTotal = true },
                new() { Key = "settlementOwed", Header = "Settlement Owed", SumTotal = true },
                new() { Key = "status", Header = "Status" }
            },
            Rows = rows
        };
    }
}
