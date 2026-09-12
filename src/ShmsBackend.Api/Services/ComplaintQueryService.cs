using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class ComplaintFilters
{
    public Guid? FlatId { get; set; }
    public Guid? HouseId { get; set; }
    public string? Status { get; set; }
    public bool? IsBillable { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public Guid? LandlordId { get; set; }   // reserved for Stage I, unused for now
}

/// <summary>
/// Single source of truth for filtered-complaint queries, mirroring ComplaintController.GetAll's
/// exact filter chain (status/complaintTypeId/flatId/houseId/isBillable/CreatedAt-range) so the
/// reports feature stays in lockstep with that listing.
/// </summary>
public class ComplaintQueryService
{
    private readonly ShmsDbContext _context;

    public ComplaintQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<Complaint> BuildFilteredQuery(ComplaintFilters filters)
    {
        // ComplaintController.GetAll does not apply a !IsDeleted filter either — mirrored as-is here.
        var query = _context.Complaints.AsQueryable();

        if (filters.LandlordId.HasValue)
            query = query.Where(c => c.LandlordId == filters.LandlordId.Value);

        if (filters.FlatId.HasValue)
            query = query.Where(c => c.FlatId == filters.FlatId.Value);

        if (filters.HouseId.HasValue)
            query = query.Where(c => c.HouseId == filters.HouseId.Value);

        if (!string.IsNullOrEmpty(filters.Status))
            query = query.Where(c => c.Status == filters.Status);

        if (filters.IsBillable.HasValue)
            query = query.Where(c => c.IsBillable == filters.IsBillable.Value);

        if (filters.FromDate.HasValue)
            query = query.Where(c => c.CreatedAt >= filters.FromDate.Value);

        if (filters.ToDate.HasValue)
            query = query.Where(c => c.CreatedAt <= filters.ToDate.Value.AddDays(1));

        return query;
    }
}
