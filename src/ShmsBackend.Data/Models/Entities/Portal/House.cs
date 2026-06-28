using System;
using System.Collections.Generic;

namespace ShmsBackend.Data.Models.Entities.Portal;

public enum HouseType
{
    SingleRoom,
    Bedsitter,
    OneBedroom,
    TwoBedroom,
    ThreeBedroom,
    FourBedroom
}

public enum OccupancyStatus
{
    Vacant,
    Occupied
}

public enum PaymentStatus
{
    Paid,
    NotPaid,
    PartiallyPaid
}

public class House
{
    public Guid Id { get; set; }
    public string HouseNumber { get; set; } = string.Empty;
    public HouseType HouseType { get; set; }
    public decimal RentFee { get; set; }
    public decimal DepositFee { get; set; }
    public OccupancyStatus OccupancyStatus { get; set; } = OccupancyStatus.Vacant;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.NotPaid;
    public Guid FlatId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Flat? Flat { get; set; }
    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
