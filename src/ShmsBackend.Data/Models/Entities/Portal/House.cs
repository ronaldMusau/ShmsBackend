using System;
using System.Collections.Generic;
using ShmsBackend.Data.Models.Interfaces;

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
    PartiallyPaid,
    Overdue
}

public class House : ISoftDelete
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
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    public Flat? Flat { get; set; }
    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
}
