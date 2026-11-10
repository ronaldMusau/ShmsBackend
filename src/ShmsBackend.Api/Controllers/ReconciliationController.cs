using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Api.Services.Security;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
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
public class ReconciliationController : ControllerBase, Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter
{
    private const int MaxIssues = 500;

    private readonly ShmsDbContext _db;
    private readonly IMpesaService _mpesa;
    private readonly ISecurityEventService _events;
    private readonly INotificationService _notify;
    private readonly IReversalService _reversal;
    private readonly ReconciliationScanner _scanner;
    private readonly IPaymentService _payments;

    public ReconciliationController(ShmsDbContext db, IMpesaService mpesa, ISecurityEventService events, INotificationService notify, IReversalService reversal, ReconciliationScanner scanner, IPaymentService payments)
    { _db = db; _mpesa = mpesa; _events = events; _notify = notify; _reversal = reversal; _scanner = scanner; _payments = payments; }

    public record Issue(string Type, string Severity, string Source, string Reference, decimal? Amount,
        string Detail, DateTime At, Guid? PaymentId, string? CheckoutRequestId,
        Guid? TenantId = null, string? TenantName = null, string? House = null, string Key = "", bool ReversalPending = false);

    private static readonly string[] IssueTypes = { "PaidNoReceipt", "AmountMismatch", "DuplicateReceipt", "StuckPrompt", "NoResult", "SuccessNotApplied", "PaymentRemoved" };

    /// <summary>One stable identity per finding, so a finding that has been dealt with stays out of the open list.</summary>
    internal static string KeyOf(string type, string reference, Guid? paymentId, string? checkout) =>
        $"{type}|{reference}|{(type == "DuplicateReceipt" ? "" : paymentId?.ToString() ?? "")}|{checkout ?? ""}";

    [HttpGet]
    [EnableRateLimiting("heavy")]
    public async Task<IActionResult> Run([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var end = (to ?? DateTime.UtcNow).Date.AddDays(1);
        var start = (from ?? end.AddDays(-31)).Date;
        if (start >= end || (end - start).TotalDays > 366) return BadRequest(new { success = false, message = "Choose a period of up to one year." });
        var scan = await _scanner.ScanAsync(start, end);
        var open = scan.Open;
        var olderUnresolved = scan.OlderUnresolved;
        var keys = open.Select(i => i.Key).Distinct().ToList();
        var pendingKeys = (await _db.ReversalRequests.AsNoTracking().Where(q => q.Status == "AwaitingApproval" && keys.Contains(q.IssueKey)).Select(q => q.IssueKey).ToListAsync()).ToHashSet();
        open = open.Select(i => i with { ReversalPending = pendingKeys.Contains(i.Key) }).ToList();

        var done = await _db.ReconciliationReviews.AsNoTracking()
            .Where(r => (r.ReviewedAt >= start && r.ReviewedAt < end) || (r.IssueAt >= start && r.IssueAt < end))
            .OrderByDescending(r => r.ReviewedAt).Take(MaxIssues).ToListAsync();

        var ordered = open.OrderByDescending(i => i.Severity == "High").ThenByDescending(i => i.At).ToList();
        var shown = ordered.Take(MaxIssues).ToList();
        return Ok(new
        {
            success = true,
            from = start, to = end.AddDays(-1),
            checkedPayments = scan.PaymentsChecked,
            checkedPrompts = scan.PromptsChecked,
            olderUnresolved,
            total = ordered.Count, truncated = ordered.Count > MaxIssues,
            counts = ordered.GroupBy(i => i.Type).ToDictionary(g => g.Key, g => g.Count()),
            reconciledTotal = done.Count,
            reversedTotal = done.Count(d => d.Outcome == "Reversed"),
            canReverse = true,
            issues = shown,
            reconciled = done
        });
    }

    private record PayInfo(Guid TenantId, string Name, string? House);

    private async Task<Dictionary<Guid, PayInfo>> PaymentInfoAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return new();
        var rows = await _db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.TenantId, First = p.Tenant != null ? p.Tenant.FirstName : "", Last = p.Tenant != null ? p.Tenant.LastName : "", House = p.House != null ? p.House.HouseNumber : null })
            .ToListAsync();
        return rows.ToDictionary(r => r.Id, r => new PayInfo(r.TenantId, $"{r.First} {r.Last}".Trim(), r.House));
    }

    private static string Cut(string? v, int max) => string.IsNullOrEmpty(v) ? "" : (v.Length <= max ? v : v[..max]);
    private string? _who;
    private string WhoName() => _who ?? User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.Role) ?? "staff";

    [NonAction]
    public async Task OnActionExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context, Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        _who = await StaffName.ResolveAsync(_db, User);
        await next();
    }


    public record ReviewRequest(string Type, string Source, string Reference, decimal? Amount, string Detail, DateTime At,
        Guid? PaymentId, string? CheckoutRequestId, string? Note, string? MpesaAnswer);

    /// <summary>Marks a finding as dealt with (checked and fine, or no money was involved). It moves to the Reconciled list.</summary>
    [HttpPost("review")]
    public async Task<IActionResult> Review([FromBody] ReviewRequest r)
    {
        if (r == null || !IssueTypes.Contains(r.Type) || string.IsNullOrWhiteSpace(r.Reference))
            return BadRequest(new { success = false, message = "That finding is not valid." });
        var key = KeyOf(r.Type, Cut(r.Reference, 200), r.PaymentId, string.IsNullOrWhiteSpace(r.CheckoutRequestId) ? null : r.CheckoutRequestId);
        if (await _db.ReconciliationReviews.AnyAsync(x => x.IssueKey == key))
            return Ok(new { success = true, message = "Already reconciled." });
        var info = r.PaymentId.HasValue ? (await PaymentInfoAsync(new List<Guid> { r.PaymentId.Value })).GetValueOrDefault(r.PaymentId.Value) : null;
        _db.ReconciliationReviews.Add(new ReconciliationReview
        {
            IssueKey = key, IssueType = r.Type, Source = Cut(r.Source, 60), Reference = Cut(r.Reference, 200), Amount = r.Amount,
            Detail = Cut(r.Detail, 1000), IssueAt = r.At, PaymentId = r.PaymentId, CheckoutRequestId = Cut(r.CheckoutRequestId, 100) is { Length: > 0 } c ? c : null,
            TenantId = info?.TenantId, TenantName = info?.Name, House = info?.House,
            Outcome = "Verified", Note = Cut(r.Note, 500) is { Length: > 0 } n ? n : null, MpesaAnswer = Cut(r.MpesaAnswer, 500) is { Length: > 0 } a ? a : null,
            ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), ReviewedByName = WhoName(), ReviewedByRole = User.FindFirstValue(ClaimTypes.Role),
            ReviewedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        await _events.RecordAsync("Payments", "ReconciliationVerified", "Info",
            $"Reconciliation finding {r.Type} ({Cut(r.Reference, 60)}) was marked as reconciled.",
            User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Role));
        return Ok(new { success = true, message = "Moved to Reconciled." });
    }

    public record ReverseRequest(Guid PaymentId, string Type, string Source, string Reference, decimal? Amount, string Detail, DateTime At,
        string? CheckoutRequestId, string Reason);

    /// <summary>
    /// Asks for a payment record to be reversed (records only, no money moves). The request goes through the "Reversal"
    /// approval sequence set up in Setups, and the reversal runs only when the last approver approves.
    /// </summary>
    [HttpPost("reverse")]
    [EnableRateLimiting("payment")]
    public async Task<IActionResult> Reverse([FromBody] ReverseRequest r)
    {
        var reason = (r?.Reason ?? "").Trim();
        if (r == null || reason.Length < 10 || reason.Length > 500)
            return BadRequest(new { success = false, message = "Give a reason of at least 10 characters." });
        var key = KeyOf(r.Type, Cut(r.Reference, 200), r.PaymentId, string.IsNullOrWhiteSpace(r.CheckoutRequestId) ? null : r.CheckoutRequestId);
        if (await _db.ReconciliationReviews.AnyAsync(x => x.IssueKey == key))
            return Conflict(new { success = false, message = "This finding has already been reconciled." });
        if (await _db.ReversalRequests.AnyAsync(x => x.IssueKey == key && x.Status == "AwaitingApproval"))
            return Conflict(new { success = false, message = "A reversal for this finding is already waiting for approval." });

        var first = await _db.ApprovalSequenceSteps.Where(s => s.Module == "Reversal").OrderBy(s => s.StepOrder).FirstOrDefaultAsync();
        if (first == null)
            return BadRequest(new { success = false, message = "No approval sequence has been set up for Reversals. Add one in Setups, under Approvals." });

        // refuse early if the payment cannot be reversed, or M-Pesa says it was paid, so nobody is asked to approve it for nothing
        var pre = await _reversal.PrecheckAsync(r.PaymentId, r.Type, r.Source);
        if (!pre.Ok) return StatusCode(pre.HttpStatus, new { success = false, message = pre.Message });

        var info = (await PaymentInfoAsync(new List<Guid> { r.PaymentId })).GetValueOrDefault(r.PaymentId);
        var req = new ReversalRequest
        {
            PaymentId = r.PaymentId, IssueKey = key, IssueType = r.Type, Source = Cut(r.Source, 60), Reference = Cut(r.Reference, 200), Amount = r.Amount,
            Detail = Cut(r.Detail, 1000), IssueAt = r.At, CheckoutRequestId = string.IsNullOrWhiteSpace(r.CheckoutRequestId) ? null : Cut(r.CheckoutRequestId, 100),
            TenantId = info?.TenantId, TenantName = info?.Name, House = info?.House, Reason = reason,
            CurrentStepOrder = first.StepOrder, RequestedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier), RequestedByName = WhoName()
        };
        _db.ReversalRequests.Add(req);
        await _db.SaveChangesAsync();
        await _events.RecordAsync("Payments", "ReversalRequested", "Info",
            $"Reversal of payment {Cut(r.Reference, 60)} requested. Reason: {Cut(reason, 200)}", User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Role));
        try
        {
            await _notify.SendToUserAsync(first.ApproverId.ToString(),
                $"A payment reversal for {info?.Name} (KES {r.Amount:N2}) needs your approval. Requested by {WhoName()}.", "payment", "Reversal", req.Id.ToString());
        }
        catch { }
        return Ok(new { success = true, message = "Reversal requested. It will run once the approvers in the sequence have approved it.", id = req.Id });
    }


    public record ApplyRequest(string CheckoutRequestId, string ReceiptNumber, decimal? Amount);

    /// <summary>
    /// Records a payment that M-Pesa took but the app never received the callback for. The accountant gives the receipt number from the
    /// M-Pesa message; the app asks M-Pesa again to confirm the prompt was paid, works out the amount that was prompted, and then applies it
    /// through the same path a live callback uses, so the payment, receipts, rewards and balances are all recorded normally.
    /// </summary>
    [HttpPost("apply")]
    [EnableRateLimiting("payment")]
    public async Task<IActionResult> Apply([FromBody] ApplyRequest r)
    {
        var cid = (r.CheckoutRequestId ?? "").Trim();
        var receipt = (r.ReceiptNumber ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(cid) || cid.Length > 100 || !cid.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
            return BadRequest(new { success = false, message = "That request id is not valid." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(receipt, "^[A-Z0-9]{10}$"))
            return BadRequest(new { success = false, message = "The M-Pesa receipt number is 10 letters and digits, for example UKAQG1LYYX." });

        // what was actually prompted, and for which kind of payment
        decimal expected; string phone = "";
        var rent = await _db.PaymentCheckoutAttempts.IgnoreQueryFilters().Include(a => a.Payment).FirstOrDefaultAsync(a => a.CheckoutRequestId == cid);
        if (rent != null)
        {
            if (rent.AttemptStatus == "Success" || rent.Payment == null) return BadRequest(new { success = false, message = "This prompt is already recorded as paid." });
            var p = rent.Payment;
            var rentPart = p.RequestedDistributionAmount ?? (p.Balance > 0 ? p.Balance : p.Amount);
            expected = rentPart + (rent.ServiceChargeAmount ?? p.ServiceChargeAmount ?? 0m);
            phone = p.PhoneNumber ?? "";
        }
        else
        {
            var cmp = await _db.ComplaintCheckoutAttempts.FirstOrDefaultAsync(a => a.CheckoutRequestId == cid);
            if (cmp != null)
            {
                if (cmp.AttemptStatus == "Success") return BadRequest(new { success = false, message = "This prompt is already recorded as paid." });
                expected = cmp.Amount + (cmp.ServiceChargeAmount ?? 0m); phone = cmp.PhoneNumber;
            }
            else
            {
                var vac = await _db.VacateCheckoutAttempts.FirstOrDefaultAsync(a => a.CheckoutRequestId == cid);
                if (vac == null) return NotFound(new { success = false, message = "No payment prompt with that id." });
                if (vac.AttemptStatus == "Success") return BadRequest(new { success = false, message = "This prompt is already recorded as paid." });
                var st = await _db.VacateSettlements.FirstOrDefaultAsync(s => s.Id == vac.VacateSettlementId);
                if (st == null) return BadRequest(new { success = false, message = "The settlement for this prompt was not found." });
                expected = st.Amount + (vac.ServiceChargeAmount ?? 0m);
            }
        }
        if (r.Amount.HasValue && Math.Abs(r.Amount.Value - expected) > 0.01m)
            return BadRequest(new { success = false, message = $"That amount does not match what was prompted (KES {expected:N2})." });

        // the receipt must not already belong to another payment
        var used = await _db.PaymentApplications.AnyAsync(a => a.MpesaReceiptNumber == receipt)
                || await _db.Payments.IgnoreQueryFilters().AnyAsync(p => p.MpesaReceiptNumber == receipt)
                || await _db.ComplaintCheckoutAttempts.AnyAsync(a => a.MpesaReceiptNumber == receipt)
                || await _db.VacateCheckoutAttempts.AnyAsync(a => a.MpesaReceiptNumber == receipt);
        if (used) return BadRequest(new { success = false, message = "That receipt number is already recorded on another payment." });

        // M-Pesa itself must confirm the prompt was paid
        var q = await _mpesa.QuerySTKStatusAsync(cid);
        if (q.ResultCode != "0")
            return BadRequest(new { success = false, message = string.IsNullOrEmpty(q.ResultCode) ? "M-Pesa has no confirmed answer for this prompt, so it cannot be recorded yet." : $"M-Pesa says this prompt was not paid ({q.ResultDesc})." });

        var cb = new MpesaCallback
        {
            Body = new MpesaCallbackBody
            {
                stkCallback = new MpesaStkCallback
                {
                    CheckoutRequestID = cid, ResultCode = 0, ResultDesc = "Recorded by staff after M-Pesa confirmed it was paid",
                    CallbackMetadata = new MpesaCallbackMetadata
                    {
                        Item = new List<MpesaCallbackItem>
                        {
                            new() { Name = "Amount", Value = System.Text.Json.JsonSerializer.SerializeToElement(expected) },
                            new() { Name = "MpesaReceiptNumber", Value = System.Text.Json.JsonSerializer.SerializeToElement(receipt) },
                            new() { Name = "TransactionDate", Value = System.Text.Json.JsonSerializer.SerializeToElement(DateTime.UtcNow.AddHours(3).ToString("yyyyMMddHHmmss")) },
                            new() { Name = "PhoneNumber", Value = System.Text.Json.JsonSerializer.SerializeToElement(phone) }
                        }
                    }
                }
            }
        };
        _db.MpesaCallbackLogs.Add(new MpesaCallbackLog
        {
            CheckoutRequestId = cid, ResultCode = 0, RawJson = System.Text.Json.JsonSerializer.Serialize(cb),
            Applied = false, Error = Cut($"Recorded by {WhoName()} with receipt {receipt}", 500)
        });
        await _db.SaveChangesAsync();

        await _payments.ProcessCallbackAsync(cb);

        var log = await _db.MpesaCallbackLogs.Where(l => l.CheckoutRequestId == cid && !l.Applied).OrderByDescending(l => l.ReceivedAt).FirstOrDefaultAsync();
        if (log != null) { log.Applied = true; log.AppliedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }

        await _events.RecordAsync("Payments", "ReconciliationApply", "Warning",
            $"{WhoName()} recorded M-Pesa receipt {receipt} (KES {expected:N2}) against prompt {cid} after M-Pesa confirmed it was paid.",
            User.FindFirstValue(ClaimTypes.NameIdentifier), User.FindFirstValue(ClaimTypes.Role));
        return Ok(new { success = true, message = $"Payment of KES {expected:N2} recorded with receipt {receipt}.", amount = expected, receipt });
    }

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
        if (r.ResultDesc == "RATE_LIMITED")
            return Ok(new { success = true, answered = false, paid = false, resultCode = "", message = "M-Pesa is limiting how often it can be asked. Wait a minute and check again." });
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
