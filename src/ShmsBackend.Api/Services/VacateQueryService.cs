using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class VacateFilters
{
    public Guid? FlatId { get; set; }
    public Guid? HouseId { get; set; }
    public string? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? LandlordId { get; set; }   // reserved for Stage I, unused for now
}

/// <summary>
/// Single source of truth for filtered-vacate-request queries, mirroring VacateController.GetAllVacateRequests'
/// exact filter chain (status/flatId/houseId/CreatedAt-range) so the reports feature stays in lockstep
/// with that listing.
/// </summary>
public class VacateQueryService
{
    private readonly ShmsDbContext _context;

    public VacateQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<VacateRequest> BuildFilteredQuery(VacateFilters filters)
    {
        var query = _context.VacateRequests.Where(v => !v.IsDeleted).AsQueryable();

        if (filters.LandlordId.HasValue)
            query = query.Where(v => v.LandlordId == filters.LandlordId.Value);

        if (filters.FlatId.HasValue)
            query = query.Where(v => v.FlatId == filters.FlatId.Value);

        if (filters.HouseId.HasValue)
            query = query.Where(v => v.HouseId == filters.HouseId.Value);

        if (!string.IsNullOrEmpty(filters.Status))
            query = query.Where(v => v.Status == filters.Status);

        if (filters.FromDate.HasValue)
            query = query.Where(v => v.CreatedAt >= filters.FromDate.Value);

        if (filters.ToDate.HasValue)
            query = query.Where(v => v.CreatedAt <= filters.ToDate.Value.AddDays(1));

        return query;
    }
}
