using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class ExplorerFilters
{
    public string? Status { get; set; }
    public Guid? HouseId { get; set; }
    public Guid? FlatId { get; set; }   // resolved via a House-id subquery, no direct column
    public Guid? ExplorerId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

/// <summary>
/// Single source of truth for filtered ExplorerInterest queries, mirroring TenantQueryService's shape.
/// ExplorerInterest has scalar HouseId only (no navigation property — same FK-only convention as
/// HouseListingLike/HouseListingRating/etc.), so FlatId filters via the same House-id subquery
/// ListingViewingSessionQueryService already uses for the same reason.
/// </summary>
public class ExplorerInterestQueryService
{
    private readonly ShmsDbContext _context;

    public ExplorerInterestQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<ExplorerInterest> BuildFilteredQuery(ExplorerFilters filters)
    {
        var query = _context.ExplorerInterests.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filters.Status))
            query = query.Where(ei => ei.Status == filters.Status);

        if (filters.HouseId.HasValue)
            query = query.Where(ei => ei.HouseId == filters.HouseId.Value);

        if (filters.FlatId.HasValue)
        {
            var houseIdsInFlat = _context.Houses
                .Where(h => h.FlatId == filters.FlatId.Value)
                .Select(h => h.Id);
            query = query.Where(ei => houseIdsInFlat.Contains(ei.HouseId));
        }

        if (filters.ExplorerId.HasValue)
            query = query.Where(ei => ei.ExplorerId == filters.ExplorerId.Value);

        if (filters.FromDate.HasValue)
            query = query.Where(ei => ei.CreatedAt >= filters.FromDate.Value.Date);

        if (filters.ToDate.HasValue)
            query = query.Where(ei => ei.CreatedAt < filters.ToDate.Value.Date.AddDays(1));

        return query;
    }
}
