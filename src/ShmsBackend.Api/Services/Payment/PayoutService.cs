using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShmsBackend.Api.Services.Security;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Payment;

public record PayoutSendOutcome(bool Ok, int HttpStatus, string Message);

public interface IPayoutService
{
    /// <summary>Sends a fully approved payout to M-Pesa. Called when the person who raised it pays it. Safe against double sending.</summary>
    Task<PayoutSendOutcome> SendApprovedAsync(Guid payoutId, string senderUserId, string senderName, string? role);
}

public class PayoutService : IPayoutService
{
    private readonly ShmsDbContext _db;
    private readonly IMpesaB2CService _b2c;
    private readonly MpesaB2CConfig _cfg;
    private readonly ISecurityEventService _events;

    public PayoutService(ShmsDbContext db, IMpesaB2CService b2c, IOptions<MpesaB2CConfig> cfg, ISecurityEventService events)
    { _db = db; _b2c = b2c; _cfg = cfg.Value; _events = events; }

    private static string Mask(string p) => p.Length >= 7 ? p[..4] + "****" + p[^3..] : "****";

    public async Task<PayoutSendOutcome> SendApprovedAsync(Guid id, string approverUserId, string approverName, string? role)
    {
        if (!_b2c.IsConfigured(out var problem)) return new(false, 400, "Payouts are not set up yet. " + problem);
        var p = await _db.B2CPayouts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return new(false, 404, "Payout not found.");
        if (p.Status != "Approved") return new(false, 409, "This payout is not approved, or has already been sent.");
        if (p.Amount > _cfg.MaxPerPayout) return new(false, 400, $"One payout can be at most KES {_cfg.MaxPerPayout:N0}.");

        var since = DateTime.UtcNow.Date;
        var sentToday = await _db.B2CPayouts.Where(x => x.ApprovedAt >= since && x.Id != id && (x.Status == "Submitting" || x.Status == "Submitted" || x.Status == "Succeeded" || x.Status == "TimedOut"))
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        if (sentToday + p.Amount > _cfg.MaxPerDay)
            return new(false, 400, $"This would pass the daily limit of KES {_cfg.MaxPerDay:N0} (KES {sentToday:N0} already approved today).");

        // claim it: only one caller can move it out of Approved, so it can never be sent twice
        var claimed = await _db.B2CPayouts.Where(x => x.Id == id && x.Status == "Approved")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Submitting").SetProperty(x => x.SentByUserId, approverUserId)
                .SetProperty(x => x.SentByName, approverName));
        if (claimed == 0) return new(false, 409, "This payout was just handled by someone else.");

        var res = await _b2c.SendAsync(p.Id, p.ReceiverPhone, p.Amount, $"{p.Purpose} refund");
        var status = res.Accepted ? "Submitted" : res.Code == "ERROR" ? "TimedOut" : "Failed";
        await _db.B2CPayouts.Where(x => x.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, status).SetProperty(x => x.ConversationId, res.ConversationId)
            .SetProperty(x => x.SubmittedAt, DateTime.UtcNow).SetProperty(x => x.ResultCode, res.Code).SetProperty(x => x.ResultDesc, res.Message)
            .SetProperty(x => x.CompletedAt, status == "Failed" ? DateTime.UtcNow : (DateTime?)null));
        await _events.RecordAsync("Payments", "PayoutApproved", status == "Failed" ? "Warning" : "Info",
            $"Payout {id} of KES {p.Amount:N0} to {Mask(p.ReceiverPhone)} sent by {approverName}: {status}.", approverUserId, role);
        if (status == "Failed") return new(false, 400, "M-Pesa did not accept the payout: " + res.Message);
        if (status == "TimedOut") return new(true, 200, "M-Pesa did not confirm. Check with M-Pesa before trying again so the customer is not paid twice.");
        return new(true, 200, "Sent to M-Pesa. The result will appear on the Payouts page in a moment.");
    }
}
