using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class RewardTransactionFilters
{
    public Guid? TenantId { get; set; }
    public Guid? FlatId { get; set; }
    public Guid? HouseId { get; set; }
    public string? TransactionType { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinPoints { get; set; }
    public decimal? MaxPoints { get; set; }
    public string? Search { get; set; }
}

/// <summary>
/// Single source of truth for filtered-reward-transaction queries, mirroring RewardController.GetTransactions'
/// exact filter chain — including FlatId/HouseId resolved via the Tenant.House nav chain rather than a
/// direct column, since RewardTransaction doesn't carry FlatId/HouseId itself.
/// </summary>
public class RewardTransactionQueryService
{
    private readonly ShmsDbContext _context;

    public RewardTransactionQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<RewardTransaction> BuildFilteredQuery(RewardTransactionFilters filters)
    {
        var query = _context.RewardTransactions
            .Include(t => t.Tenant)
                .ThenInclude(t => t!.House)
                    .ThenInclude(h => h!.Flat)
            .AsQueryable();

        if (filters.TenantId.HasValue)
            query = query.Where(t => t.TenantId == filters.TenantId.Value);

        if (filters.FlatId.HasValue)
            query = query.Where(t => t.Tenant != null && t.Tenant.House != null && t.Tenant.House.FlatId == filters.FlatId.Value);

        if (filters.HouseId.HasValue)
            query = query.Where(t => t.Tenant != null && t.Tenant.HouseId == filters.HouseId.Value);

        if (!string.IsNullOrEmpty(filters.TransactionType))
            query = query.Where(t => t.TransactionType == filters.TransactionType);

        if (filters.FromDate.HasValue)
            query = query.Where(t => t.CreatedAt >= filters.FromDate.Value);

        if (filters.ToDate.HasValue)
            query = query.Where(t => t.CreatedAt < filters.ToDate.Value.Date.AddDays(1));

        if (filters.MinPoints.HasValue)
            query = query.Where(t => t.Points >= filters.MinPoints.Value);

        if (filters.MaxPoints.HasValue)
            query = query.Where(t => t.Points <= filters.MaxPoints.Value);

        if (!string.IsNullOrEmpty(filters.Search))
            query = query.Where(t => t.Tenant != null &&
                (t.Tenant.FirstName.Contains(filters.Search) || t.Tenant.LastName.Contains(filters.Search)));

        return query;
    }
}
