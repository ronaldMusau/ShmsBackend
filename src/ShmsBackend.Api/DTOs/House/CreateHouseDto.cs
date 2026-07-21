using System;
using System.ComponentModel.DataAnnotations;

namespace ShmsBackend.Api.Models.DTOs.House;

public class CreateHouseDto
{
    [Required]
    public string HouseNumber { get; set; } = string.Empty;

    [Required]
    public Guid HouseTypeId { get; set; }

    [Required]
    [Range(1, double.MaxValue)]
    public decimal RentFee { get; set; }

    [Required]
    [Range(1, double.MaxValue)]
    public decimal DepositFee { get; set; }

    [Required]
    public Guid FlatId { get; set; }
}
