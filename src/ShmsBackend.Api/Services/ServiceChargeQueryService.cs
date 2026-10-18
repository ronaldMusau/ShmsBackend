using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using PaymentEntity = ShmsBackend.Data.Models.Entities.Portal.Payment;

namespace ShmsBackend.Api.Services;

public class ServiceChargeFilters
{
    public Guid? TenantId { get; set; }
    public Guid? HouseId { get; set; }
    public Guid? FlatId { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    /// <summary>Rent | Complaint | Vacate — null/empty = all sources.</summary>
    public string? Source { get; set; }
    public string? Search { get; set; }
}

/// <summary>One collected service charge, regardless of which flow (rent, complaint charge, vacate settlement) it came from.</summary>
public class ServiceChargeEntry
{
    public Guid Id { get; set; }
    public string Source { get; set; } = "Rent";           // Rent | Complaint | Vacate
    public string SourceLabel { get; set; } = "Rent payment";
    public Guid TenantId { get; set; }
    public string? TenantName { get; set; }
    public Guid HouseId { get; set; }
    public Guid? FlatId { get; set; }
    public string? HouseNumber { get; set; }
    public string? FlatName { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public string? Reference { get; set; }                  // rent: "Mon yyyy"; complaint: ticket; vacate: house
    public decimal? BaseAmount { get; set; }                // the amount the service charge was added to
    public decimal ServiceChargeAmount { get; set; }
    public string? MpesaReceiptNumber { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Single source of truth for filtered-service-charge queries, mirroring
/// PaymentController.GetAllServiceChargesCollected's exact query/filter shape — Payment rows with a
/// positive ServiceChargeAmount that actually collected an M-Pesa receipt.
/// </summary>
public class ServiceChargeQueryService
{
    private readonly ShmsDbContext _context;

    public ServiceChargeQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<PaymentEntity> BuildFilteredQuery(ServiceChargeFilters filters)
    {
        var query = _context.Payments
            .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
            .Where(p => p.ServiceChargeAmount > 0 && p.MpesaReceiptNumber != null)
            .AsQueryable();

        if (filters.TenantId.HasValue) query = query.Where(p => p.TenantId == filters.TenantId.Value);
        if (filters.HouseId.HasValue) query = query.Where(p => p.HouseId == filters.HouseId.Value);
        if (filters.FlatId.HasValue) query = query.Where(p => p.FlatId == filters.FlatId.Value);
        if (filters.Month.HasValue) query = query.Where(p => p.Month == filters.Month.Value);
        if (filters.Year.HasValue) query = query.Where(p => p.Year == filters.Year.Value);
        if (filters.FromDate.HasValue) query = query.Where(p => p.PaidAt >= filters.FromDate.Value);
        if (filters.ToDate.HasValue) query = query.Where(p => p.PaidAt < filters.ToDate.Value.Date.AddDays(1));
        if (filters.MinAmount.HasValue) query = query.Where(p => p.ServiceChargeAmount >= filters.MinAmount.Value);
        if (filters.MaxAmount.HasValue) query = query.Where(p => p.ServiceChargeAmount <= filters.MaxAmount.Value);

        return query;
    }

    public static bool IsValidSource(string? source) =>
        string.IsNullOrWhiteSpace(source) || source is "Rent" or "Complaint" or "Vacate";

    /// <summary>
    /// Unified ledger of every service charge actually collected: rent payments (Payment rows), complaint
    /// charges and vacate settlements (successful checkout attempts). Newest first.
    /// </summary>
    public async Task<List<ServiceChargeEntry>> GetEntriesAsync(ServiceChargeFilters f)
    {
        var src = string.IsNullOrWhiteSpace(f.Source) ? null : f.Source;
        var entries = new List<ServiceChargeEntry>();

        if (src == null || src == "Rent")
        {
            var rent = await BuildFilteredQuery(f).ToListAsync();
            entries.AddRange(rent.Select(p => new ServiceChargeEntry
            {
                Id = p.Id, Source = "Rent", SourceLabel = "Rent payment",
                TenantId = p.TenantId, HouseId = p.HouseId, FlatId = p.FlatId,
                HouseNumber = p.House?.HouseNumber, FlatName = p.House?.Flat?.FlatName,
                Month = p.Month, Year = p.Year,
                Reference = new DateTime(p.Year, Math.Clamp(p.Month, 1, 12), 1).ToString("MMM yyyy"),
                BaseAmount = p.RentAmount, ServiceChargeAmount = p.ServiceChargeAmount ?? 0m,
                MpesaReceiptNumber = p.MpesaReceiptNumber, PaidAt = p.PaidAt, CreatedAt = p.CreatedAt
            }));
        }

        if (src == null || src == "Complaint")
        {
            var rows = await (from a in _context.ComplaintCheckoutAttempts
                              join c in _context.Complaints on a.ComplaintId equals c.Id
                              where a.AttemptStatus == "Success" && a.ServiceChargeAmount > 0
                              select new { a, c.TenantId, c.HouseId, c.TicketNumber }).ToListAsync();
            entries.AddRange(rows.Select(r => new ServiceChargeEntry
            {
                Id = r.a.Id, Source = "Complaint", SourceLabel = "Complaint charge",
                TenantId = r.TenantId, HouseId = r.HouseId,
                Month = (r.a.ProcessedAt ?? r.a.CreatedAt).Month, Year = (r.a.ProcessedAt ?? r.a.CreatedAt).Year,
                Reference = r.TicketNumber, BaseAmount = r.a.Amount, ServiceChargeAmount = r.a.ServiceChargeAmount ?? 0m,
                MpesaReceiptNumber = r.a.MpesaReceiptNumber, PaidAt = r.a.ProcessedAt, CreatedAt = r.a.CreatedAt
            }));
        }

        if (src == null || src == "Vacate")
        {
            var rows = await (from a in _context.VacateCheckoutAttempts
                              join st in _context.VacateSettlements on a.VacateSettlementId equals st.Id
                              where a.AttemptStatus == "Success" && a.ServiceChargeAmount > 0
                              select new { a, st.TenantId, st.HouseId, st.Amount }).ToListAsync();
            entries.AddRange(rows.Select(r => new ServiceChargeEntry
            {
                Id = r.a.Id, Source = "Vacate", SourceLabel = "Vacate settlement",
                TenantId = r.TenantId, HouseId = r.HouseId,
                Month = (r.a.ProcessedAt ?? r.a.CreatedAt).Month, Year = (r.a.ProcessedAt ?? r.a.CreatedAt).Year,
                BaseAmount = r.Amount, ServiceChargeAmount = r.a.ServiceChargeAmount ?? 0m,
                MpesaReceiptNumber = r.a.MpesaReceiptNumber, PaidAt = r.a.ProcessedAt, CreatedAt = r.a.CreatedAt
            }));
        }

        // Fill house / flat / tenant names (rent rows already carry house + flat).
        var houseIds = entries.Where(e => e.HouseNumber == null).Select(e => e.HouseId).Distinct().ToList();
        if (houseIds.Count > 0)
        {
            var houses = await _context.Houses.IgnoreQueryFilters().Include(h => h.Flat)
                .Where(h => houseIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id);
            foreach (var e in entries.Where(e => e.HouseNumber == null))
                if (houses.TryGetValue(e.HouseId, out var h))
                {
                    e.HouseNumber = h.HouseNumber; e.FlatName = h.Flat?.FlatName; e.FlatId = h.FlatId;
                }
        }
        foreach (var e in entries.Where(e => e.Source == "Vacate"))
            e.Reference = $"Vacate — House {e.HouseNumber}";

        var tenantIds = entries.Select(e => e.TenantId).Distinct().ToList();
        var tenants = await _context.Tenants.IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id)).Select(t => new { t.Id, t.FirstName, t.LastName })
            .ToDictionaryAsync(t => t.Id);
        foreach (var e in entries)
            if (tenants.TryGetValue(e.TenantId, out var t)) e.TenantName = $"{t.FirstName} {t.LastName}".Trim();

        // Non-rent rows weren't covered by BuildFilteredQuery's filters — apply them here to all rows.
        IEnumerable<ServiceChargeEntry> q = entries;
        if (f.TenantId.HasValue) q = q.Where(e => e.TenantId == f.TenantId.Value);
        if (f.HouseId.HasValue) q = q.Where(e => e.HouseId == f.HouseId.Value);
        if (f.FlatId.HasValue) q = q.Where(e => e.FlatId == f.FlatId.Value);
        if (f.Month.HasValue) q = q.Where(e => e.Month == f.Month.Value);
        if (f.Year.HasValue) q = q.Where(e => e.Year == f.Year.Value);
        if (f.FromDate.HasValue) q = q.Where(e => e.PaidAt >= f.FromDate.Value);
        if (f.ToDate.HasValue) q = q.Where(e => e.PaidAt < f.ToDate.Value.Date.AddDays(1));
        if (f.MinAmount.HasValue) q = q.Where(e => e.ServiceChargeAmount >= f.MinAmount.Value);
        if (f.MaxAmount.HasValue) q = q.Where(e => e.ServiceChargeAmount <= f.MaxAmount.Value);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            bool Has(string? v) => v != null && v.Contains(s, StringComparison.OrdinalIgnoreCase);
            q = q.Where(e => Has(e.TenantName) || Has(e.HouseNumber) || Has(e.FlatName)
                             || Has(e.MpesaReceiptNumber) || Has(e.Reference) || Has(e.SourceLabel));
        }

        return q.OrderByDescending(e => e.PaidAt ?? e.CreatedAt).ToList();
    }
}
