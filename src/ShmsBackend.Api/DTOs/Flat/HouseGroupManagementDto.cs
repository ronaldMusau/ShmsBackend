using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.Flat;

public class IncreaseHouseCountDto
{
    [Required]
    [Range(1, 200)]
    public int AdditionalCount { get; set; }
}

public class EditHouseGroupDto
{
    [Required]
    public string NewPrefix { get; set; } = string.Empty;

    [Required]
    [Range(1, double.MaxValue)]
    public decimal NewRentFee { get; set; }

    [Required]
    [Range(1, double.MaxValue)]
    public decimal NewDepositFee { get; set; }

    [Required]
    [Range(1, 200)]
    public int NewCount { get; set; }
}
