using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Services.Security;

public record StepUpResult(bool Ok, string Message);

public interface IStepUpService
{
    /// <summary>
    /// Confirms it is really the signed-in person: checks the 6-digit code from their authenticator app.
    /// Used before money goes out. A code works once; five wrong tries lock this check for 15 minutes.
    /// </summary>
    Task<StepUpResult> VerifyAsync(string? userId, string? code, string purpose);
}

public class StepUpService : IStepUpService
{
    private const int MaxWrong = 5;
    private readonly ShmsDbContext _db;
    private readonly IAuthenticatorService _totp;
    private readonly ISecretProtector _protector;
    private readonly IMemoryCache _cache;
    private readonly ISecurityEventService _events;

    public StepUpService(ShmsDbContext db, IAuthenticatorService totp, ISecretProtector protector, IMemoryCache cache, ISecurityEventService events)
    { _db = db; _totp = totp; _protector = protector; _cache = cache; _events = events; }

    public async Task<StepUpResult> VerifyAsync(string? userId, string? code, string purpose)
    {
        if (!Guid.TryParse(userId, out var id)) return new(false, "Could not tell who you are. Sign in again.");
        var key = "stepup:" + id;
        var wrong = _cache.TryGetValue<int>(key, out var n) ? n : 0;
        if (wrong >= MaxWrong) return new(false, "Too many wrong codes. Try again in 15 minutes.");

        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Id == id && a.IsActive && !a.IsDeleted);
        var secret = admin == null ? null : _protector.Unprotect(admin.TotpSecretEnc);
        if (admin == null || secret == null)
            return new(false, "Set up your authenticator app first (Security), then try again.");

        var step = _totp.VerifyCode(secret, code ?? "", admin.TotpLastStep);
        if (step == null)
        {
            _cache.Set(key, wrong + 1, TimeSpan.FromMinutes(15));
            await _events.RecordAsync("Security", "StepUpFailed", "Warning", $"A wrong authenticator code was entered to confirm: {purpose}.", userId, null);
            return new(false, "That code is not right. Use the current 6-digit code from your authenticator app.");
        }
        admin.TotpLastStep = step.Value;      // the code cannot be used again
        await _db.SaveChangesAsync();
        _cache.Remove(key);
        return new(true, "");
    }
}
