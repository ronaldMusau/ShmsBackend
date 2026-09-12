using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class RefundFilters
{
    public Guid? TenantId { get; set; }
    public Guid? FlatId { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool? IsPaid { get; set; }   // true = PaidAt != null, false = still refundable
}

/// <summary>
/// Single source of truth for filtered-refund queries, mirroring VacateController.GetAllRefunds'
/// base query (Direction == "ManagementOwes" && !IsVoided) — extended with a FlatId filter (direct
/// column on VacateSettlement, confirmed) since the original endpoint never had one.
/// </summary>
public class RefundQueryService
{
    private readonly ShmsDbContext _context;

    public RefundQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<VacateSettlement> BuildFilteredQuery(RefundFilters filters)
    {
        var query = _context.VacateSettlements
            .Where(s => s.Direction == "ManagementOwes" && !s.IsVoided)
            .AsQueryable();

        if (filters.TenantId.HasValue) query = query.Where(s => s.TenantId == filters.TenantId.Value);
        if (filters.FlatId.HasValue) query = query.Where(s => s.FlatId == filters.FlatId.Value);
        if (filters.Month.HasValue) query = query.Where(s => s.CreatedAt.Month == filters.Month.Value);
        if (filters.Year.HasValue) query = query.Where(s => s.CreatedAt.Year == filters.Year.Value);
        if (filters.FromDate.HasValue) query = query.Where(s => s.CreatedAt >= filters.FromDate.Value);
        if (filters.ToDate.HasValue) query = query.Where(s => s.CreatedAt.Date <= filters.ToDate.Value.Date);

        if (filters.IsPaid.HasValue)
            query = filters.IsPaid.Value
                ? query.Where(s => s.PaidAt != null)
                : query.Where(s => s.PaidAt == null);

        return query;
    }
}
