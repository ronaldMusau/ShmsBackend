using System.Security.Cryptography;
using System.Text;

namespace ShmsBackend.Api.Services.Security;

/// <summary>
/// Key handling for the always-on API payload encryption.
/// The server holds long-lived P-256 key pairs (identified by a key id). Each browser creates its own
/// ephemeral P-256 pair + random salt, sends its public half and salt with every request, and both sides
/// derive the same AES-256-GCM key: HKDF-SHA256(ECDH shared secret, salt, "shms-enc-v1|kid").
/// No session key is ever stored on the server, and each message uses a fresh random nonce.
/// </summary>
public sealed class PayloadEncryptionService
{
    private readonly Dictionary<string, ECDiffieHellman> _keys = new();
    private readonly object _lock = new();

    public string CurrentKeyId { get; }

    public PayloadEncryptionService(IConfiguration config, IWebHostEnvironment env, ILogger<PayloadEncryptionService> log)
    {
        var section = config.GetSection("Encryption:Keys");
        foreach (var child in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Value)) continue;
            var ecdh = ECDiffieHellman.Create();
            ecdh.ImportPkcs8PrivateKey(Convert.FromBase64String(child.Value), out _);
            if (ecdh.KeySize != 256) throw new InvalidOperationException($"Encryption key '{child.Key}' must be P-256.");
            _keys[child.Key] = ecdh;
        }

        CurrentKeyId = config["Encryption:CurrentKeyId"] ?? "k1";

        if (!_keys.ContainsKey(CurrentKeyId))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException(
                    $"Encryption:Keys:{CurrentKeyId} is not configured. Generate a P-256 key and set it as a secret (see docs).");
            // Development only: temporary key. Sessions reset on every restart; the browser re-fetches the key automatically.
            _keys[CurrentKeyId] = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            log.LogWarning("Encryption:Keys:{Kid} not set - using a temporary in-memory key (development only).", CurrentKeyId);
        }
    }

    public bool HasKey(string kid) => _keys.ContainsKey(kid);

    /// <summary>Server public key as a raw uncompressed point (65 bytes), base64.</summary>
    public string GetPublicKeyBase64(string kid)
    {
        var p = _keys[kid].ExportParameters(false);
        var raw = new byte[65];
        raw[0] = 4;
        Pad(p.Q.X!, raw, 1);
        Pad(p.Q.Y!, raw, 33);
        return Convert.ToBase64String(raw);
    }

    public byte[]? DeriveKey(string kid, byte[] clientPublicRaw, byte[] salt)
    {
        if (!_keys.TryGetValue(kid, out var server) || clientPublicRaw.Length != 65 || clientPublicRaw[0] != 4 || salt.Length < 16)
            return null;
        try
        {
            var q = new ECPoint
            {
                X = clientPublicRaw.AsSpan(1, 32).ToArray(),
                Y = clientPublicRaw.AsSpan(33, 32).ToArray()
            };
            using var client = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = q });
            byte[] secret;
            lock (_lock) { secret = server.DeriveRawSecretAgreement(client.PublicKey); }
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, salt, Encoding.UTF8.GetBytes("shms-enc-v1|" + kid));
        }
        catch (CryptographicException) { return null; } // point not on curve, etc.
    }

    /// <summary>Returns nonce(12) || ciphertext || tag(16).</summary>
    public static byte[] Encrypt(byte[] key, byte[] plain, byte[] aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ct = new byte[plain.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, plain, ct, tag, aad);
        var o = new byte[12 + ct.Length + 16];
        nonce.CopyTo(o, 0); ct.CopyTo(o, 12); tag.CopyTo(o, 12 + ct.Length);
        return o;
    }

    public static byte[]? Decrypt(byte[] key, byte[] blob, byte[] aad)
    {
        if (blob.Length < 12 + 16) return null;
        var ctLen = blob.Length - 28;
        var plain = new byte[ctLen];
        try
        {
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(12, ctLen), blob.AsSpan(12 + ctLen, 16), plain, aad);
            return plain;
        }
        catch (CryptographicException) { return null; }
    }

    private static void Pad(byte[] src, byte[] dst, int offset)
        => src.CopyTo(dst, offset + (32 - src.Length));
}
