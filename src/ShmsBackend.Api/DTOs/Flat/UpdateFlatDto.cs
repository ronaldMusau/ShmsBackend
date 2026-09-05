using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Flat;

public class UpdateFlatDto
{
    public string? FlatName { get; set; }
    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }
    public string? GoogleMapsLink { get; set; }
    public Guid? AgentId { get; set; }
    public bool ClearAgent { get; set; } = false;
    public int RentDueDay { get; set; }
    public int BillableGracePeriodMonths { get; set; }
    public int VacateNoticeDeadlineDay { get; set; }
    public bool SitDeposit { get; set; }
    [Required]
    public string SubmissionNotes { get; set; } = string.Empty;
    public List<HouseTypeChangeDto>? HouseTypeChanges { get; set; }
}

public class HouseTypeChangeDto
{
    public string ActionType { get; set; } = string.Empty;
    public Guid HouseTypeId { get; set; }
    public string? Prefix { get; set; }
    public decimal? RentFee { get; set; }
    public decimal? DepositFee { get; set; }
    public int? Count { get; set; }
    public int? AdditionalCount { get; set; }
    public string? DeleteReason { get; set; }
    public int? EffectiveMonth { get; set; }
    public int? EffectiveYear { get; set; }
}
