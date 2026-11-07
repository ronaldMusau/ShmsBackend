using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Security;

public enum OtpResult { Ok, Wrong, Expired, TooManyTries, NotFound, TooSoon, ResendLimit, AccountLocked }

public record OtpIssued(Guid ChallengeId, int ResendAfterSeconds, int ValidMinutes, string MaskedEmail);
public record OtpVerify(OtpResult Result, Admin? Admin, int TriesLeft);
public record OtpResend(OtpResult Result, OtpIssued? Issued, int WaitSeconds);

public interface ILoginOtpService
{
    Task<OtpIssued> IssueAsync(Admin admin, string? ip);
    Task<OtpVerify> VerifyAsync(Guid challengeId, string code, string? ip);
    Task<OtpResend> ResendAsync(Guid challengeId, string? ip);
}

/// <summary>
/// Emailed one-time code for staff sign-in. Rules: 6 digits from a secure random generator, stored only as an
/// HMAC bound to the account and challenge, compared in constant time, single use, short life, 5 tries
/// (resending does not reset them), 30-second resend cool-off, and repeated burned challenges lock the account.
/// All limits come from configuration section "LoginOtp" (defaults below), none are hard-coded elsewhere.
/// </summary>
public class LoginOtpService : ILoginOtpService
{
    private readonly ShmsDbContext _db;
    private readonly IEmailService _email;
    private readonly ISecurityEventService _events;
    private readonly IConfiguration _cfg;
    private readonly ILogger<LoginOtpService> _log;
    private readonly byte[] _key;

    public LoginOtpService(ShmsDbContext db, IEmailService email, ISecurityEventService events, IConfiguration cfg, ILogger<LoginOtpService> log)
    {
        _db = db; _email = email; _events = events; _cfg = cfg; _log = log;
        // Separate secret for codes. If none is configured, derive one from the JWT secret with a purpose label so
        // a leaked code hash never reveals the JWT key and the two keys are never the same.
        var pepper = cfg["Security:OtpPepper"];
        var root = !string.IsNullOrWhiteSpace(pepper) ? pepper : cfg["JwtOptions:Secret"] ?? "";
        if (root.Length < 32) throw new InvalidOperationException("Security:OtpPepper (or the JWT secret) must be at least 32 characters.");
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(root), 32, info: Encoding.UTF8.GetBytes("shms-login-otp-v1"));
    }

    private int Cfg(string name, int def) => int.TryParse(_cfg[$"LoginOtp:{name}"], out var v) && v > 0 ? v : def;
    private int ExpiryMinutes => Cfg("ExpiryMinutes", 5);
    private int ResendSeconds => Cfg("ResendSeconds", 30);
    private int MaxAttempts => Cfg("MaxAttempts", 5);
    private int MaxSends => Cfg("MaxSends", 5);
    private int MaxLifeMinutes => Cfg("MaxLifeMinutes", 15);
    private int BurnsBeforeLock => Cfg("BurnsBeforeLock", 3);

    private string Hash(Guid adminId, Guid challengeId, string code)
    {
        var data = Encoding.UTF8.GetBytes($"{adminId:N}|{challengeId:N}|{code}");
        return Convert.ToHexString(HMACSHA256.HashData(_key, data));
    }

    private static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***" + (at >= 0 ? email[at..] : "");
        return email[0] + new string('*', Math.Min(6, at - 1)) + email[at..];
    }

    public async Task<OtpIssued> IssueAsync(Admin admin, string? ip)
    {
        // Only one live challenge per account: a new password check replaces the old code.
        var live = await _db.LoginChallenges.Where(c => c.AdminId == admin.Id && c.UsedAt == null && !c.Burned).ToListAsync();
        foreach (var c in live) c.Burned = true;

        var now = DateTime.UtcNow;
        var ch = new LoginChallenge
        {
            AdminId = admin.Id, IpAddress = ip, CreatedAt = now,
            ExpiresAt = now.AddMinutes(ExpiryMinutes), ResendAvailableAt = now.AddSeconds(ResendSeconds)
        };
        var code = NewCode();
        ch.CodeHash = Hash(admin.Id, ch.Id, code);
        _db.LoginChallenges.Add(ch);
        await _db.SaveChangesAsync();

        await _email.SendLoginCodeEmailAsync(admin.Email, admin.FirstName, code, ExpiryMinutes);
        return new OtpIssued(ch.Id, ResendSeconds, ExpiryMinutes, Mask(admin.Email));
    }

    public async Task<OtpVerify> VerifyAsync(Guid challengeId, string code, string? ip)
    {
        var ch = await _db.LoginChallenges.FirstOrDefaultAsync(c => c.Id == challengeId);
        if (ch == null || ch.UsedAt != null || ch.Burned) return new OtpVerify(OtpResult.NotFound, null, 0);

        var now = DateTime.UtcNow;
        if (now > ch.ExpiresAt || now > ch.CreatedAt.AddMinutes(MaxLifeMinutes))
        { ch.Burned = true; await _db.SaveChangesAsync(); return new OtpVerify(OtpResult.Expired, null, 0); }

        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Id == ch.AdminId);
        if (admin == null || !admin.IsActive || admin.IsDeleted || admin.IsLockedOut)
        { ch.Burned = true; await _db.SaveChangesAsync(); return new OtpVerify(OtpResult.AccountLocked, null, 0); }

        code = (code ?? "").Trim();
        var good = code.Length == 6 && code.All(char.IsAsciiDigit)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Hash(ch.AdminId, ch.Id, code)), Encoding.ASCII.GetBytes(ch.CodeHash));

        if (good)
        {
            ch.UsedAt = now;
            await _db.SaveChangesAsync();
            return new OtpVerify(OtpResult.Ok, admin, MaxAttempts);
        }

        ch.Attempts++;
        var left = Math.Max(0, MaxAttempts - ch.Attempts);
        if (left == 0)
        {
            ch.Burned = true;
            await _db.SaveChangesAsync();
            await _events.RecordAsync("Auth", "OtpTooManyTries", "High",
                $"Sign-in code for a staff account was entered wrongly {MaxAttempts} times; the code was cancelled.",
                admin.Id.ToString(), "Staff", ip);

            // Several cancelled codes in an hour: someone has the password and keeps guessing. Lock the account.
            var since = now.AddHours(-1);
            var burns = await _db.LoginChallenges.CountAsync(c => c.AdminId == admin.Id && c.Burned && c.Attempts >= MaxAttempts && c.CreatedAt >= since);
            if (burns >= BurnsBeforeLock && admin is not SuperAdmin)
            {
                admin.IsLockedOut = true;
                await _db.SaveChangesAsync();
                await _events.RecordAsync("Auth", "OtpAccountLocked", "Critical",
                    $"Staff account locked after {burns} cancelled sign-in codes in one hour. It needs a password reset.",
                    admin.Id.ToString(), "Staff", ip);
                try { await _email.SendAccountLockedEmailAsync(admin.Email, admin.FirstName); } catch { /* alert already recorded */ }
                return new OtpVerify(OtpResult.AccountLocked, null, 0);
            }
            return new OtpVerify(OtpResult.TooManyTries, null, 0);
        }
        await _db.SaveChangesAsync();
        return new OtpVerify(OtpResult.Wrong, null, left);
    }

    public async Task<OtpResend> ResendAsync(Guid challengeId, string? ip)
    {
        var ch = await _db.LoginChallenges.FirstOrDefaultAsync(c => c.Id == challengeId);
        if (ch == null || ch.UsedAt != null || ch.Burned) return new OtpResend(OtpResult.NotFound, null, 0);

        var now = DateTime.UtcNow;
        if (now > ch.CreatedAt.AddMinutes(MaxLifeMinutes)) { ch.Burned = true; await _db.SaveChangesAsync(); return new OtpResend(OtpResult.Expired, null, 0); }
        if (now < ch.ResendAvailableAt) return new OtpResend(OtpResult.TooSoon, null, (int)Math.Ceiling((ch.ResendAvailableAt - now).TotalSeconds));
        if (ch.SendCount >= MaxSends) return new OtpResend(OtpResult.ResendLimit, null, 0);

        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Id == ch.AdminId);
        if (admin == null || !admin.IsActive || admin.IsDeleted || admin.IsLockedOut)
        { ch.Burned = true; await _db.SaveChangesAsync(); return new OtpResend(OtpResult.AccountLocked, null, 0); }

        var code = NewCode();
        ch.CodeHash = Hash(ch.AdminId, ch.Id, code);   // the old code stops working immediately
        ch.SendCount++;
        ch.ExpiresAt = now.AddMinutes(ExpiryMinutes);
        ch.ResendAvailableAt = now.AddSeconds(ResendSeconds);
        await _db.SaveChangesAsync();                  // Attempts are NOT reset, so resending cannot buy more guesses

        await _email.SendLoginCodeEmailAsync(admin.Email, admin.FirstName, code, ExpiryMinutes);
        return new OtpResend(OtpResult.Ok, new OtpIssued(ch.Id, ResendSeconds, ExpiryMinutes, Mask(admin.Email)), 0);
    }
}
