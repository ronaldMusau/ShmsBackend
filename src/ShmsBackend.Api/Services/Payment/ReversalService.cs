using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Notifications;
using ShmsBackend.Api.Services.Security;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;
using ShmsBackend.Data.Models.Entities.Portal;
using PaymentRow = ShmsBackend.Data.Models.Entities.Portal.Payment;

namespace ShmsBackend.Api.Services.Payment;

public record ReversalOutcome(bool Ok, int HttpStatus, string Message, decimal Reversed = 0m, int Rows = 0, decimal Points = 0m, bool NeedsManualCheck = false);

public interface IReversalService
{
    /// <summary>Checks, before anyone is asked to approve, that the payment can be reversed and that M-Pesa does not say it was paid.</summary>
    Task<ReversalOutcome> PrecheckAsync(Guid paymentId, string type, string source);
    /// <summary>Undoes the recorded payment (records only, no money moves). Called when the last approver approves.</summary>
    Task<ReversalOutcome> ExecuteAsync(ReversalRequest req, string approvedBy, string? actorUserId, string? actorRole);
}

public class ReversalService : IReversalService
{
    private readonly ShmsDbContext _db;
    private readonly IMpesaService _mpesa;
    private readonly ISecurityEventService _events;
    private readonly INotificationService _notify;
    private readonly ILogger<ReversalService> _log;

    public ReversalService(ShmsDbContext db, IMpesaService mpesa, ISecurityEventService events, INotificationService notify, ILogger<ReversalService> log)
    { _db = db; _mpesa = mpesa; _events = events; _notify = notify; _log = log; }

    private static string Cut(string? v, int max) => string.IsNullOrEmpty(v) ? "" : (v.Length <= max ? v : v[..max]);

    private record Guard(string? Error, int Status, string MpesaNote);

    private async Task<Guard> AskMpesaAsync(List<string> checkouts)
    {
        var answered = 0;
        foreach (var c in checkouts)
        {
            var q = await _mpesa.QuerySTKStatusAsync(c);
            if (q.ResultCode == "0")
                return new($"M-Pesa says prompt {c} was paid, so this payment is not wrong and cannot be reversed. Mark it as reconciled instead.", 409, "");
            if (!string.IsNullOrEmpty(q.ResultCode)) answered++;
        }
        var note = checkouts.Count == 0 ? "No M-Pesa prompt is on record for this payment."
            : answered == checkouts.Count ? "M-Pesa confirmed none of its prompts was paid."
            : "M-Pesa gave no final answer (the prompts may be too old to look up).";
        return new(null, 200, note);
    }

    private static (List<string> receipts, List<string> checkouts) Ids(PaymentRow target)
    {
        var receipts = target.Applications.Select(a => a.MpesaReceiptNumber).Append(target.MpesaReceiptNumber ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        var checkouts = target.Applications.Select(a => a.CheckoutRequestId ?? "").Append(target.CheckoutRequestId ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        return (receipts, checkouts);
    }

    private ReversalOutcome? Validate(PaymentRow? target, string type, string source)
    {
        if (source != "Rent payment" || (type != "PaidNoReceipt" && type != "AmountMismatch"))
            return new(false, 400, "Only rent payments flagged for a missing receipt or an amount that does not add up can be reversed.");
        if (target == null) return new(false, 404, "That payment was not found.");
        if (target.PaymentMethod != PaymentMethod.Mpesa || !string.IsNullOrEmpty(target.RedemptionReference))
            return new(false, 400, "Only payments made purely by M-Pesa can be reversed here.");
        if (target.AmountPaid <= 0 && !target.Applications.Any())
            return new(false, 400, "Nothing is recorded as paid on this payment.");
        return null;
    }

    public async Task<ReversalOutcome> PrecheckAsync(Guid paymentId, string type, string source)
    {
        var target = await _db.Payments.AsNoTracking().Include(p => p.Applications).FirstOrDefaultAsync(p => p.Id == paymentId && !p.IsDeleted);
        var bad = Validate(target, type, source);
        if (bad != null) return bad;
        var (_, checkouts) = Ids(target!);
        var g = await AskMpesaAsync(checkouts);
        return g.Error != null ? new(false, g.Status, g.Error) : new(true, 200, g.MpesaNote);
    }

    public async Task<ReversalOutcome> ExecuteAsync(ReversalRequest req, string approvedBy, string? actorUserId, string? actorRole)
    {
        var target = await _db.Payments.Include(p => p.Applications).Include(p => p.Tenant).Include(p => p.House)
            .FirstOrDefaultAsync(p => p.Id == req.PaymentId && !p.IsDeleted);
        var bad = Validate(target, req.IssueType, req.Source);
        if (bad != null) return bad;
        target = target!;

        var (receipts, checkouts) = Ids(target);
        // ask M-Pesa again: if any prompt really was paid, the record is right and must not be reversed
        var g = await AskMpesaAsync(checkouts);
        if (g.Error != null) return new(false, g.Status, g.Error);
        var mpesaNote = g.MpesaNote;

        var apps = receipts.Count == 0 ? new List<PaymentApplication>()
            : await _db.PaymentApplications.Where(a => receipts.Contains(a.MpesaReceiptNumber) && a.Payment!.TenantId == target.TenantId).ToListAsync();
        var rowIds = apps.Select(a => a.PaymentId).Append(target.Id).Distinct().ToList();
        var rows = await _db.Payments.Include(p => p.Applications).Where(p => rowIds.Contains(p.Id) && !p.IsDeleted).ToListAsync();
        var removedCheckouts = apps.Select(a => a.CheckoutRequestId ?? "").Concat(checkouts).Where(x => x != "").Distinct().ToList();

        decimal reversed = 0m;
        foreach (var row in rows)
        {
            var removed = apps.Where(a => a.PaymentId == row.Id).ToList();
            var removedIds = removed.Select(a => a.Id).ToHashSet();
            var remaining = row.Applications.Where(a => !removedIds.Contains(a.Id)).ToList();
            var newPaid = row.Id == target.Id
                ? (row.CreditApplied ?? 0m) + remaining.Sum(a => a.AmountApplied)
                : Math.Max(0m, row.AmountPaid - removed.Sum(a => a.AmountApplied));
            newPaid = Math.Min(newPaid, row.AmountPaid);
            reversed += row.AmountPaid - newPaid;
            _db.PaymentApplications.RemoveRange(removed);
            row.AmountPaid = newPaid;
            row.Balance = Math.Max(0m, row.Amount - newPaid);
            row.PaymentStatus = newPaid <= 0 ? (row.DueDate < DateTime.UtcNow ? PaymentTransactionStatus.Overdue : PaymentTransactionStatus.Pending)
                : row.Balance <= 0 ? PaymentTransactionStatus.Paid : PaymentTransactionStatus.PartiallyPaid;
            if (remaining.Count == 0)
            {
                row.MpesaReceiptNumber = null;
                if (newPaid <= 0) row.PaidAt = null;
            }
            else row.MpesaReceiptNumber = remaining.OrderBy(a => a.AppliedAt).Last().MpesaReceiptNumber;
            row.UpdatedAt = DateTime.UtcNow;
        }

        var attempts = await _db.PaymentCheckoutAttempts.IgnoreQueryFilters()
            .Where(a => rowIds.Contains(a.PaymentId) && a.AttemptStatus == "Success" && removedCheckouts.Contains(a.CheckoutRequestId)).ToListAsync();
        foreach (var a in attempts) { a.AttemptStatus = "Reversed"; a.ResultDesc = "Reversed by reconciliation"; }

        decimal pointsReversed = 0m;
        var earned = await _db.RewardTransactions.Where(t => t.RelatedPaymentId == target.Id && t.TransactionType == "Earned").ToListAsync();
        foreach (var t in earned)
        {
            if (target.Tenant != null) target.Tenant.PointsBalance = Math.Max(0m, target.Tenant.PointsBalance - t.Points);
            pointsReversed += t.Points;
            t.TransactionType = "Reversed";
        }

        var nowUtc = DateTime.UtcNow;
        if (target.House != null && target.Month == nowUtc.Month && target.Year == nowUtc.Year)
        {
            target.House.PaymentStatus = target.AmountPaid > 0 ? PaymentStatus.PartiallyPaid
                : target.DueDate < nowUtc ? PaymentStatus.Overdue : PaymentStatus.NotPaid;
            target.House.UpdatedAt = nowUtc;
        }

        var summary = $"Reversed KES {reversed:N2} across {rows.Count} payment record(s)" + (pointsReversed > 0 ? $" and {pointsReversed:N0} reward points" : "") + ". " + mpesaNote;
        if (!await _db.ReconciliationReviews.AnyAsync(x => x.IssueKey == req.IssueKey))
            _db.ReconciliationReviews.Add(new ReconciliationReview
            {
                IssueKey = req.IssueKey, IssueType = req.IssueType, Source = Cut(req.Source, 60), Reference = Cut(req.Reference, 200), Amount = req.Amount,
                Detail = Cut(req.Detail, 1000), IssueAt = req.IssueAt, PaymentId = target.Id, CheckoutRequestId = string.IsNullOrEmpty(req.CheckoutRequestId) ? null : Cut(req.CheckoutRequestId, 100),
                TenantId = target.TenantId, TenantName = req.TenantName, House = req.House,
                Outcome = "Reversed", Note = Cut(req.Reason, 500), MpesaAnswer = Cut(summary + " Approved by " + approvedBy + ".", 500),
                ReviewedByUserId = req.RequestedByUserId, ReviewedByName = req.RequestedByName, ReviewedByRole = actorRole, ReviewedAt = nowUtc
            });
        await _db.SaveChangesAsync();

        await _events.RecordAsync("Payments", "PaymentReversed", "Warning",
            $"Payment {target.Month:00}/{target.Year} (KES {reversed:N2}) was reversed as not received. Approved by {approvedBy}. Reason: {Cut(req.Reason, 200)}",
            actorUserId, actorRole);

        var houseLabel = req.House ?? "";
        try
        {
            await _notify.SendForcedToUserAsync(target.TenantId.ToString(),
                $"A payment of KES {reversed:N2} recorded for House {houseLabel} ({target.Month:00}/{target.Year}) has been reversed because M-Pesa shows the money was not received. Your balance has been corrected. If you did pay, please contact the office with your M-Pesa message.",
                "payment", "PaymentReversed", target.Id.ToString());
            if (target.LandlordId.HasValue)
                await _notify.SendToUserAsync(target.LandlordId.Value.ToString(),
                    $"A rent payment of KES {reversed:N2} recorded for House {houseLabel} ({target.Month:00}/{target.Year}) was reversed: M-Pesa shows it was not received.",
                    "payment", "PaymentReversed", target.Id.ToString());
            await _notify.SendToRolesAsync(new[] { NotificationAudience.SuperAdmin, NotificationAudience.Admin, NotificationAudience.Manager, NotificationAudience.Accountant, NotificationAudience.Secretary },
                $"Payment reversed: KES {reversed:N2} for {req.TenantName} (House {houseLabel}, {target.Month:00}/{target.Year}). Requested by {req.RequestedByName}, approved by {approvedBy}. Reason: {Cut(req.Reason, 200)}"
                + (target.IsInitialPayment ? " This was the tenant's initial payment, so check their account setup by hand." : ""),
                "payment", "PaymentReversed", target.Id.ToString());
        }
        catch (Exception ex) { _log.LogWarning(ex, "Reversal notifications failed"); }

        return new(true, 200, summary, reversed, rows.Count, pointsReversed, target.IsInitialPayment);
    }
}
