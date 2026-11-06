using System.Text;
using System.Text.Json;
using ShmsBackend.Api.Services.Security;
using StackExchange.Redis;

namespace ShmsBackend.Api.Middleware;

/// <summary>
/// Always-on payload encryption for every /api call. Requests must carry the X-Enc header
/// (v1.kid.clientPub.salt.timestamp.requestId); JSON bodies arrive as {"d": base64(nonce||ct||tag)} and JSON
/// responses leave the same way, so nothing readable appears in the browser Network tab or console.
/// Each request id is accepted once (Redis) and the timestamp must be fresh, so captured calls cannot be replayed.
/// File downloads/uploads (non-JSON) are not wrapped; they are still authenticated and authorised as usual.
/// </summary>
public class PayloadEncryptionMiddleware
{
    private const int MaxSkewSeconds = 120;
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(5);
    private readonly RequestDelegate _next;
    private readonly PayloadEncryptionService _enc;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PayloadEncryptionMiddleware> _log;

    public PayloadEncryptionMiddleware(RequestDelegate next, PayloadEncryptionService enc,
        IConnectionMultiplexer redis, ILogger<PayloadEncryptionMiddleware> log)
    {
        _next = next; _enc = enc; _redis = redis; _log = log;
    }

    private static bool IsExempt(PathString path)
    {
        var p = path.Value ?? "";
        return p.Equals("/api/secure/key", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase)
            // Public company logo: used directly as an <img src>, where a browser cannot attach headers. Public image only.
            || p.Equals("/api/companysettings/public/logo", StringComparison.OrdinalIgnoreCase)
            // Safaricom calls this; it cannot encrypt. It is protected by its own secret, IP and signature checks.
            || p.Contains("/mpesa/callback/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        if (!req.Path.StartsWithSegments("/api") || HttpMethods.IsOptions(req.Method) || IsExempt(req.Path))
        {
            await _next(ctx);
            return;
        }

        // ---- parse header: v1.kid.pub.salt.ts.rid ----
        var parts = req.Headers["X-Enc"].ToString().Split('.');
        if (parts.Length != 6 || parts[0] != "v1" || !_enc.HasKey(parts[1])
            || !long.TryParse(parts[4], out var ts) || parts[5].Length is < 16 or > 64)
        { await Reject(ctx, "bad header"); return; }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > MaxSkewSeconds) { await Reject(ctx, "stale"); return; }

        byte[] pub, salt;
        try { pub = FromB64Url(parts[2]); salt = FromB64Url(parts[3]); }
        catch (FormatException) { await Reject(ctx, "bad encoding"); return; }

        var key = _enc.DeriveKey(parts[1], pub, salt);
        if (key == null) { await Reject(ctx, "key"); return; }

        // ---- one-time request id ----
        var rid = parts[5];
        try
        {
            var fresh = await _redis.GetDatabase().StringSetAsync("shms:enc:rid:" + rid, "1", ReplayWindow, When.NotExists);
            if (!fresh) { await Reject(ctx, "replay"); return; }
        }
        catch (RedisException ex)
        {
            _log.LogError(ex, "Replay store unavailable");
            ctx.Response.StatusCode = 503;
            return;
        }

        // ---- decrypt JSON request body ----
        if (req.HasJsonContentType())
        {
            req.EnableBuffering();
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            if (ms.Length > 0)
            {
                byte[]? plain = null;
                try
                {
                    using var doc = JsonDocument.Parse(ms.ToArray());
                    if (doc.RootElement.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.String)
                    {
                        var aad = Encoding.UTF8.GetBytes($"shms1|req|{req.Method}|{req.Path.Value}|{ts}|{rid}");
                        plain = PayloadEncryptionService.Decrypt(key, Convert.FromBase64String(d.GetString()!), aad);
                    }
                }
                catch (Exception ex) when (ex is JsonException or FormatException) { }
                if (plain == null) { await Reject(ctx, "body"); return; }
                req.Body = new MemoryStream(plain);
                req.ContentLength = plain.Length;
            }
            else req.Body.Position = 0;
        }

        // ---- run the app, capture the response, encrypt JSON ----
        var original = ctx.Response.Body;
        using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;
        try { await _next(ctx); }
        finally { ctx.Response.Body = original; }

        var isJson = ctx.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
        if (buffer.Length > 0 && isJson)
        {
            var aad = Encoding.UTF8.GetBytes($"shms1|res|{rid}|{ctx.Response.StatusCode}");
            var blob = PayloadEncryptionService.Encrypt(key, buffer.ToArray(), aad);
            var body = Encoding.UTF8.GetBytes("{\"d\":\"" + Convert.ToBase64String(blob) + "\"}");
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength = body.Length;
            await original.WriteAsync(body);
        }
        else if (buffer.Length > 0)
        {
            ctx.Response.ContentLength = buffer.Length;
            buffer.Position = 0;
            await buffer.CopyToAsync(original);
        }
    }

    private async Task Reject(HttpContext ctx, string reason)
    {
        _log.LogWarning("Secure channel rejected {Method} {Path} from {Ip}: {Reason}",
            ctx.Request.Method, ctx.Request.Path, ctx.Connection.RemoteIpAddress, reason);
        ctx.Response.StatusCode = 400;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"success\":false,\"message\":\"Secure connection error. Please refresh the page.\",\"code\":\"ENC\"}");
    }

    private static byte[] FromB64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
