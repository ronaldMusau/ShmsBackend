using System;

namespace ShmsBackend.Data.Models.Entities.Portal;

public class FlatEditHouseTypeChange
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FlatEditRequestId { get; set; }
    public FlatEditRequest? FlatEditRequest { get; set; }
    public string ActionType { get; set; } = string.Empty; // "AddLine", "EditGroup", "IncreaseCount", "Delete"
    public Guid HouseTypeId { get; set; }
    public string? ProposedPrefix { get; set; }
    public decimal? ProposedRentFee { get; set; }
    public decimal? ProposedDepositFee { get; set; }
    public int? ProposedCount { get; set; } // used by AddLine (total count) and EditGroup (new total count)
    public int? AdditionalCount { get; set; } // used by IncreaseCount only
    public string? DeleteReason { get; set; } // required when ActionType == "Delete", null otherwise
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
