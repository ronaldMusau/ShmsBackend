using System;
using System.Collections.Generic;

namespace ShmsBackend.Api.Models.DTOs.House;

public class BulkPriceChangeDto
{
    public List<Guid> HouseIds { get; set; } = new();
    public decimal NewRentFee { get; set; }
    public decimal NewDepositFee { get; set; }
    public int? EffectiveMonth { get; set; }
    public int? EffectiveYear { get; set; }
}
