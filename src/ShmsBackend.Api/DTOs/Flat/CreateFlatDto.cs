using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Flat;

public class HouseGroupDto
{
    [Required]
    public string HouseType { get; set; } = string.Empty;

    [Required]
    [Range(1, 200)]
    public int Count { get; set; }

    [Required]
    public string HouseNumberPrefix { get; set; } = string.Empty;

    [Required]
    [Range(1, double.MaxValue)]
    public decimal RentFee { get; set; }

    [Required]
    [Range(1, double.MaxValue)]
    public decimal DepositFee { get; set; }
}

public class CreateFlatDto
{
    [Required]
    public string FlatName { get; set; } = string.Empty;

    public string? County { get; set; }
    public string? Constituency { get; set; }
    public string? Ward { get; set; }

    [Required]
    public Guid LandlordId { get; set; }

    public Guid? AgentId { get; set; }

    public int RentDueDay { get; set; } = 5;
    public int BillableGracePeriodMonths { get; set; } = 3;

    public List<HouseGroupDto> Houses { get; set; } = new();
}
