using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;
using ShmsBackend.Data.Models.Enums;

namespace ShmsBackend.Api.Services;

public class TenantFilters
{
    public Guid? FlatId { get; set; }
    public Guid? HouseId { get; set; }
    public string? Status { get; set; }
    public int? TenancyCycle { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Search { get; set; }
    public Guid? LandlordId { get; set; }   // set server-side only, never bound from a client filter param
}

/// <summary>
/// Single source of truth for filtered-tenant queries — shared by TenantController's list endpoint
/// and the reports feature, so both stay in lockstep instead of maintaining two copies of the same
/// filtering logic.
/// </summary>
public class TenantQueryService
{
    private readonly ShmsDbContext _context;

    public TenantQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<Tenant> BuildFilteredQuery(TenantFilters filters)
    {
        var isVacated = string.Equals(filters.Status, "vacated", StringComparison.OrdinalIgnoreCase);

        var query = isVacated
            ? _context.Tenants
                .IgnoreQueryFilters()
                .Include(t => t.House)
                    .ThenInclude(h => h!.Flat)
                .Where(t => t.IsDeleted && t.HasCompletedInitialPayment)
            : _context.Tenants
                .Include(t => t.House)
                    .ThenInclude(h => h!.Flat)
                .Where(t => !t.IsDeleted);

        if (filters.LandlordId.HasValue)
            query = query.Where(t => t.House != null && t.House.Flat != null && t.House.Flat.LandlordId == filters.LandlordId.Value);

        if (filters.FlatId.HasValue)
            query = query.Where(t => t.House != null && t.House.FlatId == filters.FlatId.Value);

        if (filters.HouseId.HasValue)
            query = query.Where(t => t.HouseId == filters.HouseId.Value);

        if (filters.TenancyCycle.HasValue)
            query = query.Where(t => t.TenancyCycle == filters.TenancyCycle.Value);

        if (filters.FromDate.HasValue)
            query = query.Where(t => t.CreatedAt >= filters.FromDate.Value);

        if (filters.ToDate.HasValue)
            query = query.Where(t => t.CreatedAt <= filters.ToDate.Value);

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim();
            query = query.Where(t =>
                EF.Functions.Like(t.FirstName, $"%{search}%") ||
                EF.Functions.Like(t.LastName, $"%{search}%") ||
                EF.Functions.Like(t.Email, $"%{search}%"));
        }

        if (!isVacated && !string.IsNullOrWhiteSpace(filters.Status) &&
            Enum.TryParse<TenantStatus>(filters.Status, true, out var parsedStatus))
        {
            query = query.Where(t => t.TenantStatus == parsedStatus);
        }

        return query;
    }
}
