using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class RewardSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsGlobalEnabled { get; set; } = false;
    public decimal InitialPaymentEarnRate { get; set; } = 0; // points per KES paid on initial payment
    public decimal RegularPaymentEarnRate { get; set; } = 0; // points per KES paid on regular rent payment
    public decimal RedemptionRate { get; set; } = 0; // KES per point when redeeming
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
