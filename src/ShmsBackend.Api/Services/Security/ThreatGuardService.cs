using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Data.Context;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Security;

public interface IThreatGuard
{
    bool IsBlocked(string ip, out DateTime until);
    bool LockdownActive { get; }
    Task<SecuritySettings> GetSettingsAsync(bool fresh = false);
    void InvalidateSettings();
    Task OnLoginAsync(string ip, string? userAgent, string email, bool success, string? role = null);
    Task OnResponseAsync(HttpContext ctx);
    Task BlockIpAsync(string ip, string reason, bool manual = false, int? minutes = null, string? by = null);
    Task ReleaseIpAsync(string ip, string? by);
    Task<bool> ReactivateAccountAsync(Guid userId, string by);
    Task<bool> DeactivateAccountManualAsync(Guid userId, string by, string reason);
}

/// <summary>
/// Detects attacks and answers proportionally: warn first, cool off, block the IP temporarily, and only
/// deactivate an account when the evidence points at that account. Designed so good users are not locked out:
/// shared IPs get higher thresholds, private/proxy addresses are never blocked, repeat offences escalate slowly,
/// SuperAdmins are only ever alerted on, and every action is visible and reversible from the Security Alerts page.
/// Counters are in memory (single API instance); move them to Redis if the API is ever scaled out.
/// </summary>
public class ThreatGuardService : IThreatGuard
{
    private sealed class Counter
    {
        private readonly object _l = new();
        private readonly List<(DateTime T, string K)> _items = new();
        public (int Total, int Distinct) Add(DateTime now, TimeSpan window, string key = "")
        {
            lock (_l)
            {
                _items.RemoveAll(x => now - x.T > window);
                if (_items.Count >= 5000) _items.RemoveAt(0);
                _items.Add((now, key));
                return Summary();
            }
        }
        public (int Total, int Distinct) Peek(DateTime now, TimeSpan window)
        {
            lock (_l) { _items.RemoveAll(x => now - x.T > window); return Summary(); }
        }
        public void Clear() { lock (_l) _items.Clear(); }
        public bool Empty(DateTime now, TimeSpan keep) { lock (_l) { _items.RemoveAll(x => now - x.T > keep); return _items.Count == 0; } }
        private (int, int) Summary() => (_items.Count, _items.Select(x => x.K).Distinct().Count());
    }

    private readonly IServiceScopeFactory _scopes;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;
    private readonly ISecurityEventService _events;
    private readonly ILogger<ThreatGuardService> _log;

    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private readonly ConcurrentDictionary<string, DateTime> _blocked = new();
    private readonly ConcurrentDictionary<string, DateTime> _muted = new();
    private SecuritySettings _settings = new();
    private DateTime _settingsLoadedAt = DateTime.MinValue;
    private int _calls;
    private bool _blocksLoaded;

    public ThreatGuardService(IServiceScopeFactory scopes, Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
        ISecurityEventService events, ILogger<ThreatGuardService> log)
    { _scopes = scopes; _cache = cache; _events = events; _log = log; }

    // ---------- settings ----------
    public bool LockdownActive => _settings.LockdownActive;

    public void InvalidateSettings() => _settingsLoadedAt = DateTime.MinValue;

    public async Task<SecuritySettings> GetSettingsAsync(bool fresh = false)
    {
        if (!fresh && DateTime.UtcNow - _settingsLoadedAt < TimeSpan.FromSeconds(10)) return _settings;
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
            var row = await db.SecuritySettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync();
            if (row == null)
            {
                row = new SecuritySettings();
                db.SecuritySettings.Add(row);
                await db.SaveChangesAsync();
            }
            _settings = row;
            _settingsLoadedAt = DateTime.UtcNow;
            await LoadBlocksOnceAsync(db);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not load security settings; using last known values"); }
        return _settings;
    }

    private async Task LoadBlocksOnceAsync(ShmsDbContext db)
    {
        if (_blocksLoaded) return;
        var now = DateTime.UtcNow;
        var rows = await db.BlockedIps.AsNoTracking().Where(b => b.ReleasedAt == null && b.ExpiresAt > now).ToListAsync();
        foreach (var r in rows) _blocked[r.IpAddress] = r.ExpiresAt;
        _blocksLoaded = true;
    }

    // ---------- blocking ----------
    public bool IsBlocked(string ip, out DateTime until)
    {
        until = default;
        if (!_blocked.TryGetValue(ip, out var exp)) return false;
        if (exp <= DateTime.UtcNow) { _blocked.TryRemove(ip, out _); return false; }
        until = exp; return true;
    }

    private static bool IsPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return true;
        if (IPAddress.IsLoopback(a)) return true;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.ToString().StartsWith("fc") || a.ToString().StartsWith("fd");
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private bool Blockable(string ip, SecuritySettings s) => s.BlockPrivateIps || !IsPrivate(ip);

    public async Task BlockIpAsync(string ip, string reason, bool manual = false, int? minutes = null, string? by = null)
    {
        try
        {
            var s = await GetSettingsAsync();
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
            var since = DateTime.UtcNow.AddHours(-24);
            var prior = await db.BlockedIps.CountAsync(b => b.IpAddress == ip && b.BlockedAt >= since && !b.IsManual);

            var mins = minutes ?? (int)Math.Min(s.BlockMinutesBase * Math.Pow(4, prior), s.BlockMaxHours * 60);
            var until = DateTime.UtcNow.AddMinutes(mins);
            db.BlockedIps.Add(new BlockedIp { IpAddress = ip, Reason = reason, ExpiresAt = until, IsManual = manual, ReleasedBy = null });
            await db.SaveChangesAsync();
            _blocked[ip] = until;

            await _events.RecordAsync("Access", manual ? "IpBlockedManual" : "IpBlocked", manual ? "Warning" : "High",
                $"IP {ip} blocked for {mins} minute(s): {reason}", by, manual ? "Staff" : null, ip);

            // Repeated blocks: escalate to the accounts that actually signed in from this IP (never on a shared IP).
            if (!manual && s.AutoDeactivateEnabled && prior + 1 >= s.EscalateAfterBlocks)
            {
                var since24 = DateTime.UtcNow.AddHours(-24);
                var users = await db.LoginActivities.AsNoTracking()
                    .Where(a => a.IpAddress == ip && a.Success && a.UserId != null && a.CreatedAt >= since24)
                    .Select(a => a.UserId!.Value).Distinct().ToListAsync();
                if (users.Count >= s.SharedIpFactor)
                {
                    await _events.RecordAsync("Access", "IpEscalationSkipped", "Warning",
                        $"IP {ip} was blocked {prior + 1} times but {users.Count} different accounts use it (looks shared), so no account was deactivated.", null, null, ip);
                }
                else
                {
                    foreach (var u in users)
                        await _events.DeactivateAccountAsync(u.ToString(),
                            $"IP {ip} was blocked {prior + 1} times in 24 hours and this account signed in from it.",
                            "Access", "AccountDeactivatedIpEscalation", ip);
                }
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not block IP {Ip}", ip); }
    }

    public async Task ReleaseIpAsync(string ip, string? by)
    {
        _blocked.TryRemove(ip, out _);
        foreach (var k in _counters.Keys.Where(k => k.Contains(ip))) _counters.TryRemove(k, out _);
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var now = DateTime.UtcNow;
        await db.BlockedIps.Where(b => b.IpAddress == ip && b.ReleasedAt == null && b.ExpiresAt > now)
            .ExecuteUpdateAsync(x => x.SetProperty(b => b.ReleasedAt, now).SetProperty(b => b.ReleasedBy, by));
        await _events.RecordAsync("Access", "IpReleased", "Info", $"IP {ip} unblocked by staff.", by, "Staff", ip);
    }

    // ---------- accounts ----------
    public async Task<bool> ReactivateAccountAsync(Guid userId, string by)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
        var ok = false;
        var admin = await db.Admins.FirstOrDefaultAsync(a => a.Id == userId);
        if (admin != null) { admin.IsActive = true; admin.IsLockedOut = false; admin.FailedLoginAttempts = 0; if (admin.TotpEnabledAt == null) admin.AuthenticatorGraceFrom = DateTime.UtcNow; ok = true; }
        else
        {
            var p = await db.PortalUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (p != null) { p.IsActive = true; p.IsLockedOut = false; ok = true; }
        }
        if (ok)
        {
            await db.SaveChangesAsync();
            _cache.Remove($"auth:active:{userId}");
            ClearAccountCounters(userId.ToString());
            await _events.RecordAsync("Access", "AccountReactivated", "Info", "An account was re-activated after review.", by, "Staff", null, "Account", userId);
        }
        return ok;
    }

    public async Task<bool> DeactivateAccountManualAsync(Guid userId, string by, string reason)
        => await _events.DeactivateAccountAsync(userId.ToString(), $"Deactivated by staff: {reason}", "Access", "AccountDeactivatedManual");

    private void ClearAccountCounters(string id)
    {
        foreach (var k in _counters.Keys.Where(k => k.EndsWith(":" + id))) _counters.TryRemove(k, out _);
    }

    private Counter C(string key) => _counters.GetOrAdd(key, _ => new Counter());

    private bool Mute(string key, TimeSpan span)
    {
        var now = DateTime.UtcNow;
        if (_muted.TryGetValue(key, out var until) && until > now) return true;
        _muted[key] = now.Add(span);
        return false;
    }

    private void Sweep()
    {
        if (Interlocked.Increment(ref _calls) % 2000 != 0) return;
        var now = DateTime.UtcNow;
        foreach (var kv in _counters) if (kv.Value.Empty(now, TimeSpan.FromHours(1))) _counters.TryRemove(kv.Key, out _);
        foreach (var kv in _muted) if (kv.Value <= now) _muted.TryRemove(kv.Key, out _);
    }

    /// <summary>Warn on the first breach, deactivate on the Nth within 24 h (setting). SuperAdmins are only alerted.</summary>
    private async Task AccountBreachAsync(string userId, string eventType, string summary, string? ip)
    {
        var s = await GetSettingsAsync();
        int prior;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
            var since = DateTime.UtcNow.AddHours(-24);
            prior = await db.SecurityEvents.CountAsync(e => e.ActorUserId == userId && e.EventType == eventType && e.CreatedAt >= since);
        }
        if (s.AutoDeactivateEnabled && prior + 1 >= s.AccountStrikesBeforeDeactivate)
            await _events.DeactivateAccountAsync(userId, summary + $" (breach {prior + 1} in 24 hours)", "Access", eventType, ip);
        else
            await _events.RecordAsync("Access", eventType, "High", summary + " First warning: a repeat within 24 hours will deactivate the account.", userId, null, ip);
    }

    // ---------- login hook ----------
    public async Task OnLoginAsync(string ip, string? userAgent, string email, bool success, string? role = null)
    {
        try
        {
            Sweep();
            var s = await GetSettingsAsync();
            var now = DateTime.UtcNow;
            email = (email ?? "").Trim().ToLowerInvariant();

            Guid? userId = null;
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ShmsDbContext>();
                // The same email can exist under several roles, so look the account up by email AND role.
                if (role != null && Enum.TryParse<ShmsBackend.Data.Enums.UserType>(role, out var staffType))
                    userId = await db.Admins.AsNoTracking().Where(a => a.Email.ToLower() == email && a.UserType == staffType).Select(a => (Guid?)a.Id).FirstOrDefaultAsync();
                else if (role != null && Enum.TryParse<ShmsBackend.Data.Enums.PortalUserType>(role, out var portalType))
                    userId = await db.PortalUsers.AsNoTracking().Where(u => u.Email.ToLower() == email && u.PortalUserType == portalType).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
                else
                    userId = await db.Admins.AsNoTracking().Where(a => a.Email.ToLower() == email).Select(a => (Guid?)a.Id).FirstOrDefaultAsync()
                          ?? await db.PortalUsers.AsNoTracking().Where(u => u.Email.ToLower() == email).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
                db.LoginActivities.Add(new LoginActivity
                {
                    UserId = userId, Email = email.Length > 200 ? email[..200] : email, IpAddress = ip, Success = success, Role = role,
                    UserAgent = userAgent?.Length > 300 ? userAgent[..300] : userAgent
                });
                await db.SaveChangesAsync();
                if (Random.Shared.Next(500) == 0)
                    await db.LoginActivities.Where(a => a.CreatedAt < now.AddDays(-90)).ExecuteDeleteAsync();
            }

            var win = TimeSpan.FromMinutes(s.IpFailWindowMinutes);
            // An IP where several different real accounts sign in successfully is probably shared (office, mobile carrier).
            var shared = C($"ipok:{ip}").Peek(now, win).Distinct >= 3;
            var factor = shared ? Math.Max(1, s.SharedIpFactor) : 1;

            if (!success)
            {
                var (total, distinct) = C($"ipfail:{ip}").Add(now, win, email);
                if (Blockable(ip, s) && (total >= s.IpFailThreshold * factor || distinct >= s.IpDistinctAccountsThreshold * factor)
                    && !Mute($"blk:{ip}", TimeSpan.FromMinutes(1)))
                {
                    C($"ipfail:{ip}").Clear();
                    await BlockIpAsync(ip, distinct >= s.IpDistinctAccountsThreshold * factor
                        ? $"{distinct} different accounts failed to sign in from it (password spraying)"
                        : $"{total} failed sign-ins in {s.IpFailWindowMinutes} minutes");
                }

                // Someone is hammering a SuperAdmin account. It is never locked or deactivated by this, but every
                // SuperAdmin and Admin is told straight away (bell, push and email) so a person can act.
                if (userId != null && role == "SuperAdmin")
                {
                    var (sa, _) = C($"safail:{userId}").Add(now, win, ip);
                    if (sa >= (s.SuperAdminFailThreshold > 0 ? s.SuperAdminFailThreshold : 3) && !Mute($"sa:{userId}", TimeSpan.FromMinutes(10)))
                        await _events.RecordAsync("Auth", "SuperAdminTargeted", "Critical",
                            $"Someone is trying to sign in as SuperAdmin ({email}): {sa} failed attempts in {s.IpFailWindowMinutes} minutes, latest from IP {ip}. The account is not locked; check it now.",
                            userId.ToString(), "SuperAdmin", ip);
                }

                if (userId != null)
                {
                    var (t, ips) = C($"acctfailips:{userId}").Add(now, win, ip);
                    if (ips >= 10 && t >= 15 && !Mute($"dist:{userId}", TimeSpan.FromMinutes(10)))
                        await _events.RecordAsync("Auth", "DistributedAttack", "High",
                            $"One {role ?? "account"} account ({email}) is being attacked from {ips} different IPs ({t} failed sign-ins).", userId.ToString(), role, ip);
                }
                return;
            }

            // Success
            if (userId == null) return;
            C($"ipok:{ip}").Add(now, win, userId.ToString()!);

            // Password-spray hit: this IP failed on several other accounts, then got one right.
            var sprayed = C($"ipfail:{ip}").Peek(now, win).Distinct;
            if (sprayed >= s.SprayDistinctAccounts && !shared && Blockable(ip, s))
            {
                await _events.DeactivateAccountAsync(userId.ToString()!,
                    $"Signed in from IP {ip} right after that IP failed on {sprayed} other accounts (password spraying).",
                    "Auth", "SprayHit", ip);
                await BlockIpAsync(ip, $"password spraying: {sprayed} accounts tried, one succeeded");
                return;
            }

            // One account used from many IPs in a short time.
            var dwin = TimeSpan.FromMinutes(s.DistinctIpsWindowMinutes);
            var (_, ipCount) = C($"acctips:{userId}").Add(now, dwin, ip);
            if (ipCount >= s.DistinctIpsThreshold && !Mute($"ips:{userId}", dwin))
                await AccountBreachAsync(userId.ToString()!, "ManyIpsOneAccount",
                    $"One account signed in from {ipCount} different IPs within {s.DistinctIpsWindowMinutes} minutes.", ip);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Threat guard login hook failed"); }
    }

    // ---------- response hook ----------
    public async Task OnResponseAsync(HttpContext ctx)
    {
        try
        {
            var path = ctx.Request.Path.Value ?? "";
            if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)) return;
            if (path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase) || path.Contains("/mpesa/callback/", StringComparison.OrdinalIgnoreCase)) return;
            Sweep();

            var s = await GetSettingsAsync();
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var status = ctx.Response.StatusCode;
            var now = DateTime.UtcNow;
            var uid = ctx.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (status == 404 && Blockable(ip, s))
            {
                var (n, _) = C($"nf:{ip}").Add(now, TimeSpan.FromMinutes(s.NotFoundWindowMinutes));
                if (n >= s.NotFoundThreshold && !Mute($"blk:{ip}", TimeSpan.FromMinutes(1)))
                { C($"nf:{ip}").Clear(); await BlockIpAsync(ip, $"{n} requests for pages that do not exist in {s.NotFoundWindowMinutes} minutes (scanning)"); }
            }
            else if (status == 429 && Blockable(ip, s))
            {
                var (n, _) = C($"rl:{ip}").Add(now, TimeSpan.FromMinutes(s.RateLimitedWindowMinutes));
                if (n >= s.RateLimitedThreshold && !Mute($"blk:{ip}", TimeSpan.FromMinutes(1)))
                { C($"rl:{ip}").Clear(); await BlockIpAsync(ip, $"kept hitting the rate limit ({n} times in {s.RateLimitedWindowMinutes} minutes)"); }
            }
            else if (status == 403 && uid != null && !ctx.Items.ContainsKey("terms-required"))
            {
                var (n, _) = C($"403:{uid}").Add(now, TimeSpan.FromMinutes(s.ForbiddenWindowMinutes));
                if (n >= s.ForbiddenThreshold)
                {
                    C($"403:{uid}").Clear();
                    await AccountBreachAsync(uid, "RepeatedForbidden",
                        $"A signed-in account was refused {n} times in {s.ForbiddenWindowMinutes} minutes (trying to open records it does not own).", ip);
                }
            }
            else if (status < 400 && uid != null && HttpMethods.IsGet(ctx.Request.Method))
            {
                var (n, _) = C($"rd:{uid}").Add(now, TimeSpan.FromMinutes(1));
                if (n >= s.ReadsPerMinuteThreshold)
                {
                    C($"rd:{uid}").Clear();
                    await AccountBreachAsync(uid, "MassRead",
                        $"One account made {n} data reads in a minute, far above normal (possible data scraping or leak).", ip);
                }
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Threat guard response hook failed"); }
    }
}
