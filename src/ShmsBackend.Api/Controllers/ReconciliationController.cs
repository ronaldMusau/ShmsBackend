using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Api.Services.Security;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Controllers;

/// <summary>
/// Payment reconciliation: finds M-Pesa payments whose records disagree with each other (missing or reused receipts,
/// amounts that do not add up, prompts that never resolved or succeeded without being applied). Read-only: it reports
/// and never changes a payment. Staff then follow up with M-Pesa.
/// </summary>
[ApiController]
[Route("api/reconciliation")]
[Authorize(Roles = "SuperAdmin,Admin,Accountant")]
public class ReconciliationController : ControllerBase
{
    private const decimal Tolerance = 0.01m;
    private const int MaxIssues = 500;
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LookupWindow = TimeSpan.FromDays(3);

    private readonly ShmsDbContext _db;
    private readonly IMpesaService _mpesa;
    private readonly ISecurityEventService _events;

    public ReconciliationController(ShmsDbContext db, IMpesaService mpesa, ISecurityEventService events)
    { _db = db; _mpesa = mpesa; _events = events; }

    public record Issue(string Type, string Severity, string Source, string Reference, decimal? Amount,
        string Detail, DateTime At, Guid? PaymentId, string? CheckoutRequestId);

    [HttpGet]
    [EnableRateLimiting("heavy")]
    public async Task<IActionResult> Run([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var end = (to ?? DateTime.UtcNow).Date.AddDays(1);
        var start = (from ?? end.AddDays(-31)).Date;
        if (start >= end || (end - start).TotalDays > 366) return BadRequest(new { success = false, message = "Choose a period of up to one year." });
        var now = DateTime.UtcNow;
        var issues = new List<Issue>();
        var olderUnresolved = 0;

        // ---- rent / deposit payments (M-Pesa) ----
        var pays = await _db.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.CreatedAt >= start && p.CreatedAt < end)
            .Select(p => new
            {
                p.Id, p.Amount, p.AmountPaid, p.Balance, p.PaymentStatus, p.PaymentMethod, p.MpesaReceiptNumber, p.CheckoutRequestId,
                p.TenantId, p.CreatedAt, p.PaidAt, p.Month, p.Year, p.CreditApplied, p.RedemptionReference,
                AppliedSum = p.Applications.Sum(a => (decimal?)a.AmountApplied) ?? 0m,
                AppCount = p.Applications.Count()
            }).ToListAsync();

        var paidish = new[] { PaymentTransactionStatus.Paid, PaymentTransactionStatus.PartiallyPaid };
        foreach (var p in pays)
        {
            var label = $"Payment {p.Month:00}/{p.Year}";
            if (p.PaymentMethod == PaymentMethod.Mpesa && paidish.Contains(p.PaymentStatus) && p.AmountPaid > 0
                && string.IsNullOrWhiteSpace(p.MpesaReceiptNumber) && p.AppCount == 0)
                issues.Add(new("PaidNoReceipt", "High", "Rent payment", label, p.AmountPaid,
                    "Marked as paid by M-Pesa but there is no M-Pesa receipt number on it.", p.PaidAt ?? p.CreatedAt, p.Id, p.CheckoutRequestId));

            if (p.PaymentStatus != PaymentTransactionStatus.Cancelled && Math.Abs(p.Amount - p.AmountPaid - p.Balance) > Tolerance && p.Balance != 0)
                issues.Add(new("AmountMismatch", "High", "Rent payment", label, p.Amount,
                    $"Amount {p.Amount:N2} minus paid {p.AmountPaid:N2} should leave {(p.Amount - p.AmountPaid):N2}, but the balance is {p.Balance:N2}.", p.CreatedAt, p.Id, p.CheckoutRequestId));

            // Credit from an earlier overpayment and points redemptions legitimately make up part of what was paid, so they are allowed for.
            var explained = p.AppliedSum + (p.CreditApplied ?? 0m);
            if (p.AppCount > 0 && p.PaymentMethod == PaymentMethod.Mpesa && Math.Abs(explained - p.AmountPaid) > Tolerance && paidish.Contains(p.PaymentStatus) && p.RedemptionReference == null)
                issues.Add(new("AmountMismatch", "High", "Rent payment", label, p.AmountPaid,
                    $"M-Pesa receipts applied add up to {p.AppliedSum:N2}{((p.CreditApplied ?? 0m) > 0 ? $" plus credit {p.CreditApplied:N2}" : "")}, but the payment says {p.AmountPaid:N2} was paid.", p.PaidAt ?? p.CreatedAt, p.Id, p.CheckoutRequestId));

            if (p.AmountPaid > p.Amount + Tolerance && p.PaymentStatus != PaymentTransactionStatus.Cancelled)
                issues.Add(new("AmountMismatch", "Medium", "Rent payment", label, p.AmountPaid,
                    $"Paid {p.AmountPaid:N2} is more than the amount due {p.Amount:N2}.", p.PaidAt ?? p.CreatedAt, p.Id, p.CheckoutRequestId));
        }

        // a receipt number that appears on payments of different tenants
        var receipts = await _db.PaymentApplications.AsNoTracking()
            .Where(a => a.AppliedAt >= start && a.AppliedAt < end && a.MpesaReceiptNumber != "")
            .Select(a => new { a.MpesaReceiptNumber, a.PaymentId, TenantId = a.Payment!.TenantId, a.AmountApplied, a.AppliedAt })
            .ToListAsync();
        foreach (var g in receipts.GroupBy(r => r.MpesaReceiptNumber).Where(g => g.Select(x => x.TenantId).Distinct().Count() > 1))
            issues.Add(new("DuplicateReceipt", "High", "Rent payment", g.Key, g.Sum(x => x.AmountApplied),
                $"The same M-Pesa receipt was applied to {g.Select(x => x.TenantId).Distinct().Count()} different tenants.", g.Max(x => x.AppliedAt), g.First().PaymentId, null));

        // ---- the prompts (checkout attempts) of all three kinds ----
        var rentRaw = await _db.PaymentCheckoutAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.CreatedAt >= start && a.CreatedAt < end)
            .Select(a => new { a.CheckoutRequestId, a.AttemptStatus, a.ResultDesc, a.ProcessedAt, a.CreatedAt, a.PaymentId,
                PayStatus = (PaymentTransactionStatus?)a.Payment!.PaymentStatus, PayPaid = (decimal?)a.Payment!.AmountPaid, PayDeleted = (bool?)a.Payment!.IsDeleted })
            .ToListAsync();
        var rent = rentRaw.Select(a => new Att("Rent payment", a.CheckoutRequestId, a.AttemptStatus, a.ResultDesc, null, a.ProcessedAt, a.CreatedAt, a.PaymentId,
            a.PayDeleted != true && ((a.PayStatus.HasValue && paidish.Contains(a.PayStatus.Value)) || (a.PayPaid ?? 0m) > 0m), a.PayDeleted == true)).ToList();
        var cmp = await _db.ComplaintCheckoutAttempts.AsNoTracking()
            .Where(a => a.CreatedAt >= start && a.CreatedAt < end)
            .Select(a => new Att("Complaint charge", a.CheckoutRequestId, a.AttemptStatus, a.ResultDesc, a.MpesaReceiptNumber, a.ProcessedAt, a.CreatedAt, null, true, false)).ToListAsync();
        var vac = await _db.VacateCheckoutAttempts.AsNoTracking()
            .Where(a => a.CreatedAt >= start && a.CreatedAt < end)
            .Select(a => new Att("Vacate settlement", a.CheckoutRequestId, a.AttemptStatus, a.ResultDesc, a.MpesaReceiptNumber, a.ProcessedAt, a.CreatedAt, null, true, false)).ToListAsync();

        foreach (var a in rent.Concat(cmp).Concat(vac))
        {
            var age = now - a.CreatedAt;
            if (a.Status == "Processing" && age > StuckAfter)
                issues.Add(new("StuckPrompt", "Medium", a.Source, a.Checkout, null, "The payment prompt is still waiting for a result after more than 10 minutes.", a.CreatedAt, a.PaymentId, a.Checkout));
            else if (a.Status == "Failed" && a.ProcessedAt == null && (a.Desc ?? "").StartsWith("Timed out", StringComparison.OrdinalIgnoreCase) && age > StuckAfter)
            {
                // M-Pesa can only be asked about recent prompts; older ones are counted but not listed one by one.
                if (age <= LookupWindow)
                    issues.Add(new("NoResult", "Medium", a.Source, a.Checkout, null, "No result came back from M-Pesa. The customer may or may not have paid: check with M-Pesa.", a.CreatedAt, a.PaymentId, a.Checkout));
                else olderUnresolved++;
            }
            if (a.Status == "Success" && a.PaymentDeleted)
                issues.Add(new("PaymentRemoved", "Medium", a.Source, a.Checkout, null, "M-Pesa money was received for this prompt, but the payment record was later deleted.", a.ProcessedAt ?? a.CreatedAt, a.PaymentId, a.Checkout));
            else if (a.Status == "Success" && !a.PaymentSettled)
                issues.Add(new("SuccessNotApplied", "High", a.Source, a.Checkout, null, "M-Pesa reported success but nothing is recorded as paid on the payment.", a.ProcessedAt ?? a.CreatedAt, a.PaymentId, a.Checkout));
            if (a.Status == "Success" && a.Source != "Rent payment" && string.IsNullOrWhiteSpace(a.Receipt))
                issues.Add(new("PaidNoReceipt", "High", a.Source, a.Checkout, null, "Marked successful but has no M-Pesa receipt number.", a.ProcessedAt ?? a.CreatedAt, a.PaymentId, a.Checkout));
        }
        foreach (var g in rent.Concat(cmp).Concat(vac).Where(x => !string.IsNullOrEmpty(x.Checkout)).GroupBy(x => x.Checkout).Where(g => g.Count(x => x.Status == "Success") > 1))
            issues.Add(new("DuplicateReceipt", "High", g.First().Source, g.Key, null, $"{g.Count(x => x.Status == "Success")} successful attempts share the same M-Pesa request id.", g.Max(x => x.CreatedAt), null, g.Key));

        var ordered = issues.OrderByDescending(i => i.Severity == "High").ThenByDescending(i => i.At).ToList();
        var shown = ordered.Take(MaxIssues).ToList();
        return Ok(new
        {
            success = true,
            from = start, to = end.AddDays(-1),
            checkedPayments = pays.Count,
            checkedPrompts = rent.Count + cmp.Count + vac.Count,
            olderUnresolved,
            total = ordered.Count, truncated = ordered.Count > MaxIssues,
            counts = ordered.GroupBy(i => i.Type).ToDictionary(g => g.Key, g => g.Count()),
            issues = shown
        });
    }

    private record Att(string Source, string Checkout, string Status, string? Desc, string? Receipt, DateTime? ProcessedAt, DateTime CreatedAt, Guid? PaymentId, bool PaymentSettled, bool PaymentDeleted);

    /// <summary>Asks M-Pesa what really happened to one prompt. Read-only: it does not change any payment.</summary>
    [HttpPost("check/{checkoutRequestId}")]
    [EnableRateLimiting("payment")]
    public async Task<IActionResult> CheckWithMpesa(string checkoutRequestId)
    {
        if (string.IsNullOrWhiteSpace(checkoutRequestId) || checkoutRequestId.Length > 100 || !checkoutRequestId.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            return BadRequest(new { success = false, message = "That request id is not valid." });
        var known = await _db.PaymentCheckoutAttempts.AnyAsync(a => a.CheckoutRequestId == checkoutRequestId)
                 || await _db.ComplaintCheckoutAttempts.AnyAsync(a => a.CheckoutRequestId == checkoutRequestId)
                 || await _db.VacateCheckoutAttempts.AnyAsync(a => a.CheckoutRequestId == checkoutRequestId);
        if (!known) return NotFound(new { success = false, message = "No payment prompt with that id." });

        var r = await _mpesa.QuerySTKStatusAsync(checkoutRequestId);
        var paid = r.ResultCode == "0";
        var answered = !string.IsNullOrEmpty(r.ResultCode);
        await _events.RecordAsync("Payments", "ReconciliationCheck", "Info",
            $"M-Pesa was asked about prompt {checkoutRequestId}: {(answered ? (paid ? "paid" : "not paid") : "no answer yet")}.",
            User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Role));
        return Ok(new
        {
            success = true,
            answered,
            paid,
            resultCode = r.ResultCode,
            message = !answered ? "M-Pesa has no final answer for this prompt yet (it may still be waiting for the PIN, or too old to look up)."
                      : paid ? "M-Pesa says this prompt was paid. If the payment here is not marked as paid, follow it up with M-Pesa's records."
                      : $"M-Pesa says it was not paid: {r.ResultDesc}"
        });
    }
}
