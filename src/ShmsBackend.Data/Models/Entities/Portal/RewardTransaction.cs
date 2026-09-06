using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class RewardTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public string TransactionType { get; set; } = string.Empty; // "Earned" or "Redeemed"
    public string Source { get; set; } = string.Empty; // "InitialPayment", "RegularPayment", "Redemption"
    public int Points { get; set; } // positive for Earned, positive magnitude for Redeemed too (sign implied by TransactionType, not stored as negative)
    public decimal? AmountPaidOrRedeemed { get; set; } // KES amount that drove this (payment amount for Earned, converted KES for Redeemed)
    public int BalanceAfter { get; set; }
    public Guid? RelatedPaymentId { get; set; } // the Payment row that triggered earning, or that redemption was applied to (first row touched)
    public string? RedemptionReference { get; set; } // the RDM-... reference, set only for Redeemed rows
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
