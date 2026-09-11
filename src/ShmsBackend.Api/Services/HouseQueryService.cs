using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class HouseFilters
{
    public Guid? FlatId { get; set; }
    public string? OccupancyStatus { get; set; }
    public Guid? LandlordId { get; set; }   // set server-side only, never bound from a client filter param
}

/// <summary>
/// Single source of truth for filtered-house queries, mirroring TenantQueryService/PaymentQueryService
/// so the reports feature stays in lockstep with however houses are queried elsewhere.
/// </summary>
public class HouseQueryService
{
    private readonly ShmsDbContext _context;

    public HouseQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<House> BuildFilteredQuery(HouseFilters filters)
    {
        // No explicit !IsDeleted filter here — House already carries a global query filter
        // (ShmsDbContext: modelBuilder.Entity<House>().HasQueryFilter(e => !e.IsDeleted)), so adding
        // one here would just be a redundant duplicate of it, not a fix for anything.
        var query = _context.Houses
            .Include(h => h.Flat)
            .Include(h => h.HouseTypeRef)
            .AsQueryable();

        if (filters.LandlordId.HasValue)
            query = query.Where(h => h.Flat != null && h.Flat.LandlordId == filters.LandlordId.Value);

        if (filters.FlatId.HasValue)
            query = query.Where(h => h.FlatId == filters.FlatId.Value);

        if (!string.IsNullOrWhiteSpace(filters.OccupancyStatus) &&
            Enum.TryParse<OccupancyStatus>(filters.OccupancyStatus, true, out var parsedStatus))
        {
            query = query.Where(h => h.OccupancyStatus == parsedStatus);
        }

        return query;
    }
}
