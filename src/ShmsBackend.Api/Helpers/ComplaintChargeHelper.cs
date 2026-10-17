using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Helpers;

/// <summary>
/// Shared rules + STK-push plumbing for the "tenant pays for a tenant-billed complaint" flow. Payments here are
/// platform-only: never exposed to the landlord and never distributed to one.
/// </summary>
public static class ComplaintChargeHelper
{
    public static bool IsTenantBilled(Complaint c) => c.IsBillable == true && c.BillableTarget == "Tenant";

    public static bool HasChargeAmount(Complaint c) => (c.BillableAmount ?? 0m) > 0m;

    /// <summary>The tenant has accepted and still owes a charge.</summary>
    public static bool IsChargeOutstanding(Complaint c) =>
        IsTenantBilled(c) && c.Status == "Approved" && c.TenantChargeDecision == "Accepted"
        && HasChargeAmount(c) && c.TenantChargePaidAt == null;

    /// <summary>True when escalation to an agent is allowed for a tenant-billed ticket.</summary>
    public static bool ChargeSettledForEscalation(Complaint c) =>
        !IsTenantBilled(c)
        || (c.TenantChargeDecision == "Accepted" && (!HasChargeAmount(c) || c.TenantChargePaidAt != null));

    public static async Task<(ComplaintCheckoutAttempt? Attempt, string? Error)> StartAsync(
        ShmsDbContext ctx, IMpesaService mpesa, Complaint complaint, string phone, string paidBy, Guid? adminId)
    {
        if (!IsChargeOutstanding(complaint))
            return (null, "There is no outstanding charge on this complaint.");
        if (string.IsNullOrWhiteSpace(phone))
            return (null, "A phone number is required.");

        var inFlight = await ctx.ComplaintCheckoutAttempts
            .AnyAsync(a => a.ComplaintId == complaint.Id && a.AttemptStatus == "Processing" && a.ProcessedAt == null
                           && a.CreatedAt > DateTime.UtcNow.AddMinutes(-3));
        if (inFlight)
            return (null, "A payment is already being processed for this complaint. Please wait a moment or check the phone.");

        var amount = complaint.BillableAmount!.Value;
        STKPushResponse stk;
        try
        {
            stk = await mpesa.InitiateSTKPushAsync(new STKPushRequest
            {
                PhoneNumber = phone,
                Amount = amount,
                AccountReference = $"CMP-{complaint.TicketNumber}",
                TransactionDesc = "Complaint fix",
                TenantId = complaint.TenantId.ToString(),
                HouseId = complaint.HouseId.ToString()
            });
        }
        catch (Exception ex) { return (null, ex.Message); }

        var attempt = new ComplaintCheckoutAttempt
        {
            ComplaintId = complaint.Id,
            Amount = amount,
            PhoneNumber = phone,
            PaidBy = paidBy,
            InitiatedByAdminId = adminId,
            CheckoutRequestId = stk.CheckoutRequestID,
            AttemptStatus = "Processing"
        };
        ctx.ComplaintCheckoutAttempts.Add(attempt);
        await ctx.SaveChangesAsync();
        return (attempt, null);
    }
}
