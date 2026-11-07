using ShmsBackend.Api.Services.Security;

namespace ShmsBackend.Api.Middleware;

/// <summary>
/// First line of defence. Rejects blocked IPs before any work is done, then lets the guard watch the outcome
/// (404 / 429 / 403 / read volume) after the request finishes. Never blocks health checks or the M-Pesa callback.
/// </summary>
public class ThreatGuardMiddleware
{
    private readonly RequestDelegate _next;
    public ThreatGuardMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IThreatGuard guard)
    {
        var path = ctx.Request.Path.Value ?? "";
        var exempt = !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/mpesa/callback/", StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsOptions(ctx.Request.Method);
        if (exempt) { await _next(ctx); return; }

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (guard.IsBlocked(ip, out var until))
        {
            var secs = Math.Max(1, (int)(until - DateTime.UtcNow).TotalSeconds);
            ctx.Response.StatusCode = 429;
            ctx.Response.Headers["Retry-After"] = secs.ToString();
            await ctx.Response.WriteAsJsonAsync(new
            {
                success = false, code = "IP_BLOCKED", retryAfterSeconds = secs,
                message = "Too many suspicious requests from your network. Please try again later."
            });
            return;
        }

        await _next(ctx);
        await guard.OnResponseAsync(ctx);
    }
}

/// <summary>
/// Emergency lockdown (one click on the Security Alerts page). While active, only SuperAdmin and Admin accounts
/// (plus sign-in, health checks, the encryption key and the M-Pesa callback) get through; everyone else gets 503.
/// Runs after authentication so staff can still sign in and switch it off.
/// </summary>
public class LockdownMiddleware
{
    private readonly RequestDelegate _next;
    public LockdownMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IThreatGuard guard)
    {
        if (!guard.LockdownActive) { await _next(ctx); return; }

        var path = ctx.Request.Path.Value ?? "";
        var open = !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/secure/key", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/mpesa/callback/", StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsOptions(ctx.Request.Method)
            || ctx.User.IsInRole("SuperAdmin") || ctx.User.IsInRole("Admin");
        if (open) { await _next(ctx); return; }

        ctx.Response.StatusCode = 503;
        ctx.Response.Headers["Retry-After"] = "300";
        await ctx.Response.WriteAsJsonAsync(new
        {
            success = false, code = "LOCKDOWN",
            message = "The system is temporarily unavailable while we deal with a security issue. Please try again shortly."
        });
    }
}

/// <summary>
/// When the SuperAdmin/Admin address rule is on, a signed-in SuperAdmin or Admin who is not on an allowed address is refused,
/// even with a valid token (a stolen token is useless from elsewhere). Sign-in itself is checked in the auth controller.
/// </summary>
public class PrivilegedIpMiddleware
{
    private readonly RequestDelegate _next;
    public PrivilegedIpMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx, IThreatGuard guard, ShmsBackend.Api.Services.Security.ISecurityEventService events,
        IConfiguration cfg, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        var path = ctx.Request.Path.Value ?? "";
        var skip = !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/secure/key", StringComparison.OrdinalIgnoreCase)
            || HttpMethods.IsOptions(ctx.Request.Method)
            || !(ctx.User.IsInRole("SuperAdmin") || ctx.User.IsInRole("Admin"));
        if (skip) { await _next(ctx); return; }

        var s = await guard.GetSettingsAsync();
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!PrivilegedIpRule.Active(s, cfg) || PrivilegedIpRule.IsAllowed(ip, s.PrivilegedAllowedIps)) { await _next(ctx); return; }

        if (PrivilegedIpRule.ShouldReport(cache, ip))
            await events.RecordAsync("Access", "PrivilegedIpRefused", "High",
                $"A signed-in {(ctx.User.IsInRole("SuperAdmin") ? "SuperAdmin" : "Admin")} session was refused because address {ip} is not on the allowed list.",
                null, null, ip);
        ctx.Response.StatusCode = 403;
        await ctx.Response.WriteAsJsonAsync(new { success = false, code = "IP_NOT_ALLOWED", message = "This account can only be used from approved locations." });
    }
}
