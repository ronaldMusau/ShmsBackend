using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class ForfeitedAdvanceFilters
{
    public Guid? TenantId { get; set; }
    public Guid? HouseId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
}

/// <summary>
/// Single source of truth for filtered-forfeited-advance queries, mirroring
/// VacateController.GetAllForfeitedAdvances' base query (!IsVoided) and filter chain
/// (VacateForfeitedAdvance has scalar Tenant/House/Flat/LandlordId only, no nav properties).
/// </summary>
public class ForfeitedAdvanceQueryService
{
    private readonly ShmsDbContext _context;

    public ForfeitedAdvanceQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<VacateForfeitedAdvance> BuildFilteredQuery(ForfeitedAdvanceFilters filters)
    {
        var query = _context.VacateForfeitedAdvances.Where(f => !f.IsVoided).AsQueryable();

        if (filters.TenantId.HasValue) query = query.Where(f => f.TenantId == filters.TenantId.Value);
        if (filters.HouseId.HasValue) query = query.Where(f => f.HouseId == filters.HouseId.Value);
        if (filters.FromDate.HasValue) query = query.Where(f => f.CreatedAt >= filters.FromDate.Value);
        if (filters.ToDate.HasValue) query = query.Where(f => f.CreatedAt < filters.ToDate.Value.Date.AddDays(1));
        if (filters.MinAmount.HasValue) query = query.Where(f => f.AmountForfeitedUnused >= filters.MinAmount.Value);
        if (filters.MaxAmount.HasValue) query = query.Where(f => f.AmountForfeitedUnused <= filters.MaxAmount.Value);

        return query;
    }
}
