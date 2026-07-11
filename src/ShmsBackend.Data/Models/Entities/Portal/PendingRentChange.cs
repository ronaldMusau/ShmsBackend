using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class PendingRentChange
{
    public Guid Id { get; set; }
    public Guid HouseId { get; set; }
    public House? House { get; set; }
    public decimal NewRentFee { get; set; }
    public decimal NewDepositFee { get; set; }
    public int EffectiveMonth { get; set; }
    public int EffectiveYear { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
}
