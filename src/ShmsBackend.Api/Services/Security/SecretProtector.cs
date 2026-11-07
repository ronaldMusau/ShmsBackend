using System.Security.Cryptography;
using System.Text;

namespace ShmsBackend.Api.Services.Security;

public interface ISecretProtector
{
    string Protect(string plain);
    string? Unprotect(string? protectedValue);
}

/// <summary>
/// Encrypts small secrets (authenticator seeds) with AES-256-GCM. The key lives in configuration
/// (Security:SecretKey, base64 of 32 bytes), never in the database. If it is missing, a key is derived from the JWT secret
/// with a purpose label so the two are never the same. Format: "v1." + base64(nonce|tag|cipher).
/// </summary>
public class SecretProtector : ISecretProtector
{
    private readonly byte[] _key;

    public SecretProtector(IConfiguration cfg)
    {
        var configured = cfg["Security:SecretKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _key = Convert.FromBase64String(configured);
            if (_key.Length != 32) throw new InvalidOperationException("Security:SecretKey must be base64 of exactly 32 bytes.");
        }
        else
        {
            var root = cfg["JwtOptions:Secret"] ?? "";
            if (root.Length < 32) throw new InvalidOperationException("Security:SecretKey (or the JWT secret) must be configured.");
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(root), 32, info: Encoding.UTF8.GetBytes("shms-secret-protector-v1"));
        }
    }

    public string Protect(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(_key, 16);
        gcm.Encrypt(nonce, data, cipher, tag);
        var all = new byte[12 + 16 + cipher.Length];
        nonce.CopyTo(all, 0); tag.CopyTo(all, 12); cipher.CopyTo(all, 28);
        return "v1." + Convert.ToBase64String(all);
    }

    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue) || !protectedValue.StartsWith("v1.")) return null;
        try
        {
            var all = Convert.FromBase64String(protectedValue[3..]);
            var nonce = all[..12]; var tag = all[12..28]; var cipher = all[28..];
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(_key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
    }
}
