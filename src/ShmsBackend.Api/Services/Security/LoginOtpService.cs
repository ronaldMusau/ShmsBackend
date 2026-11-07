using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Email;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Security;

public enum OtpResult { Ok, Wrong, Expired, TooManyTries, NotFound, TooSoon, ResendLimit, AccountLocked }

public record OtpIssued(Guid ChallengeId, int ResendAfterSeconds, int ValidMinutes, string MaskedEmail);
public record OtpVerify(OtpResult Result, Admin? Admin, int TriesLeft, int Kind = 0, bool RecoveryUsed = false);
public record EnrolStart(OtpResult Result, string? Secret, string? Uri, Admin? Admin);
public record EnrolDone(OtpResult Result, Admin? Admin, List<string>? RecoveryCodes, int TriesLeft);
public record OtpResend(OtpResult Result, OtpIssued? Issued, int WaitSeconds);

public interface ILoginOtpService
{
    Task<OtpIssued> IssueAsync(Admin admin, string? ip);
    Task<OtpVerify> VerifyAsync(Guid challengeId, string code, string? ip);
    Task<OtpResend> ResendAsync(Guid challengeId, string? ip);
    /// <summary>For accounts that already have an authenticator: no email, the app code is asked for directly.</summary>
    Task<Guid> IssueAuthenticatorAsync(Admin admin, string? ip);
    /// <summary>After the emailed code has been accepted for an account with no authenticator: the compulsory set-up step.</summary>
    Task<Guid> IssueEnrolAsync(Admin admin, string? ip);
    Task<EnrolStart> StartEnrolAsync(Guid challengeId);
    Task<EnrolDone> ConfirmEnrolAsync(Guid challengeId, string code, string? ip);
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
    private readonly IAuthenticatorService _totp;
    private readonly ISecretProtector _protector;
    private readonly byte[] _key;

    public LoginOtpService(ShmsDbContext db, IEmailService email, ISecurityEventService events, IConfiguration cfg, ILogger<LoginOtpService> log,
        IAuthenticatorService totp, ISecretProtector protector)
    {
        _db = db; _email = email; _events = events; _cfg = cfg; _log = log; _totp = totp; _protector = protector;
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

        if (ch.Kind == 2) return new OtpVerify(OtpResult.NotFound, null, 0);   // enrolment has its own endpoints

        code = (code ?? "").Trim();
        var recoveryUsed = false;
        bool good;
        if (ch.Kind == 1)
        {
            // Already enrolled: only the authenticator app (or a recovery code) is accepted; the emailed code is not.
            var secret = _protector.Unprotect(admin.TotpSecretEnc);
            var step = secret != null ? _totp.VerifyCode(secret, code, admin.TotpLastStep) : null;
            good = step != null;
            if (good) admin.TotpLastStep = step!.Value;
            else if (code.Length >= 10 && await _totp.TryUseRecoveryCodeAsync(admin.Id, code)) { good = true; recoveryUsed = true; }
        }
        else
        {
            good = code.Length == 6 && code.All(char.IsAsciiDigit)
                && CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(Hash(ch.AdminId, ch.Id, code)), Encoding.ASCII.GetBytes(ch.CodeHash));
        }

        if (good)
        {
            ch.UsedAt = now;
            await _db.SaveChangesAsync();
            if (recoveryUsed)
                await _events.RecordAsync("Auth", "RecoveryCodeUsed", "Warning",
                    $"A recovery code was used to sign in to the {admin.UserType} account {admin.Email}.", admin.Id.ToString(), admin.UserType.ToString(), ip);
            return new OtpVerify(OtpResult.Ok, admin, MaxAttempts, ch.Kind, recoveryUsed);
        }

        ch.Attempts++;
        var left = Math.Max(0, MaxAttempts - ch.Attempts);
        if (left == 0)
        {
            ch.Burned = true;
            await _db.SaveChangesAsync();
            await _events.RecordAsync("Auth", "OtpTooManyTries", "High",
                $"Sign-in code for a {admin.UserType} account ({admin.Email}) was entered wrongly {MaxAttempts} times; the code was cancelled.",
                admin.Id.ToString(), admin.UserType.ToString(), ip);

            // Several cancelled codes in an hour: someone has the password and keeps guessing. Lock the account.
            var since = now.AddHours(-1);
            var burns = await _db.LoginChallenges.CountAsync(c => c.AdminId == admin.Id && c.Burned && c.Attempts >= MaxAttempts && c.CreatedAt >= since);
            if (burns >= BurnsBeforeLock && admin is not SuperAdmin)
            {
                admin.IsLockedOut = true;
                await _db.SaveChangesAsync();
                await _events.RecordAsync("Auth", "OtpAccountLocked", "Critical",
                    $"{admin.UserType} account ({admin.Email}) locked after {burns} cancelled sign-in codes in one hour. It needs a password reset.",
                    admin.Id.ToString(), admin.UserType.ToString(), ip);
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
        if (ch == null || ch.UsedAt != null || ch.Burned || ch.Kind != 0) return new OtpResend(OtpResult.NotFound, null, 0);

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

    public async Task<Guid> IssueAuthenticatorAsync(Admin admin, string? ip)
    {
        var live = await _db.LoginChallenges.Where(c => c.AdminId == admin.Id && c.UsedAt == null && !c.Burned).ToListAsync();
        foreach (var c in live) c.Burned = true;
        var now = DateTime.UtcNow;
        var ch = new LoginChallenge { AdminId = admin.Id, IpAddress = ip, CreatedAt = now, Kind = 1, ExpiresAt = now.AddMinutes(ExpiryMinutes), ResendAvailableAt = now };
        _db.LoginChallenges.Add(ch);
        await _db.SaveChangesAsync();
        return ch.Id;
    }

    public async Task<Guid> IssueEnrolAsync(Admin admin, string? ip)
    {
        var live = await _db.LoginChallenges.Where(c => c.AdminId == admin.Id && c.UsedAt == null && !c.Burned).ToListAsync();
        foreach (var c in live) c.Burned = true;
        var now = DateTime.UtcNow;
        var ch = new LoginChallenge { AdminId = admin.Id, IpAddress = ip, CreatedAt = now, Kind = 2, ExpiresAt = now.AddMinutes(MaxLifeMinutes), ResendAvailableAt = now };
        _db.LoginChallenges.Add(ch);
        await _db.SaveChangesAsync();
        return ch.Id;
    }

    private async Task<(LoginChallenge? Ch, Admin? Admin, OtpResult Fail)> LoadEnrolAsync(Guid challengeId)
    {
        var ch = await _db.LoginChallenges.FirstOrDefaultAsync(c => c.Id == challengeId && c.Kind == 2);
        if (ch == null || ch.UsedAt != null || ch.Burned) return (null, null, OtpResult.NotFound);
        if (DateTime.UtcNow > ch.ExpiresAt) { ch.Burned = true; await _db.SaveChangesAsync(); return (null, null, OtpResult.Expired); }
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Id == ch.AdminId);
        if (admin == null || !admin.IsActive || admin.IsDeleted || admin.IsLockedOut)
        { ch.Burned = true; await _db.SaveChangesAsync(); return (null, null, OtpResult.AccountLocked); }
        if (admin.TotpEnabledAt != null) { ch.Burned = true; await _db.SaveChangesAsync(); return (null, null, OtpResult.NotFound); }
        return (ch, admin, OtpResult.Ok);
    }

    public async Task<EnrolStart> StartEnrolAsync(Guid challengeId)
    {
        var (ch, admin, fail) = await LoadEnrolAsync(challengeId);
        if (ch == null || admin == null) return new EnrolStart(fail, null, null, null);
        // The same secret is shown again if the page is reloaded, so a code already scanned keeps working.
        string secret;
        var existing = _protector.Unprotect(ch.PendingSecretEnc);
        if (existing != null) secret = existing;
        else { secret = _totp.NewSecret(); ch.PendingSecretEnc = _protector.Protect(secret); await _db.SaveChangesAsync(); }
        return new EnrolStart(OtpResult.Ok, secret, _totp.BuildUri(admin.Email, secret), admin);
    }

    public async Task<EnrolDone> ConfirmEnrolAsync(Guid challengeId, string code, string? ip)
    {
        var (ch, admin, fail) = await LoadEnrolAsync(challengeId);
        if (ch == null || admin == null) return new EnrolDone(fail, null, null, 0);
        var secret = _protector.Unprotect(ch.PendingSecretEnc);
        if (secret == null) return new EnrolDone(OtpResult.NotFound, null, null, 0);

        var step = _totp.VerifyCode(secret, code, 0);
        if (step == null)
        {
            ch.Attempts++;
            var left = Math.Max(0, MaxAttempts - ch.Attempts);
            if (left == 0)
            {
                ch.Burned = true;
                await _db.SaveChangesAsync();
                await _events.RecordAsync("Auth", "EnrolTooManyTries", "High",
                    $"Authenticator set-up for the {admin.UserType} account {admin.Email} was cancelled after {MaxAttempts} wrong codes.",
                    admin.Id.ToString(), admin.UserType.ToString(), ip);
                return new EnrolDone(OtpResult.TooManyTries, null, null, 0);
            }
            await _db.SaveChangesAsync();
            return new EnrolDone(OtpResult.Wrong, null, null, left);
        }

        admin.TotpSecretEnc = _protector.Protect(secret);
        admin.TotpEnabledAt = DateTime.UtcNow;
        admin.TotpLastStep = step.Value;
        ch.UsedAt = DateTime.UtcNow;
        ch.PendingSecretEnc = null;
        await _db.SaveChangesAsync();
        var recovery = await _totp.CreateRecoveryCodesAsync(admin.Id);
        await _events.RecordAsync("Auth", "AuthenticatorActivated", "Info",
            $"The {admin.UserType} account {admin.Email} activated its authenticator app.", admin.Id.ToString(), admin.UserType.ToString(), ip);
        return new EnrolDone(OtpResult.Ok, admin, recovery, MaxAttempts);
    }
}
