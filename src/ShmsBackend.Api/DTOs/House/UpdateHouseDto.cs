namespace ShmsBackend.Api.Models.DTOs.House;

public class UpdateHouseDto
{
    public string? HouseNumber { get; set; }
    public Guid? HouseTypeId { get; set; }
    public decimal? RentFee { get; set; }
    public decimal? DepositFee { get; set; }
    public string? OccupancyStatus { get; set; }
    public string? PaymentStatus { get; set; }
}
