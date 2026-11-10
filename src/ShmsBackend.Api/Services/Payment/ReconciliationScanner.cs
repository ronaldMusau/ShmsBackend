using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Controllers;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities.Portal;

namespace ShmsBackend.Api.Services.Payment;

/// <summary>
/// The reconciliation check itself: looks at every payment made in a period (rent rows created or paid in it, plus
/// complaint and vacate-settlement payments) and every M-Pesa prompt, and returns the findings still open. Read-only.
/// Used by the Reconciliation page and by the watcher that tells the accountants when a new finding appears.
/// </summary>
public class ReconciliationScanner
{
    private const decimal Tolerance = 0.01m;
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LookupWindow = TimeSpan.FromDays(3);

    private readonly ShmsDbContext _db;
    public ReconciliationScanner(ShmsDbContext db) { _db = db; }

    public record ScanResult(List<ReconciliationController.Issue> Open, int PaymentsChecked, int PromptsChecked, int OlderUnresolved);

    private record Att(string Source, string Checkout, string Status, string? Desc, string? Receipt, DateTime? ProcessedAt, DateTime CreatedAt, Guid? PaymentId, bool PaymentSettled, bool PaymentDeleted);
    private record PayInfo(Guid TenantId, string Name, string? House);

    public async Task<ScanResult> ScanAsync(DateTime start, DateTime end)
    {
        var now = DateTime.UtcNow;
        var issues = new List<ReconciliationController.Issue>();
        var olderUnresolved = 0;

        // ---- rent / deposit payments (M-Pesa) ----
        var pays = await _db.Payments.AsNoTracking()
            .Where(p => !p.IsDeleted && ((p.CreatedAt >= start && p.CreatedAt < end) || (p.PaidAt >= start && p.PaidAt < end)))
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
                issues.Add(new ReconciliationController.Issue("PaidNoReceipt", "High", "Rent payment", label, p.AmountPaid,
                    "Marked as paid by M-Pesa but there is no M-Pesa receipt number on it.", p.PaidAt ?? p.CreatedAt, p.Id, p.CheckoutRequestId));

            if (p.PaymentStatus != PaymentTransactionStatus.Cancelled && Math.Abs(p.Amount - p.AmountPaid - p.Balance) > Tolerance && p.Balance != 0)
                issues.Add(new ReconciliationController.Issue("AmountMismatch", "High", "Rent payment", label, p.Amount,
                    $"Amount {p.Amount:N2} minus paid {p.AmountPaid:N2} should leave {(p.Amount - p.AmountPaid):N2}, but the balance is {p.Balance:N2}.", p.CreatedAt, p.Id, p.CheckoutRequestId));

            // Credit from an earlier overpayment and points redemptions legitimately make up part of what was paid, so they are allowed for.
            var explained = p.AppliedSum + (p.CreditApplied ?? 0m);
            if (p.AppCount > 0 && p.PaymentMethod == PaymentMethod.Mpesa && Math.Abs(explained - p.AmountPaid) > Tolerance && paidish.Contains(p.PaymentStatus) && p.RedemptionReference == null)
                issues.Add(new ReconciliationController.Issue("AmountMismatch", "High", "Rent payment", label, p.AmountPaid,
                    $"M-Pesa receipts applied add up to {p.AppliedSum:N2}{((p.CreditApplied ?? 0m) > 0 ? $" plus credit {p.CreditApplied:N2}" : "")}, but the payment says {p.AmountPaid:N2} was paid.", p.PaidAt ?? p.CreatedAt, p.Id, p.CheckoutRequestId));

            if (p.AmountPaid > p.Amount + Tolerance && p.PaymentStatus != PaymentTransactionStatus.Cancelled)
                issues.Add(new ReconciliationController.Issue("AmountMismatch", "Medium", "Rent payment", label, p.AmountPaid,
                    $"Paid {p.AmountPaid:N2} is more than the amount due {p.Amount:N2}.", p.PaidAt ?? p.CreatedAt, p.Id, p.CheckoutRequestId));
        }

        // a receipt number that appears on payments of different tenants
        var receipts = await _db.PaymentApplications.AsNoTracking()
            .Where(a => a.AppliedAt >= start && a.AppliedAt < end && a.MpesaReceiptNumber != "")
            .Select(a => new { a.MpesaReceiptNumber, a.PaymentId, TenantId = a.Payment!.TenantId, a.AmountApplied, a.AppliedAt })
            .ToListAsync();
        foreach (var g in receipts.GroupBy(r => r.MpesaReceiptNumber).Where(g => g.Select(x => x.TenantId).Distinct().Count() > 1))
            issues.Add(new ReconciliationController.Issue("DuplicateReceipt", "High", "Rent payment", g.Key, g.Sum(x => x.AmountApplied),
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
                issues.Add(new ReconciliationController.Issue("StuckPrompt", "Medium", a.Source, a.Checkout, null, "The payment prompt is still waiting for a result after more than 10 minutes.", a.CreatedAt, a.PaymentId, a.Checkout));
            else if (a.Status == "Failed" && a.ProcessedAt == null && (a.Desc ?? "").StartsWith("Timed out", StringComparison.OrdinalIgnoreCase) && age > StuckAfter)
            {
                // M-Pesa can only be asked about recent prompts; older ones are counted but not listed one by one.
                if (age <= LookupWindow)
                    issues.Add(new ReconciliationController.Issue("NoResult", "Medium", a.Source, a.Checkout, null, "No result came back from M-Pesa. The customer may or may not have paid: check with M-Pesa.", a.CreatedAt, a.PaymentId, a.Checkout));
                else olderUnresolved++;
            }
            if (a.Status == "Success" && a.PaymentDeleted)
                issues.Add(new ReconciliationController.Issue("PaymentRemoved", "Medium", a.Source, a.Checkout, null, "M-Pesa money was received for this prompt, but the payment record was later deleted.", a.ProcessedAt ?? a.CreatedAt, a.PaymentId, a.Checkout));
            else if (a.Status == "Success" && !a.PaymentSettled)
                issues.Add(new ReconciliationController.Issue("SuccessNotApplied", "High", a.Source, a.Checkout, null, "M-Pesa reported success but nothing is recorded as paid on the payment.", a.ProcessedAt ?? a.CreatedAt, a.PaymentId, a.Checkout));
            if (a.Status == "Success" && a.Source != "Rent payment" && string.IsNullOrWhiteSpace(a.Receipt))
                issues.Add(new ReconciliationController.Issue("PaidNoReceipt", "High", a.Source, a.Checkout, null, "Marked successful but has no M-Pesa receipt number.", a.ProcessedAt ?? a.CreatedAt, a.PaymentId, a.Checkout));
        }
        foreach (var g in rent.Concat(cmp).Concat(vac).Where(x => !string.IsNullOrEmpty(x.Checkout)).GroupBy(x => x.Checkout).Where(g => g.Count(x => x.Status == "Success") > 1))
            issues.Add(new ReconciliationController.Issue("DuplicateReceipt", "High", g.First().Source, g.Key, null, $"{g.Count(x => x.Status == "Success")} successful attempts share the same M-Pesa request id.", g.Max(x => x.CreatedAt), null, g.Key));

        // who and which house each finding belongs to, so the page can link to the tenant and the payment
        var payIds = issues.Where(i => i.PaymentId.HasValue).Select(i => i.PaymentId!.Value).Distinct().ToList();
        var info = await PaymentInfoAsync(payIds);
        issues = issues.Select(i =>
        {
            var pi = i.PaymentId.HasValue && info.TryGetValue(i.PaymentId.Value, out var x) ? x : null;
            return i with { TenantId = pi?.TenantId, TenantName = pi?.Name, House = pi?.House, Key = ReconciliationController.KeyOf(i.Type, i.Reference, i.PaymentId, i.CheckoutRequestId) };
        }).ToList();

        // findings staff have already dealt with move to the Reconciled list; a finding whose amount has since changed reopens
        var keys = issues.Select(i => i.Key).Distinct().ToList();
        var reviewed = await _db.ReconciliationReviews.AsNoTracking()
            .Where(r => keys.Contains(r.IssueKey)).ToDictionaryAsync(r => r.IssueKey);
        var open = issues.Where(i => !reviewed.TryGetValue(i.Key, out var rv) || rv.Amount != i.Amount).ToList();

        // every payment made counts as checked: rent payments, plus complaint and vacate-settlement payments that went through
        var checkedPayments = pays.Count + cmp.Count(x => x.Status == "Success") + vac.Count(x => x.Status == "Success");
        return new ScanResult(open, checkedPayments, rent.Count + cmp.Count + vac.Count, olderUnresolved);
    }

    private async Task<Dictionary<Guid, PayInfo>> PaymentInfoAsync(List<Guid> ids)
    {
        if (ids.Count == 0) return new();
        var rows = await _db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.TenantId, First = p.Tenant != null ? p.Tenant.FirstName : "", Last = p.Tenant != null ? p.Tenant.LastName : "", House = p.House != null ? p.House.HouseNumber : null })
            .ToListAsync();
        return rows.ToDictionary(r => r.Id, r => new PayInfo(r.TenantId, $"{r.First} {r.Last}".Trim(), r.House));
    }
}
