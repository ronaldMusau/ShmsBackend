using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Api.Services.Payment;

public class LeavingBalanceFilters
{
    public string? Search { get; set; }
    public Guid? FlatId { get; set; }
    /// <summary>"rent" (unpaid rent), "settlement" (settlement owed by tenant), "refund" (refund pending), or empty for all.</summary>
    public string? Status { get; set; }
    /// <summary>Set by the controller for agents — never bound from the query string.</summary>
    [BindNever] public Guid? AgentId { get; set; }
}

public class LeavingBalanceMonth
{
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Balance { get; set; }
    public string Status { get; set; } = "";
}

public class LeavingBalanceRow
{
    public Guid VacateRequestId { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = "";
    public string? PhoneNumber { get; set; }
    public Guid HouseId { get; set; }
    public string? HouseNumber { get; set; }
    public Guid? FlatId { get; set; }
    public string? FlatName { get; set; }
    public bool HouseOccupied { get; set; }
    public int VacateMonth { get; set; }
    public int VacateYear { get; set; }
    public decimal ArrearsTotal { get; set; }
    public List<LeavingBalanceMonth> ArrearsRows { get; set; } = new();
    public Guid? SettlementId { get; set; }
    public string? SettlementDirection { get; set; }
    public decimal? SettlementAmount { get; set; }
    public bool SettlementRequiresPayment { get; set; }
    public bool RefundPending { get; set; }
    public string Status => ArrearsTotal > 0 ? "Owes rent"
        : SettlementRequiresPayment ? "Owes settlement"
        : RefundPending ? "Refund pending" : "Settling";
}

public class LeavingBalanceTotals
{
    public int TenantCount { get; set; }
    public decimal RentOwed { get; set; }
    public int RentOwedCount { get; set; }
    public decimal SettlementOwed { get; set; }
    public int SettlementOwedCount { get; set; }
    public decimal RefundsPending { get; set; }
    public int RefundsPendingCount { get; set; }
}

/// <summary>
/// Tenants whose move-out month has passed (SettlingVacate): login is closed and the house is released, so
/// management/agents settle what they still owe on their behalf. Every figure is keyed to the LEAVING tenant's
/// own payment rows — nothing here reads or touches whoever now occupies the house.
/// </summary>
public class LeavingBalanceService
{
    private readonly ShmsDbContext _context;
    public LeavingBalanceService(ShmsDbContext context) { _context = context; }

    public async Task<List<LeavingBalanceRow>> GetAsync(LeavingBalanceFilters f)
    {
        var todayUtc = DateTime.UtcNow.Date;

        var settlingQuery = _context.Tenants.Where(t => t.TenantStatus == TenantStatus.SettlingVacate && !t.IsDeleted);

        if (f.AgentId.HasValue)
        {
            var agentId = f.AgentId.Value;
            var flatIds = _context.AgentFlats.Where(af => af.AgentId == agentId).Select(af => af.FlatId);
            settlingQuery = settlingQuery.Where(t => t.House != null && flatIds.Contains(t.House.FlatId));
        }

        var tenants = await settlingQuery
            .Select(t => new { t.Id, t.FirstName, t.LastName, t.PhoneNumber, t.TenancyCycle })
            .ToListAsync();
        var tenantIds = tenants.Select(t => t.Id).ToList();

        var requests = await _context.VacateRequests
            .Where(v => tenantIds.Contains(v.TenantId) && !v.IsDeleted && v.Status == "Approved" && v.ClosedAt == null)
            .ToListAsync();

        var houseIds = requests.Select(r => r.HouseId).Distinct().ToList();
        var houses = await _context.Houses.Where(h => houseIds.Contains(h.Id))
            .Select(h => new { h.Id, h.HouseNumber, h.FlatId, FlatName = h.Flat != null ? h.Flat.FlatName : "", h.OccupancyStatus })
            .ToDictionaryAsync(h => h.Id);

        var requestIds = requests.Select(r => r.Id).ToList();
        var settlements = await _context.VacateSettlements
            .Where(s => requestIds.Contains(s.VacateRequestId) && !s.IsVoided)
            .ToListAsync();

        var owedRows = await _context.Payments
            .Where(p => tenantIds.Contains(p.TenantId) && !p.IsDeleted && p.DueDate < todayUtc
                && (p.PaymentStatus == PaymentTransactionStatus.Overdue
                    || p.PaymentStatus == PaymentTransactionStatus.Pending
                    || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid))
            .Select(p => new { p.TenantId, p.TenancyCycle, p.Year, p.Month, p.Balance, p.PaymentStatus })
            .ToListAsync();

        var result = new List<LeavingBalanceRow>();
        foreach (var r in requests)
        {
            var t = tenants.First(x => x.Id == r.TenantId);
            houses.TryGetValue(r.HouseId, out var house);

            var rows = owedRows
                .Where(p => p.TenantId == r.TenantId && p.TenancyCycle == t.TenancyCycle
                    && (p.Year < r.VacateYear || (p.Year == r.VacateYear && p.Month <= r.VacateMonth)))
                .OrderBy(p => p.Year).ThenBy(p => p.Month)
                .ToList();
            var settlement = settlements.FirstOrDefault(s => s.VacateRequestId == r.Id);

            var row = new LeavingBalanceRow
            {
                VacateRequestId = r.Id,
                TenantId = t.Id,
                TenantName = $"{t.FirstName} {t.LastName}".Trim(),
                PhoneNumber = t.PhoneNumber,
                HouseId = r.HouseId,
                HouseNumber = house?.HouseNumber,
                FlatId = house?.FlatId,
                FlatName = house?.FlatName,
                HouseOccupied = house != null && house.OccupancyStatus == OccupancyStatus.Occupied,
                VacateMonth = r.VacateMonth,
                VacateYear = r.VacateYear,
                ArrearsTotal = rows.Sum(x => x.Balance),
                ArrearsRows = rows.Select(x => new LeavingBalanceMonth { Year = x.Year, Month = x.Month, Balance = x.Balance, Status = x.PaymentStatus.ToString() }).ToList(),
                SettlementId = settlement?.Id,
                SettlementDirection = settlement?.Direction,
                SettlementAmount = settlement?.Amount,
                SettlementRequiresPayment = settlement != null && settlement.Direction == "TenantOwes" && settlement.PaidAt == null,
                RefundPending = settlement != null && settlement.Direction == "ManagementOwes" && settlement.PaidAt == null
            };

            if (f.FlatId.HasValue && row.FlatId != f.FlatId.Value) continue;

            if (!string.IsNullOrWhiteSpace(f.Search)
                && !row.TenantName.Contains(f.Search, StringComparison.OrdinalIgnoreCase)
                && !(row.HouseNumber ?? "").Contains(f.Search, StringComparison.OrdinalIgnoreCase)
                && !(row.FlatName ?? "").Contains(f.Search, StringComparison.OrdinalIgnoreCase)
                && !(row.PhoneNumber ?? "").Contains(f.Search, StringComparison.OrdinalIgnoreCase))
                continue;

            if (f.Status == "rent" && row.ArrearsTotal <= 0) continue;
            if (f.Status == "settlement" && !row.SettlementRequiresPayment) continue;
            if (f.Status == "refund" && !row.RefundPending) continue;

            result.Add(row);
        }

        return result.OrderByDescending(x => x.ArrearsTotal).ThenBy(x => x.TenantName).ToList();
    }

    public static LeavingBalanceTotals Totals(IReadOnlyCollection<LeavingBalanceRow> rows) => new()
    {
        TenantCount = rows.Count,
        RentOwed = rows.Sum(r => r.ArrearsTotal),
        RentOwedCount = rows.Count(r => r.ArrearsTotal > 0),
        SettlementOwed = rows.Where(r => r.SettlementRequiresPayment).Sum(r => r.SettlementAmount ?? 0),
        SettlementOwedCount = rows.Count(r => r.SettlementRequiresPayment),
        RefundsPending = rows.Where(r => r.RefundPending).Sum(r => r.SettlementAmount ?? 0),
        RefundsPendingCount = rows.Count(r => r.RefundPending)
    };
}
