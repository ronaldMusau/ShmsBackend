using System;
using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.House;

public class CreateHouseDto
{
    [Required]
    public string HouseNumber { get; set; } = string.Empty;

    [Required]
    public string HouseType { get; set; } = string.Empty;

    [Required]
    [Range(1000, double.MaxValue)]
    public decimal RentFee { get; set; }

    [Required]
    [Range(1000, double.MaxValue)]
    public decimal DepositFee { get; set; }

    [Required]
    public Guid FlatId { get; set; }
}
