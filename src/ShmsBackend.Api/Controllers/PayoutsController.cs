using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Payment;
using ShmsBackend.Api.Services.Security;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Controllers;

/// <summary>
/// Payouts (M-Pesa Business to Customer): refunds and similar money going out.
/// Staff request a payout; it goes through the "Payout" approval sequence (Setups, Approvals); only after the last approver approves is anything sent to M-Pesa.
/// Per-payout and daily limits apply, and M-Pesa's answer comes back on the callbacks below.
/// </summary>
[ApiController]
[Route("api/payouts")]
public class PayoutsController : ControllerBase, Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter
{
    private static readonly string[] Purposes = { "AdvanceRefund", "DepositRefund", "Other" };

    private readonly ShmsDbContext _db;
    private readonly IMpesaB2CService _b2c;
    private readonly MpesaB2CConfig _cfg;
    private readonly ISecurityEventService _events;
    private readonly INotificationService _notify;
    private readonly ILogger<PayoutsController> _log;
    private readonly IPayoutService _payouts;
    private readonly IStepUpService _stepUp;

    public PayoutsController(ShmsDbContext db, IMpesaB2CService b2c, IOptions<MpesaB2CConfig> cfg, ISecurityEventService events,
        INotificationService notify, ILogger<PayoutsController> log, IPayoutService payouts, IStepUpService stepUp)
    { _db = db; _b2c = b2c; _cfg = cfg.Value; _events = events; _notify = notify; _log = log; _payouts = payouts; _stepUp = stepUp; }

    private string? Uid => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private string? _who;
    private string Who => _who ?? User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.Role) ?? "staff";

    public async Task OnActionExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context, Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        _who = await StaffName.ResolveAsync(_db, User);
        await next();
    }

    private static string Mask(string p) => p.Length >= 7 ? p[..4] + "****" + p[^3..] : "****";

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    public async Task<IActionResult> List([FromQuery] string? status)
    {
        var q = _db.B2CPayouts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
        var rows = await q.OrderByDescending(p => p.RequestedAt).Take(300).ToListAsync();
        var since = DateTime.UtcNow.Date;
        var today = await _db.B2CPayouts.AsNoTracking()
            .Where(p => p.RequestedAt >= since && p.Status != "Rejected" && p.Status != "Failed").SumAsync(p => (decimal?)p.Amount) ?? 0m;
        var ok = _b2c.IsConfigured(out var problem);
        return Ok(new
        {
            success = true, configured = ok, configProblem = ok ? null : problem,
            maxPerPayout = _cfg.MaxPerPayout, maxPerDay = _cfg.MaxPerDay, usedToday = today,
            currentUserId = Uid,
            payouts = rows.Select(p => new
            {
                p.Id, p.Purpose, p.TenantId, p.TenantName, phone = Mask(p.ReceiverPhone), p.Amount, p.Reason, p.Status,
                p.RequestedByUserId, p.RequestedByName, p.RequestedAt, p.CurrentStepOrder, p.SentByName, p.ApprovedByName, p.ApprovedAt, p.RejectReason,
                p.ResultDesc, p.MpesaReceipt, p.SubmittedAt, p.CompletedAt
            })
        });
    }

    public record CreateRequest(Guid? TenantId, string? Phone, decimal Amount, string Purpose, string Reason);

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    [EnableRateLimiting("payment")]
    public async Task<IActionResult> Create([FromBody] CreateRequest r)
    {
        if (r == null || !Purposes.Contains(r.Purpose)) return BadRequest(new { success = false, message = "Choose what the payout is for." });
        var reason = (r.Reason ?? "").Trim();
        if (reason.Length < 10 || reason.Length > 300) return BadRequest(new { success = false, message = "Give a reason of 10 to 300 characters." });
        if (r.Amount < 10 || r.Amount != decimal.Truncate(r.Amount)) return BadRequest(new { success = false, message = "The amount must be a whole number of shillings, at least 10 (M-Pesa minimum)." });
        if (r.Amount > _cfg.MaxPerPayout) return BadRequest(new { success = false, message = $"One payout can be at most KES {_cfg.MaxPerPayout:N0}." });

        string? tenantName = null; var phoneRaw = r.Phone;
        if (r.TenantId.HasValue)
        {
            var t = await _db.Tenants.AsNoTracking().Where(x => x.Id == r.TenantId.Value)
                .Select(x => new { x.FirstName, x.LastName, x.PhoneNumber }).FirstOrDefaultAsync();
            if (t == null) return NotFound(new { success = false, message = "That tenant was not found." });
            tenantName = $"{t.FirstName} {t.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(phoneRaw)) phoneRaw = t.PhoneNumber;
        }
        var digits = new string((phoneRaw ?? "").Where(char.IsDigit).ToArray());
        var phone = digits.StartsWith("0") ? "254" + digits[1..] : digits.StartsWith("254") ? digits : "254" + digits;
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^254(7|1)\d{8}$"))
            return BadRequest(new { success = false, message = "Enter a valid Safaricom number, for example 0712345678." });

        var first = await _db.ApprovalSequenceSteps.Where(x => x.Module == "Payout").OrderBy(x => x.StepOrder).FirstOrDefaultAsync();
        if (first == null)
            return BadRequest(new { success = false, message = "No approval sequence has been set up for Payouts. Add one in Setups, under Approvals." });

        var p = new B2CPayout
        {
            CurrentStepOrder = first.StepOrder,
            Purpose = r.Purpose, TenantId = r.TenantId, TenantName = tenantName, ReceiverPhone = phone, Amount = r.Amount, Reason = reason,
            RequestedByUserId = Uid, RequestedByName = Who
        };
        _db.B2CPayouts.Add(p);
        await _db.SaveChangesAsync();
        await _events.RecordAsync("Payments", "PayoutRequested", "Info", $"Payout of KES {p.Amount:N0} to {Mask(phone)} requested ({p.Purpose}).", Uid, User.FindFirstValue(ClaimTypes.Role));
        try
        {
            await _notify.SendToUserAsync(first.ApproverId.ToString(),
                $"A payout of KES {p.Amount:N0}{(tenantName != null ? " to " + tenantName : "")} needs your approval. Requested by {Who}.", "payment", "Payout", p.Id.ToString());
        }
        catch { }
        return Ok(new { success = true, message = "Payout requested. It is sent once the approvers in the sequence have approved it.", id = p.Id });
    }

    public record PayRequest(string Code);

    /// <summary>
    /// Pays a fully approved payout. Only the person who raised it can do this, and only with a current code from their
    /// authenticator app. This is the moment money leaves.
    /// </summary>
    [HttpPost("{id:guid}/pay")]
    [Authorize(Roles = "SuperAdmin,Admin,Accountant")]
    [EnableRateLimiting("payment")]
    public async Task<IActionResult> Pay(Guid id, [FromBody] PayRequest r)
    {
        var p = await _db.B2CPayouts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound(new { success = false, message = "Payout not found." });
        if (p.Status != "Approved") return Conflict(new { success = false, message = "This payout is not approved, or has already been sent." });
        if (string.IsNullOrEmpty(p.RequestedByUserId) || p.RequestedByUserId != Uid)
            return StatusCode(403, new { success = false, message = "Only the person who raised this payout can pay it." });

        var step = await _stepUp.VerifyAsync(Uid, r?.Code, $"payout {id}");
        if (!step.Ok) return BadRequest(new { success = false, message = step.Message });

        var res = await _payouts.SendApprovedAsync(id, Uid!, Who, User.FindFirstValue(ClaimTypes.Role));
        if (!res.Ok) return StatusCode(res.HttpStatus, new { success = false, message = res.Message });
        return Ok(new { success = true, message = res.Message });
    }

    // ---- M-Pesa callbacks (no login: protected by the secret in the URL; the path contains /mpesa/callback/ so the app's body-encryption and terms checks let it through) ----

    [HttpPost("mpesa/callback/result/{secret}")]
    [AllowAnonymous]
    [EnableRateLimiting("callback")]
    public async Task<IActionResult> Result(string secret, [FromBody] JsonElement body)
    {
        if (!_b2c.IsValidCallbackSecret(secret)) return NotFound();
        try
        {
            var r = body.GetProperty("Result");
            var orig = r.TryGetProperty("OriginatorConversationID", out var o) ? o.GetString() : null;
            var conv = r.TryGetProperty("ConversationID", out var c) ? c.GetString() : null;
            var code = r.TryGetProperty("ResultCode", out var rc) ? rc.GetRawText().Trim('"') : "";
            var desc = r.TryGetProperty("ResultDesc", out var rd) ? rd.GetString() : null;
            string? receipt = r.TryGetProperty("TransactionID", out var tid) ? tid.GetString() : null;
            if (r.TryGetProperty("ResultParameters", out var rp) && rp.TryGetProperty("ResultParameter", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var it in arr.EnumerateArray())
                    if (it.TryGetProperty("Key", out var k) && k.GetString() == "TransactionReceipt" && it.TryGetProperty("Value", out var v)) receipt = v.ToString();

            var p = Guid.TryParse(orig, out var gid) ? await _db.B2CPayouts.FirstOrDefaultAsync(x => x.Id == gid) : null;
            if (p == null && !string.IsNullOrEmpty(conv)) p = await _db.B2CPayouts.FirstOrDefaultAsync(x => x.ConversationId == conv);
            if (p == null) { _log.LogWarning("B2C result for an unknown payout"); return Ok(new { ResultCode = 0, ResultDesc = "Accepted" }); }
            if (p.Status == "Succeeded") return Ok(new { ResultCode = 0, ResultDesc = "Accepted" });   // repeated callback

            var ok = code == "0";
            p.Status = ok ? "Succeeded" : "Failed";
            p.ResultCode = code; p.ResultDesc = desc; p.MpesaReceipt = ok ? receipt : p.MpesaReceipt; p.CompletedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(conv)) p.ConversationId = conv;
            await _db.SaveChangesAsync();
            await _events.RecordAsync("Payments", ok ? "PayoutSucceeded" : "PayoutFailed", ok ? "Info" : "Warning",
                $"Payout {p.Id} of KES {p.Amount:N0} to {Mask(p.ReceiverPhone)}: {(ok ? "paid, receipt " + receipt : "failed, " + desc)}", null, null);
            try
            {
                if (p.TenantId.HasValue && ok)
                    await _notify.SendForcedToUserAsync(p.TenantId.Value.ToString(), $"KES {p.Amount:N0} has been sent to your M-Pesa. Receipt {receipt}.", "payment", "Payout", p.Id.ToString());
                await _notify.SendToRolesAsync(new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Accountant },
                    ok ? $"Payout of KES {p.Amount:N0}{(p.TenantName != null ? " to " + p.TenantName : "")} was paid. Receipt {receipt}."
                       : $"Payout of KES {p.Amount:N0}{(p.TenantName != null ? " to " + p.TenantName : "")} FAILED: {desc}", "payment", "Payout", p.Id.ToString());
            }
            catch { }
        }
        catch (Exception ex) { _log.LogError(ex, "Could not process a B2C result callback"); }
        return Ok(new { ResultCode = 0, ResultDesc = "Accepted" });
    }

    [HttpPost("mpesa/callback/timeout/{secret}")]
    [AllowAnonymous]
    [EnableRateLimiting("callback")]
    public async Task<IActionResult> Timeout(string secret, [FromBody] JsonElement body)
    {
        if (!_b2c.IsValidCallbackSecret(secret)) return NotFound();
        try
        {
            var r = body.GetProperty("Result");
            var orig = r.TryGetProperty("OriginatorConversationID", out var o) ? o.GetString() : null;
            if (Guid.TryParse(orig, out var gid))
            {
                // the outcome is unknown, so it is flagged for a person to verify rather than assumed failed
                await _db.B2CPayouts.Where(x => x.Id == gid && x.Status == "Submitted")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "TimedOut").SetProperty(x => x.ResultDesc, "M-Pesa timed out. Check the outcome with M-Pesa before paying again."));
                await _notify.SendToRolesAsync(new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Accountant },
                    "A payout timed out at M-Pesa. Check the outcome with M-Pesa before paying again.", "payment", "Payout", gid.ToString());
            }
        }
        catch (Exception ex) { _log.LogError(ex, "Could not process a B2C timeout callback"); }
        return Ok(new { ResultCode = 0, ResultDesc = "Accepted" });
    }
}
