using System;

namespace ShmsBackend.Api.Models.Reports;

public class ReceiptData
{
    public string ReceiptNumber { get; set; } = "";   // MpesaReceiptNumber if present, else "PMT-{Payment.Id short form}"
    public DateTime? PaidAt { get; set; }
    public string TenantName { get; set; } = "";
    public string? TenantPhone { get; set; }
    public string? TenantEmail { get; set; }
    public string FlatName { get; set; } = "";
    public string HouseNumber { get; set; } = "";
    public string PaymentType { get; set; } = "";
    public int Month { get; set; }
    public int Year { get; set; }
    public decimal Amount { get; set; }
    public decimal? RentAmount { get; set; }
    public decimal? DepositAmount { get; set; }
    public decimal? ServiceChargeAmount { get; set; }
    public decimal? CreditApplied { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; }
    public string? PaymentMethod { get; set; }
    public string? MpesaReceiptNumber { get; set; }
    public string Status { get; set; } = "";

    // Non-rent receipts (complaint charge, vacate settlement): a free-text reference replaces the
    // month/year "Period" row, and Description says what the money was for.
    public string? PeriodLabel { get; set; }
    public string? Description { get; set; }
}
