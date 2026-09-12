using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using PaymentEntity = ShmsBackend.Data.Models.Entities.Portal.Payment;

namespace ShmsBackend.Api.Services;

public class ServiceChargeFilters
{
    public Guid? TenantId { get; set; }
    public Guid? HouseId { get; set; }
    public Guid? FlatId { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
}

/// <summary>
/// Single source of truth for filtered-service-charge queries, mirroring
/// PaymentController.GetAllServiceChargesCollected's exact query/filter shape — Payment rows with a
/// positive ServiceChargeAmount that actually collected an M-Pesa receipt.
/// </summary>
public class ServiceChargeQueryService
{
    private readonly ShmsDbContext _context;

    public ServiceChargeQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<PaymentEntity> BuildFilteredQuery(ServiceChargeFilters filters)
    {
        var query = _context.Payments
            .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
            .Where(p => p.ServiceChargeAmount > 0 && p.MpesaReceiptNumber != null)
            .AsQueryable();

        if (filters.TenantId.HasValue) query = query.Where(p => p.TenantId == filters.TenantId.Value);
        if (filters.HouseId.HasValue) query = query.Where(p => p.HouseId == filters.HouseId.Value);
        if (filters.FlatId.HasValue) query = query.Where(p => p.FlatId == filters.FlatId.Value);
        if (filters.Month.HasValue) query = query.Where(p => p.Month == filters.Month.Value);
        if (filters.Year.HasValue) query = query.Where(p => p.Year == filters.Year.Value);
        if (filters.FromDate.HasValue) query = query.Where(p => p.PaidAt >= filters.FromDate.Value);
        if (filters.ToDate.HasValue) query = query.Where(p => p.PaidAt < filters.ToDate.Value.Date.AddDays(1));
        if (filters.MinAmount.HasValue) query = query.Where(p => p.ServiceChargeAmount >= filters.MinAmount.Value);
        if (filters.MaxAmount.HasValue) query = query.Where(p => p.ServiceChargeAmount <= filters.MaxAmount.Value);

        return query;
    }
}
