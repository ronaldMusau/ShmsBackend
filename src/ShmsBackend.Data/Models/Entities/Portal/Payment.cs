using System;
using ShmsBackend.Data.Models.Interfaces;

namespace ShmsBackend.Data.Models.Entities.Portal;

public enum PaymentTransactionStatus
{
    Pending,
    Processing,
    Paid,
    PartiallyPaid,
    Overdue,
    Failed,
    Cancelled
}

public enum PaymentType
{
    InitialPayment,   // Deposit + First Rent + ServiceCharge
    Rent,             // Monthly rent
    Deposit,          // Deposit only
    ServiceCharge,    // Service charge only
    Partial,          // Partial payment
    Credit            // Overpayment credit
}

public enum PaymentMethod
{
    Mpesa,
    Cash,
    BankTransfer
}

public class Payment : ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Relations
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public Guid HouseId { get; set; }
    public House? House { get; set; }
    public Guid FlatId { get; set; }
    public Flat? Flat { get; set; }
    public Guid? LandlordId { get; set; }

    // Amount tracking
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; } = 0;
    public decimal Balance { get; set; } = 0;
    public decimal? RentAmount { get; set; }
    public decimal? DepositAmount { get; set; }
    public decimal? ServiceChargeAmount { get; set; }
    public decimal? CreditApplied { get; set; }

    // Payment details
    public PaymentTransactionStatus PaymentStatus { get; set; } = PaymentTransactionStatus.Pending;
    public PaymentType PaymentType { get; set; } = PaymentType.Rent;
    public PaymentMethod? PaymentMethod { get; set; }

    // M-Pesa specific
    public string? MpesaReceiptNumber { get; set; }
    public string? MpesaTransactionId { get; set; }
    public string? CheckoutRequestId { get; set; }
    public string? MerchantRequestId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? MpesaResultCode { get; set; }
    public string? MpesaResultDesc { get; set; }

    // Schedule
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public int Month { get; set; }
    public int Year { get; set; }
    public int RetryCount { get; set; } = 0;

    // Metadata
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public bool IsInitialPayment { get; set; } = false;

    // Soft delete
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
