using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class DeductionFilters
{
    public Guid? LandlordId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? HouseId { get; set; }
    public Guid? FlatId { get; set; }
    public int? Year { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Single source of truth for filtered-deduction queries, mirroring DeductionController.GetAll's
/// filter chain (Deduction has scalar Landlord/Tenant/House/FlatId only, no nav properties).
/// </summary>
public class DeductionQueryService
{
    private readonly ShmsDbContext _context;

    public DeductionQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<Deduction> BuildFilteredQuery(DeductionFilters filters)
    {
        var query = _context.Deductions.AsQueryable();

        if (filters.LandlordId.HasValue) query = query.Where(d => d.LandlordId == filters.LandlordId.Value);
        if (filters.TenantId.HasValue) query = query.Where(d => d.TenantId == filters.TenantId.Value);
        if (filters.HouseId.HasValue) query = query.Where(d => d.HouseId == filters.HouseId.Value);
        if (filters.FlatId.HasValue) query = query.Where(d => d.FlatId == filters.FlatId.Value);
        if (filters.Year.HasValue) query = query.Where(d => d.DeductionYear == filters.Year.Value);
        if (filters.FromDate.HasValue) query = query.Where(d => d.CreatedAt >= filters.FromDate.Value);
        if (filters.ToDate.HasValue) query = query.Where(d => d.CreatedAt.Date <= filters.ToDate.Value.Date);

        return query;
    }
}
