using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Security;

public interface IAuthenticatorService
{
    string NewSecret();
    string BuildUri(string email, string secret);
    /// <summary>Returns the matching time step, or null. A step at or below lastStep is refused (a code works once).</summary>
    long? VerifyCode(string secret, string code, long lastStep);
    Task<List<string>> CreateRecoveryCodesAsync(Guid adminId);
    Task<bool> TryUseRecoveryCodeAsync(Guid adminId, string code);
}

/// <summary>
/// Authenticator app codes (RFC 6238: HMAC-SHA1, 30 seconds, 6 digits). The window accepts the previous and next step
/// only, a step can never be used twice, and recovery codes are 10-character single-use codes stored only as keyed hashes.
/// </summary>
public class AuthenticatorService : IAuthenticatorService
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const string RecoveryAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // no 0/O/1/I/L
    private const string Issuer = "Romah Estates";
    private readonly ShmsDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly byte[] _key;

    public AuthenticatorService(ShmsDbContext db, IConfiguration cfg)
    {
        _db = db; _cfg = cfg;
        var root = !string.IsNullOrWhiteSpace(cfg["Security:OtpPepper"]) ? cfg["Security:OtpPepper"]! : cfg["JwtOptions:Secret"] ?? "";
        if (root.Length < 32) throw new InvalidOperationException("Security:OtpPepper (or the JWT secret) must be at least 32 characters.");
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(root), 32, info: Encoding.UTF8.GetBytes("shms-recovery-code-v1"));
    }

    private int Cfg(string name, int def) => int.TryParse(_cfg[$"Authenticator:{name}"], out var v) && v > 0 ? v : def;

    public string NewSecret() => Base32(RandomNumberGenerator.GetBytes(20));

    public string BuildUri(string email, string secret)
        => $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";

    public long? VerifyCode(string secret, string code, long lastStep)
    {
        code = (code ?? "").Trim();
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) return null;
        var key = FromBase32(secret);
        if (key == null) return null;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var tolerance = Cfg("ToleranceSteps", 1);
        long? hit = null;
        for (var step = now - tolerance; step <= now + tolerance; step++)
        {
            // Every step is compared (no early exit) so timing does not reveal which step matched.
            var expected = Code(key, step);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code)) && step > lastStep)
                hit = step;
        }
        return hit;
    }

    private static string Code(byte[] key, long step)
    {
        var counter = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(step));
        var h = HMACSHA1.HashData(key, counter);
        var o = h[^1] & 0x0F;
        var bin = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (bin % 1_000_000).ToString("D6");
    }

    public async Task<List<string>> CreateRecoveryCodesAsync(Guid adminId)
    {
        var old = await _db.AdminRecoveryCodes.Where(r => r.AdminId == adminId).ToListAsync();
        _db.AdminRecoveryCodes.RemoveRange(old);
        var shown = new List<string>();
        for (var i = 0; i < Cfg("RecoveryCodeCount", 10); i++)
        {
            var chars = new char[10];
            for (var j = 0; j < 10; j++) chars[j] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
            var raw = new string(chars);
            _db.AdminRecoveryCodes.Add(new AdminRecoveryCode { AdminId = adminId, CodeHash = HashRecovery(adminId, raw) });
            shown.Add(raw[..5] + "-" + raw[5..]);
        }
        await _db.SaveChangesAsync();
        return shown;
    }

    public async Task<bool> TryUseRecoveryCodeAsync(Guid adminId, string code)
    {
        var raw = new string((code ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (raw.Length != 10) return false;
        var hash = HashRecovery(adminId, raw);
        var row = await _db.AdminRecoveryCodes.FirstOrDefaultAsync(r => r.AdminId == adminId && r.UsedAt == null && r.CodeHash == hash);
        if (row == null) return false;
        row.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    private string HashRecovery(Guid adminId, string raw)
        => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{adminId:N}|{raw}")));

    private static string Base32(byte[] data)
    {
        var sb = new StringBuilder(); int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[]? FromBase32(string s)
    {
        var o = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var c in s.TrimEnd('=').ToUpperInvariant())
        {
            var v = Alphabet.IndexOf(c); if (v < 0) return null;
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { o.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return o.ToArray();
    }
}
