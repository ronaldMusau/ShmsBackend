using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class LandlordFilters
{
    public string? Search { get; set; }       // matches FirstName/LastName/Email/AgencyName, Contains, case-insensitive
    public string? County { get; set; }
    public bool? IsActive { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }      // both against CreatedAt
}

/// <summary>
/// Single source of truth for filtered-landlord queries. LandlordController.GetAll has no filter
/// logic to mirror at all (it's a bare _dbSet.ToListAsync() via the generic repository) — this is a
/// fresh, DB-level IQueryable built for the reports feature specifically.
/// </summary>
public class LandlordQueryService
{
    private readonly ShmsDbContext _context;

    public LandlordQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<Landlord> BuildFilteredQuery(LandlordFilters filters)
    {
        // No explicit !IsDeleted filter here — Landlord (via PortalUser) already carries a global
        // query filter, so adding one here would just be a redundant duplicate of it.
        var query = _context.Landlords.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query = query.Where(l =>
                EF.Functions.Like(l.FirstName, $"%{search}%") ||
                EF.Functions.Like(l.LastName, $"%{search}%") ||
                EF.Functions.Like(l.Email, $"%{search}%") ||
                (l.AgencyName != null && EF.Functions.Like(l.AgencyName, $"%{search}%")));
        }

        if (!string.IsNullOrWhiteSpace(filters.County))
            query = query.Where(l => l.County == filters.County);

        if (filters.IsActive.HasValue)
            query = query.Where(l => l.IsActive == filters.IsActive.Value);

        if (filters.FromDate.HasValue)
            query = query.Where(l => l.CreatedAt >= filters.FromDate.Value);

        if (filters.ToDate.HasValue)
            query = query.Where(l => l.CreatedAt <= filters.ToDate.Value.AddDays(1));

        return query;
    }
}
