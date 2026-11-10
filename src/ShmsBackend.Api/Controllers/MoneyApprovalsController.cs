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

namespace ShmsBackend.Api.Controllers;

/// <summary>
/// Approvals for money-related requests: payment reversals (module "Reversal") and payouts (module "Payout").
/// Each module has an ordered list of approvers set up in Setups. Only the approver whose turn it is can decide.
/// When the last approver approves a reversal it runs (records only). When the last approver approves a payout, the
/// person who raised it is told, and they pay it with their authenticator code.
/// </summary>
[ApiController]
[Route("api/money-approvals")]
[Authorize(Roles = "SuperAdmin,Admin,Secretary,Manager,Accountant")]
public class MoneyApprovalsController : ControllerBase, Microsoft.AspNetCore.Mvc.Filters.IAsyncActionFilter
{
    private readonly ShmsDbContext _db;
    private readonly IReversalService _reversal;
    private readonly ISecurityEventService _events;
    private readonly INotificationService _notify;

    public MoneyApprovalsController(ShmsDbContext db, IReversalService reversal, ISecurityEventService events, INotificationService notify)
    { _db = db; _reversal = reversal; _events = events; _notify = notify; }

    private string? Uid => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private Guid MyId => Guid.TryParse(Uid, out var g) ? g : Guid.Empty;
    private string? _who;
    private string Who => _who ?? User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue(ClaimTypes.Role) ?? "staff";

    public async Task OnActionExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context, Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
    {
        _who = await StaffName.ResolveAsync(_db, User);
        await next();
    }

    private static string Mask(string p) => p.Length >= 7 ? p[..4] + "****" + p[^3..] : "****";

    public record Item(string Kind, Guid Id, string Title, decimal? Amount, string? TenantName, string? Reason, string? Detail,
        string? RequestedByName, DateTime RequestedAt, int StepOrder, int StepCount, string Status, object? Trail, bool Mine = false);

    private async Task<Dictionary<Guid, List<MoneyApprovalAction>>> TrailsAsync(string kind, IEnumerable<Guid> ids)
    {
        var list = ids.ToList();
        var acts = await _db.MoneyApprovalActions.AsNoTracking().Where(a => a.Kind == kind && list.Contains(a.SubjectId)).OrderBy(a => a.ActionedAt).ToListAsync();
        return acts.GroupBy(a => a.SubjectId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>Requests waiting for the signed-in person's decision.</summary>
    [HttpGet("queue")]
    public async Task<IActionResult> Queue()
    {
        var me = MyId;
        var mySteps = await _db.ApprovalSequenceSteps.AsNoTracking().Where(s => (s.Module == "Reversal" || s.Module == "Payout") && s.ApproverId == me).ToListAsync();
        var counts = await _db.ApprovalSequenceSteps.AsNoTracking().Where(s => s.Module == "Reversal" || s.Module == "Payout")
            .GroupBy(s => s.Module).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        var items = new List<Item>();

        var revSteps = mySteps.Where(s => s.Module == "Reversal").Select(s => s.StepOrder).ToList();
        if (revSteps.Count > 0)
        {
            var revs = await _db.ReversalRequests.AsNoTracking().Where(r => r.Status == "AwaitingApproval" && revSteps.Contains(r.CurrentStepOrder)).OrderBy(r => r.RequestedAt).ToListAsync();
            var tr = await TrailsAsync("Reversal", revs.Select(r => r.Id));
            items.AddRange(revs.Select(r => new Item("Reversal", r.Id, $"Reverse payment {r.Reference}", r.Amount, r.TenantName, r.Reason, r.Detail,
                r.RequestedByName, r.RequestedAt, r.CurrentStepOrder, counts.GetValueOrDefault("Reversal"), r.Status,
                tr.GetValueOrDefault(r.Id)?.Select(a => new { a.StepOrder, a.ApproverName, a.Decision, a.Notes, a.ActionedAt }), r.RequestedByUserId == Uid)));
        }
        var payRows = mySteps.Where(s => s.Module == "Payout").Select(s => s.StepOrder).ToList();
        if (payRows.Count > 0)
        {
            var pays = await _db.B2CPayouts.AsNoTracking().Where(p => p.Status == "PendingApproval" && payRows.Contains(p.CurrentStepOrder)).OrderBy(p => p.RequestedAt).ToListAsync();
            var tr = await TrailsAsync("Payout", pays.Select(p => p.Id));
            items.AddRange(pays.Select(p => new Item("Payout", p.Id, $"Pay out KES {p.Amount:N0} to {p.TenantName ?? Mask(p.ReceiverPhone)}", p.Amount, p.TenantName, p.Reason,
                $"{p.Purpose} · {Mask(p.ReceiverPhone)}", p.RequestedByName, p.RequestedAt, p.CurrentStepOrder, counts.GetValueOrDefault("Payout"), p.Status,
                tr.GetValueOrDefault(p.Id)?.Select(a => new { a.StepOrder, a.ApproverName, a.Decision, a.Notes, a.ActionedAt }), p.RequestedByUserId == Uid)));
        }
        return Ok(new { success = true, data = items.OrderBy(i => i.RequestedAt) });
    }

    /// <summary>Decisions the signed-in person has made.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History()
    {
        var me = MyId;
        var acts = await _db.MoneyApprovalActions.AsNoTracking().Where(a => a.ApproverId == me).OrderByDescending(a => a.ActionedAt).Take(100).ToListAsync();
        var revIds = acts.Where(a => a.Kind == "Reversal").Select(a => a.SubjectId).ToList();
        var payIds = acts.Where(a => a.Kind == "Payout").Select(a => a.SubjectId).ToList();
        var revs = await _db.ReversalRequests.AsNoTracking().Where(r => revIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        var pays = await _db.B2CPayouts.AsNoTracking().Where(p => payIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var data = acts.Select(a =>
        {
            revs.TryGetValue(a.SubjectId, out var r); pays.TryGetValue(a.SubjectId, out var p);
            return new
            {
                a.Kind, id = a.SubjectId, a.Decision, a.Notes, a.ActionedAt, a.StepOrder,
                title = a.Kind == "Reversal" ? $"Reverse payment {r?.Reference}" : $"Pay out KES {p?.Amount:N0} to {p?.TenantName ?? "a customer"}",
                amount = a.Kind == "Reversal" ? r?.Amount : p?.Amount, tenantName = a.Kind == "Reversal" ? r?.TenantName : p?.TenantName,
                status = a.Kind == "Reversal" ? r?.Status : p?.Status
            };
        });
        return Ok(new { success = true, data });
    }

    public record DecideRequest(bool Approve, string? Notes);

    [HttpPost("{kind}/{id:guid}/decide")]
    [EnableRateLimiting("payment")]
    public async Task<IActionResult> Decide(string kind, Guid id, [FromBody] DecideRequest r)
    {
        if (kind != "Reversal" && kind != "Payout") return BadRequest(new { success = false, message = "Unknown request type." });
        var notes = (r?.Notes ?? "").Trim();
        if (r != null && !r.Approve && notes.Length < 5) return BadRequest(new { success = false, message = "Give a reason of at least 5 characters when rejecting." });
        if (notes.Length > 300) notes = notes[..300];
        var me = MyId;

        string? requesterId, tenantName; int step; decimal? amount;
        ReversalRequest? rev = null; B2CPayout? pay = null;
        if (kind == "Reversal")
        {
            rev = await _db.ReversalRequests.FirstOrDefaultAsync(x => x.Id == id);
            if (rev == null) return NotFound(new { success = false, message = "Request not found." });
            if (rev.Status != "AwaitingApproval") return Conflict(new { success = false, message = "This request is no longer waiting for approval." });
            requesterId = rev.RequestedByUserId; step = rev.CurrentStepOrder; tenantName = rev.TenantName; amount = rev.Amount;
        }
        else
        {
            pay = await _db.B2CPayouts.FirstOrDefaultAsync(x => x.Id == id);
            if (pay == null) return NotFound(new { success = false, message = "Request not found." });
            if (pay.Status != "PendingApproval") return Conflict(new { success = false, message = "This request is no longer waiting for approval." });
            requesterId = pay.RequestedByUserId; step = pay.CurrentStepOrder; tenantName = pay.TenantName; amount = pay.Amount;
        }

        var steps = await _db.ApprovalSequenceSteps.Where(s => s.Module == kind).OrderBy(s => s.StepOrder).ToListAsync();
        var current = steps.FirstOrDefault(s => s.StepOrder == step);
        if (current == null || current.ApproverId != me)
            return StatusCode(403, new { success = false, message = "It is not your turn to approve this request." });
        if (!string.IsNullOrEmpty(requesterId) && requesterId == Uid)
            return StatusCode(403, new { success = false, message = "You raised this request, so someone else must approve it." });

        _db.MoneyApprovalActions.Add(new MoneyApprovalAction
        {
            Kind = kind, SubjectId = id, StepOrder = step, ApproverId = me, ApproverName = Who,
            Decision = r!.Approve ? "Approved" : "Rejected", Notes = string.IsNullOrEmpty(notes) ? null : notes
        });

        var label = kind == "Reversal" ? "payment reversal" : "payout";
        var subject = $"{label} of KES {amount:N0}{(tenantName != null ? " for " + tenantName : "")}";

        if (!r.Approve)
        {
            if (rev != null) { rev.Status = "Rejected"; rev.DecidedAt = DateTime.UtcNow; rev.ResultMessage = notes; }
            if (pay != null) { pay.Status = "Rejected"; pay.RejectReason = notes; pay.ApprovedByUserId = Uid; pay.ApprovedByName = Who; pay.ApprovedAt = DateTime.UtcNow; }
            await _db.SaveChangesAsync();
            await _events.RecordAsync("Payments", kind + "Rejected", "Info", $"The {subject} was rejected by {Who}: {notes}", Uid, User.FindFirstValue(ClaimTypes.Role));
            await TryNotify(requesterId, $"Your {subject} was rejected by {Who}: {notes}", kind, id);
            return Ok(new { success = true, message = "Rejected." });
        }

        var next = steps.FirstOrDefault(s => s.StepOrder > step);
        if (next != null)
        {
            if (rev != null) rev.CurrentStepOrder = next.StepOrder;
            if (pay != null) pay.CurrentStepOrder = next.StepOrder;
            await _db.SaveChangesAsync();
            await TryNotify(next.ApproverId.ToString(), $"A {subject} needs your approval.", kind, id);
            return Ok(new { success = true, message = "Approved. It has gone to the next approver." });
        }

        // last approver
        if (pay != null)
        {
            pay.Status = "Approved"; pay.ApprovedByUserId = Uid; pay.ApprovedByName = Who; pay.ApprovedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _events.RecordAsync("Payments", "PayoutApprovedAll", "Info", $"The {subject} was fully approved (last step by {Who}). It is now waiting to be paid by {pay.RequestedByName}.", Uid, User.FindFirstValue(ClaimTypes.Role));
            await TryNotify(requesterId, $"Your {subject} is fully approved. Open Payouts, press Pay and enter your authenticator code to send it.", kind, id);
            return Ok(new { success = true, message = "Approved. The person who raised it has been told to pay it with their authenticator code." });
        }

        var approvers = string.Join(", ", (await _db.MoneyApprovalActions.AsNoTracking().Where(a => a.Kind == "Reversal" && a.SubjectId == id && a.Decision == "Approved").OrderBy(a => a.StepOrder).Select(a => a.ApproverName).ToListAsync()).Append(Who).Distinct());
        var outcome = await _reversal.ExecuteAsync(rev!, approvers, Uid, User.FindFirstValue(ClaimTypes.Role));
        rev!.DecidedAt = DateTime.UtcNow; rev.ResultMessage = outcome.Message.Length > 500 ? outcome.Message[..500] : outcome.Message;
        rev.Status = outcome.Ok ? "Executed" : "Blocked";
        await _db.SaveChangesAsync();
        if (!outcome.Ok)
        {
            await TryNotify(requesterId, $"Your {subject} was approved but could not be done: {outcome.Message}", kind, id);
            return StatusCode(outcome.HttpStatus, new { success = false, message = outcome.Message });
        }
        await TryNotify(requesterId, $"Your {subject} was approved and has been done.", kind, id);
        return Ok(new { success = true, message = outcome.Message });
    }

    private async Task TryNotify(string? userId, string message, string kind, Guid id)
    {
        if (string.IsNullOrEmpty(userId)) return;
        try { await _notify.SendToUserAsync(userId, message, "payment", kind, id.ToString()); } catch { }
    }
}
