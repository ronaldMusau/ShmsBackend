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

    /// <summary>
    /// Rent-side ledger: one entry per M-Pesa push that actually collected a charge (a successful checkout
    /// attempt on its triggering Payment row) — NOT one per row. A multi-month advance payment funds several
    /// rows but carries a single charge, and a row paid over several pushes carries one per push. Older rows
    /// that pre-date checkout-attempt/application tracking are counted once each.
    /// </summary>
    private async Task<List<ServiceChargeEntry>> GetRentEntriesAsync(ServiceChargeFilters f)
    {
        var result = new List<ServiceChargeEntry>();

        // Filters are applied in SQL so a filtered view (year, month, flat, amount, dates) never loads the whole ledger.
        var month = f.Month; var year = f.Year; var minA = f.MinAmount; var maxA = f.MaxAmount;
        var fromD = f.FromDate; var toExcl = f.ToDate.HasValue ? f.ToDate.Value.Date.AddDays(1) : (DateTime?)null;

        var charged = await (from a in _context.PaymentCheckoutAttempts
                             where a.AttemptStatus == "Success"
                             join p in _context.Payments on a.PaymentId equals p.Id
                             where (a.ServiceChargeAmount ?? p.ServiceChargeAmount) > 0
                                   && (!f.TenantId.HasValue || p.TenantId == f.TenantId.Value)
                                   && (!f.HouseId.HasValue || p.HouseId == f.HouseId.Value)
                                   && (!f.FlatId.HasValue || p.FlatId == f.FlatId.Value)
                                   && (month == null || p.Month == month)
                                   && (year == null || p.Year == year)
                                   && (minA == null || (a.ServiceChargeAmount ?? p.ServiceChargeAmount) >= minA)
                                   && (maxA == null || (a.ServiceChargeAmount ?? p.ServiceChargeAmount) <= maxA)
                                   && (fromD == null || (a.ProcessedAt ?? p.PaidAt) >= fromD)
                                   && (toExcl == null || (a.ProcessedAt ?? p.PaidAt) < toExcl)
                             select new { a.CheckoutRequestId, a.ProcessedAt, Charge = a.ServiceChargeAmount, Payment = p }).ToListAsync();

        var cids = charged.Select(x => x.CheckoutRequestId).Distinct().ToList();
        var apps = await _context.PaymentApplications
            .Where(x => x.CheckoutRequestId != null && cids.Contains(x.CheckoutRequestId))
            .ToListAsync();
        var appsByCid = apps.GroupBy(x => x.CheckoutRequestId!).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var x in charged)
        {
            var p = x.Payment;
            appsByCid.TryGetValue(x.CheckoutRequestId, out var list);
            var funded = list?.Sum(a => a.AmountApplied) ?? 0m;
            result.Add(new ServiceChargeEntry
            {
                Id = Guid.NewGuid(), Source = "Rent", SourceLabel = "Rent payment",
                TenantId = p.TenantId, HouseId = p.HouseId, FlatId = p.FlatId,
                Month = p.Month, Year = p.Year,
                Reference = new DateTime(p.Year, Math.Clamp(p.Month, 1, 12), 1).ToString("MMM yyyy"),
                BaseAmount = funded > 0 ? funded : p.RentAmount,
                ServiceChargeAmount = x.Charge ?? p.ServiceChargeAmount ?? 0m,
                MpesaReceiptNumber = list?.FirstOrDefault()?.MpesaReceiptNumber ?? p.MpesaReceiptNumber,
                PaidAt = x.ProcessedAt ?? p.PaidAt, CreatedAt = p.CreatedAt
            });
        }

        var legacy = await _context.Payments
            .Where(p => p.ServiceChargeAmount > 0 && p.MpesaReceiptNumber != null
                        && !_context.PaymentCheckoutAttempts.Any(a => a.PaymentId == p.Id && a.AttemptStatus == "Success")
                        && !_context.PaymentApplications.Any(a => a.PaymentId == p.Id && a.CheckoutRequestId != null)
                        && (!f.TenantId.HasValue || p.TenantId == f.TenantId.Value)
                        && (!f.HouseId.HasValue || p.HouseId == f.HouseId.Value)
                        && (!f.FlatId.HasValue || p.FlatId == f.FlatId.Value)
                        && (month == null || p.Month == month)
                        && (year == null || p.Year == year)
                        && (minA == null || p.ServiceChargeAmount >= minA)
                        && (maxA == null || p.ServiceChargeAmount <= maxA)
                        && (fromD == null || p.PaidAt >= fromD)
                        && (toExcl == null || p.PaidAt < toExcl))
            .ToListAsync();
        foreach (var p in legacy)
            result.Add(new ServiceChargeEntry
            {
                Id = p.Id, Source = "Rent", SourceLabel = "Rent payment",
                TenantId = p.TenantId, HouseId = p.HouseId, FlatId = p.FlatId,
                Month = p.Month, Year = p.Year,
                Reference = new DateTime(p.Year, Math.Clamp(p.Month, 1, 12), 1).ToString("MMM yyyy"),
                BaseAmount = p.RentAmount, ServiceChargeAmount = p.ServiceChargeAmount ?? 0m,
                MpesaReceiptNumber = p.MpesaReceiptNumber, PaidAt = p.PaidAt, CreatedAt = p.CreatedAt
            });

        return result;
    }

    /// <summary>Distinct years that have a collected service charge, computed in SQL (no ledger load).</summary>
    public async Task<List<int>> GetYearsAsync()
    {
        var rent = await _context.Payments
            .Where(p => p.ServiceChargeAmount > 0 && (p.MpesaReceiptNumber != null
                        || _context.PaymentCheckoutAttempts.Any(a => a.PaymentId == p.Id && a.AttemptStatus == "Success")))
            .Select(p => p.Year).Distinct().ToListAsync();
        var complaint = await _context.ComplaintCheckoutAttempts
            .Where(a => a.AttemptStatus == "Success" && a.ServiceChargeAmount > 0)
            .Select(a => (a.ProcessedAt ?? a.CreatedAt).Year).Distinct().ToListAsync();
        var vacate = await _context.VacateCheckoutAttempts
            .Where(a => a.AttemptStatus == "Success" && a.ServiceChargeAmount > 0)
            .Select(a => (a.ProcessedAt ?? a.CreatedAt).Year).Distinct().ToListAsync();
        return rent.Concat(complaint).Concat(vacate).Distinct().OrderByDescending(y => y).ToList();
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
            entries.AddRange(await GetRentEntriesAsync(f));

        if (src == null || src == "Complaint")
        {
            var rows = await (from a in _context.ComplaintCheckoutAttempts
                              join c in _context.Complaints on a.ComplaintId equals c.Id
                              where a.AttemptStatus == "Success" && a.ServiceChargeAmount > 0
                                    && (f.TenantId == null || c.TenantId == f.TenantId)
                                    && (f.HouseId == null || c.HouseId == f.HouseId)
                                    && (f.Month == null || (a.ProcessedAt ?? a.CreatedAt).Month == f.Month)
                                    && (f.Year == null || (a.ProcessedAt ?? a.CreatedAt).Year == f.Year)
                                    && (f.MinAmount == null || a.ServiceChargeAmount >= f.MinAmount)
                                    && (f.MaxAmount == null || a.ServiceChargeAmount <= f.MaxAmount)
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
                                    && (f.TenantId == null || st.TenantId == f.TenantId)
                                    && (f.HouseId == null || st.HouseId == f.HouseId)
                                    && (f.Month == null || (a.ProcessedAt ?? a.CreatedAt).Month == f.Month)
                                    && (f.Year == null || (a.ProcessedAt ?? a.CreatedAt).Year == f.Year)
                                    && (f.MinAmount == null || a.ServiceChargeAmount >= f.MinAmount)
                                    && (f.MaxAmount == null || a.ServiceChargeAmount <= f.MaxAmount)
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
