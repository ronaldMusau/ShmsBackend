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
    public bool? IsAwaitingExistingTenant { get; set; }
    // Month/Year are NOT applied as a Where clause on the House query itself — a house isn't "in" a
    // month. They're consumed downstream by HouseReportBuilder to resolve each house's rent-payment
    // status for that specific month, not to decide which houses appear in the report.
    public int? Month { get; set; }
    public int? Year { get; set; }
    public Guid? LandlordId { get; set; }   // set server-side only, never bound from a client filter param

    // Listing-report-only filters, mirroring HouseController.GetAllListingStats' filter set.
    public bool? IsListingHidden { get; set; }
    public bool? CommentsMuted { get; set; }
    public decimal? MinRent { get; set; }
    public decimal? MaxRent { get; set; }
    public string? County { get; set; }
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
            .Include(h => h.Images)
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

        if (filters.IsAwaitingExistingTenant.HasValue)
            query = query.Where(h => h.IsAwaitingExistingTenant == filters.IsAwaitingExistingTenant.Value);

        if (filters.IsListingHidden.HasValue)
            query = query.Where(h => h.IsListingHidden == filters.IsListingHidden.Value);

        if (filters.CommentsMuted.HasValue)
            query = query.Where(h => h.CommentsMuted == filters.CommentsMuted.Value);

        if (filters.MinRent.HasValue)
            query = query.Where(h => h.RentFee >= filters.MinRent.Value);

        if (filters.MaxRent.HasValue)
            query = query.Where(h => h.RentFee <= filters.MaxRent.Value);

        if (!string.IsNullOrWhiteSpace(filters.County))
            query = query.Where(h => h.Flat != null && h.Flat.County == filters.County);

        return query;
    }
}
