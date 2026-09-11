using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using PaymentEntity = ShmsBackend.Data.Models.Entities.Portal.Payment;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services;

public class PaymentFilters
{
    public Guid? FlatId { get; set; }
    public Guid? HouseId { get; set; }
    public Guid? TenantId { get; set; }
    public string? Status { get; set; }
    public string? PaymentType { get; set; }
    public string? PaymentMethod { get; set; }
    public int? Month { get; set; }
    public int? Year { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public bool? IsInitialPayment { get; set; }
    public Guid? LandlordId { get; set; }   // set server-side only, never bound from a client filter param
    public int? TenancyCycle { get; set; }  // set server-side only for tenant-scoped actions, never bound from a client filter param
}

/// <summary>
/// Single source of truth for filtered-payment queries — mirrors PortalPaymentController.GetLandlordPayments'
/// exact filter chain, generalized with an optional LandlordId so both admin-wide and landlord-scoped
/// report builders can share it without duplicating filtering logic. Does not replace or modify
/// PaymentController/PortalPaymentController — those keep their own inline queries as-is.
/// </summary>
public class PaymentQueryService
{
    private readonly ShmsDbContext _context;

    public PaymentQueryService(ShmsDbContext context)
    {
        _context = context;
    }

    public IQueryable<PaymentEntity> BuildFilteredQuery(PaymentFilters filters)
    {
        var query = _context.Payments
            .Include(p => p.House)
                .ThenInclude(h => h!.Flat)
            .Include(p => p.Tenant)
            .Where(p => !p.IsDeleted);

        if (filters.LandlordId.HasValue)
            query = query.Where(p => p.LandlordId == filters.LandlordId.Value);

        if (filters.FlatId.HasValue)
            query = query.Where(p => p.FlatId == filters.FlatId.Value);

        if (filters.HouseId.HasValue)
            query = query.Where(p => p.HouseId == filters.HouseId.Value);

        if (filters.TenantId.HasValue)
            query = query.Where(p => p.TenantId == filters.TenantId.Value);

        if (filters.TenancyCycle.HasValue)
            query = query.Where(p => p.TenancyCycle == filters.TenancyCycle.Value);

        // Mirrors GetLandlordPayments' exact default: explicit status wins; otherwise only the
        // "settled or overdue" statuses show, hiding Pending/Processing/Failed/Cancelled noise.
        if (!string.IsNullOrEmpty(filters.Status) && Enum.TryParse<PaymentTransactionStatus>(filters.Status, true, out var ps))
            query = query.Where(p => p.PaymentStatus == ps);
        else
            query = query.Where(p => p.PaymentStatus == PaymentTransactionStatus.Paid
                                  || p.PaymentStatus == PaymentTransactionStatus.PartiallyPaid
                                  || p.PaymentStatus == PaymentTransactionStatus.Overdue);

        if (!string.IsNullOrEmpty(filters.PaymentType) && Enum.TryParse<PaymentType>(filters.PaymentType, true, out var pt))
            query = query.Where(p => p.PaymentType == pt);

        if (!string.IsNullOrEmpty(filters.PaymentMethod) && Enum.TryParse<PaymentMethod>(filters.PaymentMethod, true, out var pm))
            query = query.Where(p => p.PaymentMethod == pm);

        if (filters.Month.HasValue)
            query = query.Where(p => p.Month == filters.Month.Value);

        if (filters.Year.HasValue)
            query = query.Where(p => p.Year == filters.Year.Value);

        // Date range applies to PaidAt (mirrors GetLandlordPayments), not CreatedAt (GetAllPayments'
        // convention) — a report is about when money moved, which matters more for a landlord-scoped
        // payments report than when the row was first created.
        if (filters.FromDate.HasValue)
            query = query.Where(p => p.PaidAt >= filters.FromDate.Value);

        if (filters.ToDate.HasValue)
            query = query.Where(p => p.PaidAt <= filters.ToDate.Value);

        if (filters.MinAmount.HasValue)
            query = query.Where(p => p.Amount >= filters.MinAmount.Value);

        if (filters.MaxAmount.HasValue)
            query = query.Where(p => p.Amount <= filters.MaxAmount.Value);

        if (filters.IsInitialPayment.HasValue)
            query = query.Where(p => p.IsInitialPayment == filters.IsInitialPayment.Value);

        return query;
    }
}
