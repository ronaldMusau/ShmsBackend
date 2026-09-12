using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class RewardTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string TransactionType { get; set; } = string.Empty; // "Earned" or "Redeemed"
    public string Source { get; set; } = string.Empty; // "InitialPayment", "RegularPayment", "Redemption"
    public decimal Points { get; set; } // positive for Earned, positive magnitude for Redeemed too (sign implied by TransactionType, not stored as negative)
    public decimal? AmountPaidOrRedeemed { get; set; } // KES amount that drove this (payment amount for Earned, converted KES for Redeemed)
    public decimal BalanceAfter { get; set; } // decimal to mirror Tenant.PointsBalance, which this is a snapshot of
    public int TenancyCycle { get; set; } // the tenant's TenancyCycle at the moment this row was written — authoritative for cycle scoping, independent of RelatedPaymentId (which can be null on a Redeemed row)
    public Guid? RelatedPaymentId { get; set; } // the Payment row that triggered earning, or that redemption was applied to (first row touched)
    public string? RedemptionReference { get; set; } // the RDM-... reference, set only for Redeemed rows
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
