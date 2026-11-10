using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Controllers;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Payment;

/// <summary>
/// Fixes the reconciliation findings that can be fixed safely, then records them as reconciled by "System (auto)".
///  1. A success callback was received and stored but never applied: the stored callback is applied again (the same path a live
///     callback takes, which ignores duplicates).
///  2. A prompt is stuck or has no result, and M-Pesa says it was NOT paid: the prompt is closed as failed.
/// Never changes a payment on a guess: when M-Pesa says "paid" but no callback was stored there is no receipt or amount to apply,
/// so that stays open for the accountant. Only prompts younger than M-Pesa's 3-day lookup window are asked about.
/// </summary>
public class ReconciliationHealer
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(3);

    private readonly ShmsDbContext _db;
    private readonly IMpesaService _mpesa;
    private readonly IPaymentService _payments;
    private readonly ILogger<ReconciliationHealer> _log;

    public ReconciliationHealer(ShmsDbContext db, IMpesaService mpesa, IPaymentService payments, ILogger<ReconciliationHealer> log)
    { _db = db; _mpesa = mpesa; _payments = payments; _log = log; }

    /// <summary>Findings where M-Pesa says the prompt was paid but the app has no callback to apply: a person must record them.</summary>
    public List<ReconciliationController.Issue> PaidAtMpesa { get; } = new();

    /// <summary>Returns the keys of the findings that were fixed and closed.</summary>
    public async Task<HashSet<string>> HealAsync(IEnumerable<ReconciliationController.Issue> open, int maxMpesaQueries = 10)
    {
        var healed = new HashSet<string>();
        var queries = 0;
        var now = DateTime.UtcNow;

        foreach (var i in open.Where(i => i.Type is "SuccessNotApplied" or "StuckPrompt" or "NoResult" && !string.IsNullOrEmpty(i.CheckoutRequestId)))
        {
            if (now - i.At > Window) continue;
            var cid = i.CheckoutRequestId!;
            try
            {
                // 1. replay a stored success callback that was never applied
                // a callback received in the last 2 minutes may still be mid-processing by the live callback; leave it alone
                var settled = now.AddMinutes(-2);
                var stored = await _db.MpesaCallbackLogs
                    .Where(l => l.CheckoutRequestId == cid && l.ResultCode == 0 && !l.Applied && l.ReceivedAt < settled)
                    .OrderByDescending(l => l.ReceivedAt).FirstOrDefaultAsync();
                if (stored != null)
                {
                    var cb = JsonSerializer.Deserialize<MpesaCallback>(stored.RawJson);
                    if (cb != null)
                    {
                        await _payments.ProcessCallbackAsync(cb);
                        var details = _mpesa.ExtractPaymentDetails(cb);
                        stored.Applied = true; stored.AppliedAt = DateTime.UtcNow; stored.Error = null;
                        await _db.SaveChangesAsync();
                        await RecordAsync(i, "Applied the stored M-Pesa callback again" + (string.IsNullOrEmpty(details.MpesaReceiptNumber) ? "." : $" (receipt {details.MpesaReceiptNumber})."), "Paid");
                        healed.Add(i.Key);
                        continue;
                    }
                }

                // 2. M-Pesa says the prompt was not paid: close it
                if (i.Type is "StuckPrompt" or "NoResult" && queries < maxMpesaQueries)
                {
                    queries++;
                    if (queries > 1) await Task.Delay(1500); // M-Pesa limits how fast it can be asked
                    var r = await _mpesa.QuerySTKStatusAsync(cid);
                    if (r.ResultDesc == "RATE_LIMITED") { _log.LogWarning("M-Pesa is rate limiting status checks; the rest wait for the next run"); queries = maxMpesaQueries; continue; }
                    // 4999 means M-Pesa is still processing it: that is no answer yet, never a failure
                    if (r.ResultCode == "0") PaidAtMpesa.Add(i);
                    if (!string.IsNullOrEmpty(r.ResultCode) && r.ResultCode != "0" && r.ResultCode != "4999")
                    {
                        var closed = await CloseAttemptAsync(cid, r.ResultCode, r.ResultDesc);
                        if (closed)
                        {
                            await RecordAsync(i, $"M-Pesa confirmed this prompt was not paid ({r.ResultDesc}); it was closed as failed.", $"Not paid: {r.ResultDesc}");
                            healed.Add(i.Key);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Auto-reconcile failed for {Checkout}", cid);
                _db.ChangeTracker.Clear();
            }
        }
        return healed;
    }

    private async Task<bool> CloseAttemptAsync(string cid, string code, string desc)
    {
        var status = code == "1032" ? "Cancelled" : "Failed";
        var now = DateTime.UtcNow;
        var rent = await _db.PaymentCheckoutAttempts.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.CheckoutRequestId == cid);
        if (rent != null && rent.AttemptStatus != "Success") { rent.AttemptStatus = status; rent.ResultCode = code; rent.ResultDesc = desc; rent.ProcessedAt = now; await _db.SaveChangesAsync(); return true; }
        var cmp = await _db.ComplaintCheckoutAttempts.FirstOrDefaultAsync(a => a.CheckoutRequestId == cid);
        if (cmp != null && cmp.AttemptStatus != "Success") { cmp.AttemptStatus = status; cmp.ResultCode = code; cmp.ResultDesc = desc; cmp.ProcessedAt = now; await _db.SaveChangesAsync(); return true; }
        var vac = await _db.VacateCheckoutAttempts.FirstOrDefaultAsync(a => a.CheckoutRequestId == cid);
        if (vac != null && vac.AttemptStatus != "Success") { vac.AttemptStatus = status; vac.ResultCode = code; vac.ResultDesc = desc; vac.ProcessedAt = now; await _db.SaveChangesAsync(); return true; }
        return false;
    }

    private async Task RecordAsync(ReconciliationController.Issue i, string note, string mpesaAnswer)
    {
        if (await _db.ReconciliationReviews.AnyAsync(r => r.IssueKey == i.Key && r.Amount == i.Amount)) return;
        string Cut(string? v, int max) => string.IsNullOrEmpty(v) ? "" : (v.Length <= max ? v : v[..max]);
        _db.ReconciliationReviews.Add(new ReconciliationReview
        {
            IssueKey = Cut(i.Key, 200), IssueType = i.Type, Source = Cut(i.Source, 60), Reference = Cut(i.Reference, 200), Amount = i.Amount,
            Detail = Cut(i.Detail, 1000), IssueAt = i.At, PaymentId = i.PaymentId, TenantId = i.TenantId, TenantName = Cut(i.TenantName, 150) is { Length: > 0 } n ? n : null,
            House = Cut(i.House, 60) is { Length: > 0 } h ? h : null, CheckoutRequestId = Cut(i.CheckoutRequestId, 100) is { Length: > 0 } c ? c : null,
            Outcome = "Verified", Note = "Auto-reconciled: " + Cut(note, 960), MpesaAnswer = Cut(mpesaAnswer, 500),
            ReviewedByName = "System (auto)", ReviewedByRole = "System", ReviewedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
