using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShmsBackend.Api.Services.Security;
using ShmsBackend.Data.Context;

namespace ShmsBackend.Api.Controllers;

/// <summary>Thresholds, blocked IPs, flagged accounts and the emergency lockdown. SuperAdmin and Admin only.</summary>
[ApiController]
[Route("api/security-center")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class SecurityCenterController : ControllerBase
{
    private readonly ShmsDbContext _db;
    private readonly IThreatGuard _guard;
    private readonly ISecurityEventService _events;
    public SecurityCenterController(ShmsDbContext db, IThreatGuard guard, ISecurityEventService events)
    { _db = db; _guard = guard; _events = events; }

    private string Me => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    // ---- settings ----
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings() => Ok(await _guard.GetSettingsAsync(true));

    public class SettingsDto
    {
        public int IpFailThreshold { get; set; }
        public int IpDistinctAccountsThreshold { get; set; }
        public int IpFailWindowMinutes { get; set; }
        public int SuperAdminFailThreshold { get; set; }
        public int RecoveryRequestCooldownMinutes { get; set; }
        public int SharedIpFactor { get; set; }
        public int NotFoundThreshold { get; set; }
        public int NotFoundWindowMinutes { get; set; }
        public int RateLimitedThreshold { get; set; }
        public int RateLimitedWindowMinutes { get; set; }
        public int BlockMinutesBase { get; set; }
        public int BlockMaxHours { get; set; }
        public int EscalateAfterBlocks { get; set; }
        public bool BlockPrivateIps { get; set; }
        public int SprayDistinctAccounts { get; set; }
        public int DistinctIpsThreshold { get; set; }
        public int DistinctIpsWindowMinutes { get; set; }
        public int ForbiddenThreshold { get; set; }
        public int ForbiddenWindowMinutes { get; set; }
        public int ReadsPerMinuteThreshold { get; set; }
        public int AccountStrikesBeforeDeactivate { get; set; }
        public bool AutoDeactivateEnabled { get; set; }
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] SettingsDto d)
    {
        // Floors stop a typo from creating a hair-trigger that locks out normal users.
        string? bad =
            d.IpFailThreshold < 5 ? "Failed sign-ins per IP must be at least 5."
          : d.IpDistinctAccountsThreshold < 3 ? "Different accounts per IP must be at least 3."
          : d.IpFailWindowMinutes is < 1 or > 1440 ? "Sign-in window must be 1 to 1440 minutes."
          : d.RecoveryRequestCooldownMinutes is < 5 or > 1440 ? "Minutes between emergency-recovery requests must be 5 to 1440."
          : d.SuperAdminFailThreshold is < 2 or > 20 ? "Failed sign-ins before a SuperAdmin alert must be 2 to 20."
          : d.SharedIpFactor is < 1 or > 20 ? "Shared-IP multiplier must be 1 to 20."
          : d.NotFoundThreshold < 10 ? "Missing-page requests must be at least 10."
          : d.NotFoundWindowMinutes is < 1 or > 1440 ? "Missing-page window must be 1 to 1440 minutes."
          : d.RateLimitedThreshold < 5 ? "Rate-limit hits must be at least 5."
          : d.RateLimitedWindowMinutes is < 1 or > 1440 ? "Rate-limit window must be 1 to 1440 minutes."
          : d.BlockMinutesBase is < 1 or > 1440 ? "First block must be 1 to 1440 minutes."
          : d.BlockMaxHours is < 1 or > 720 ? "Longest block must be 1 to 720 hours."
          : d.EscalateAfterBlocks < 2 ? "Blocks before accounts are deactivated must be at least 2."
          : d.SprayDistinctAccounts < 3 ? "Spray accounts must be at least 3."
          : d.DistinctIpsThreshold < 3 ? "Different IPs per account must be at least 3."
          : d.DistinctIpsWindowMinutes is < 1 or > 1440 ? "IP window must be 1 to 1440 minutes."
          : d.ForbiddenThreshold < 3 ? "Refused requests must be at least 3."
          : d.ForbiddenWindowMinutes is < 1 or > 1440 ? "Refused-request window must be 1 to 1440 minutes."
          : d.ReadsPerMinuteThreshold < 60 ? "Reads per minute must be at least 60."
          : d.AccountStrikesBeforeDeactivate is < 1 or > 5 ? "Breaches before deactivation must be 1 to 5."
          : null;
        if (bad != null) return BadRequest(new { success = false, message = bad });

        var s = await _db.SecuritySettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (s == null) { s = new ShmsBackend.Data.Models.Entities.SecuritySettings(); _db.SecuritySettings.Add(s); }
        s.IpFailThreshold = d.IpFailThreshold; s.IpDistinctAccountsThreshold = d.IpDistinctAccountsThreshold;
        s.IpFailWindowMinutes = d.IpFailWindowMinutes; s.SharedIpFactor = d.SharedIpFactor; s.SuperAdminFailThreshold = d.SuperAdminFailThreshold; s.RecoveryRequestCooldownMinutes = d.RecoveryRequestCooldownMinutes;
        s.NotFoundThreshold = d.NotFoundThreshold; s.NotFoundWindowMinutes = d.NotFoundWindowMinutes;
        s.RateLimitedThreshold = d.RateLimitedThreshold; s.RateLimitedWindowMinutes = d.RateLimitedWindowMinutes;
        s.BlockMinutesBase = d.BlockMinutesBase; s.BlockMaxHours = d.BlockMaxHours;
        s.EscalateAfterBlocks = d.EscalateAfterBlocks; s.BlockPrivateIps = d.BlockPrivateIps;
        s.SprayDistinctAccounts = d.SprayDistinctAccounts; s.DistinctIpsThreshold = d.DistinctIpsThreshold;
        s.DistinctIpsWindowMinutes = d.DistinctIpsWindowMinutes; s.ForbiddenThreshold = d.ForbiddenThreshold;
        s.ForbiddenWindowMinutes = d.ForbiddenWindowMinutes; s.ReadsPerMinuteThreshold = d.ReadsPerMinuteThreshold;
        s.AccountStrikesBeforeDeactivate = d.AccountStrikesBeforeDeactivate; s.AutoDeactivateEnabled = d.AutoDeactivateEnabled;
        s.UpdatedAt = DateTime.UtcNow; s.UpdatedBy = Me;
        await _db.SaveChangesAsync();
        _guard.InvalidateSettings();
        await _guard.GetSettingsAsync(true);
        await _events.RecordAsync("System", "SecuritySettingsChanged", "Info", "Security thresholds were changed.", Me, "Staff");
        return Ok(new { success = true });
    }

    // ---- where SuperAdmin and Admin may be used from ----
    [HttpGet("privileged-ip")]
    public async Task<IActionResult> PrivilegedIp()
    {
        var s = await _guard.GetSettingsAsync(true);
        var cfg = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        return Ok(new
        {
            enabled = s.RestrictPrivilegedIps,
            allowedIps = s.PrivilegedAllowedIps ?? "",
            yourIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            overriddenByServer = cfg.GetValue<bool>("Security:DisablePrivilegedIpRestriction"),
            canEdit = User.IsInRole("SuperAdmin")
        });
    }

    public class PrivilegedIpDto { public bool Enabled { get; set; } public string? AllowedIps { get; set; } }

    [HttpPut("privileged-ip")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SetPrivilegedIp([FromBody] PrivilegedIpDto d)
    {
        var list = ShmsBackend.Api.Services.Security.PrivilegedIpRule.Parse(d.AllowedIps);
        var bad = list.FirstOrDefault(x => !ShmsBackend.Api.Services.Security.PrivilegedIpRule.IsValidEntry(x));
        if (bad != null) return BadRequest(new { success = false, message = $"\"{bad}\" is not a valid address or range (for example 41.90.12.5 or 41.90.0.0/16)." });
        if (list.Count > 50) return BadRequest(new { success = false, message = "At most 50 addresses or ranges." });
        var mine = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (d.Enabled)
        {
            if (list.Count == 0) return BadRequest(new { success = false, message = "Add at least one address before switching the rule on." });
            if (!ShmsBackend.Api.Services.Security.PrivilegedIpRule.IsAllowed(mine, string.Join(",", list)))
                return BadRequest(new { success = false, message = $"Your own address ({mine}) is not on the list. Saving this would lock you out, so add it first." });
        }
        var s = await _db.SecuritySettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (s == null) { s = new ShmsBackend.Data.Models.Entities.SecuritySettings(); _db.SecuritySettings.Add(s); }
        s.RestrictPrivilegedIps = d.Enabled; s.PrivilegedAllowedIps = string.Join(",", list);
        s.UpdatedAt = DateTime.UtcNow; s.UpdatedBy = Me;
        await _db.SaveChangesAsync();
        _guard.InvalidateSettings(); await _guard.GetSettingsAsync(true);
        await _events.RecordAsync("System", "PrivilegedIpRuleChanged", "Warning",
            d.Enabled ? $"SuperAdmin and Admin are now limited to {list.Count} approved address(es)." : "The approved-address rule for SuperAdmin and Admin was switched off.", Me, "SuperAdmin", mine);
        return Ok(new { success = true });
    }

    // ---- blocked IPs ----
    [HttpGet("blocked-ips")]
    public async Task<IActionResult> BlockedIps([FromQuery] bool activeOnly = false)
    {
        var now = DateTime.UtcNow;
        var q = _db.BlockedIps.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(b => b.ReleasedAt == null && b.ExpiresAt > now);
        var rows = await q.OrderByDescending(b => b.BlockedAt).Take(200).ToListAsync();
        return Ok(rows.Select(b => new
        {
            b.Id, b.IpAddress, b.Reason, b.BlockedAt, b.ExpiresAt, b.IsManual, b.ReleasedAt,
            active = b.ReleasedAt == null && b.ExpiresAt > now
        }));
    }

    public class BlockIpDto { public string IpAddress { get; set; } = ""; public int Minutes { get; set; } = 60; public string? Reason { get; set; } }

    [HttpPost("blocked-ips")]
    public async Task<IActionResult> Block([FromBody] BlockIpDto d)
    {
        if (!System.Net.IPAddress.TryParse(d.IpAddress?.Trim(), out var ip)) return BadRequest(new { success = false, message = "That is not a valid IP address." });
        var mine = HttpContext.Connection.RemoteIpAddress;
        if (mine != null && mine.Equals(ip)) return BadRequest(new { success = false, message = "You cannot block the IP address you are using." });
        if (d.Minutes is < 1 or > 43200) return BadRequest(new { success = false, message = "Block time must be 1 to 43200 minutes." });
        await _guard.BlockIpAsync(ip.ToString(), string.IsNullOrWhiteSpace(d.Reason) ? "Blocked by staff" : d.Reason.Trim(), true, d.Minutes, Me);
        return Ok(new { success = true });
    }

    [HttpDelete("blocked-ips/{ip}")]
    public async Task<IActionResult> Release(string ip)
    {
        await _guard.ReleaseIpAsync(ip, Me);
        return Ok(new { success = true });
    }

    // ---- accounts ----
    /// <summary>Accounts the system (or staff) deactivated for security reasons and that are still inactive.</summary>
    [HttpGet("flagged-accounts")]
    public async Task<IActionResult> Flagged()
    {
        var since = DateTime.UtcNow.AddDays(-30);
        var evs = await _db.SecurityEvents.AsNoTracking()
            .Where(e => e.Severity == "Critical" && e.ActorUserId != null && e.CreatedAt >= since && e.Summary.Contains("deactivated"))
            .OrderByDescending(e => e.CreatedAt).Take(300).ToListAsync();
        var ids = evs.Select(e => Guid.TryParse(e.ActorUserId, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();

        var admins = await _db.Admins.AsNoTracking().Where(a => ids.Contains(a.Id) && !a.IsActive)
            .Select(a => new { a.Id, a.Email, Name = a.FirstName + " " + a.LastName, a.UserType }).ToListAsync();
        if (!User.IsInRole("SuperAdmin")) admins = admins.Where(a => a.UserType != ShmsBackend.Data.Enums.UserType.SuperAdmin).ToList();
        var portal = await _db.PortalUsers.AsNoTracking().Where(u => ids.Contains(u.Id) && !u.IsActive)
            .Select(u => new { u.Id, u.Email, Name = u.FirstName }).ToListAsync();

        var all = admins.Select(a => (a.Id, a.Email, a.Name, Kind: "Staff")).Concat(portal.Select(u => (u.Id, u.Email, u.Name, Kind: "Client")));
        return Ok(all.Select(a =>
        {
            var e = evs.First(x => x.ActorUserId == a.Id.ToString());
            return new { id = a.Id, email = a.Email, name = a.Name, kind = a.Kind, reason = e.Summary, at = e.CreatedAt, ip = e.IpAddress };
        }).OrderByDescending(x => x.at));
    }

    [HttpPost("accounts/{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
        => await _guard.ReactivateAccountAsync(id, Me) ? Ok(new { success = true }) : NotFound();

    public class DeactivateDto { public string? Reason { get; set; } }

    [HttpPost("accounts/{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, [FromBody] DeactivateDto d)
    {
        if (id.ToString() == Me) return BadRequest(new { success = false, message = "You cannot deactivate your own account." });
        var ok = await _guard.DeactivateAccountManualAsync(id, Me, string.IsNullOrWhiteSpace(d?.Reason) ? "no reason given" : d!.Reason!.Trim());
        return ok ? Ok(new { success = true }) : BadRequest(new { success = false, message = "That account could not be deactivated (a SuperAdmin cannot be deactivated here)." });
    }

    // ---- authenticator app status (staff) ----
    [HttpGet("authenticators")]
    public async Task<IActionResult> Authenticators([FromQuery] string? q, [FromQuery] string? role, [FromQuery] bool? activated,
        [FromQuery] bool recoveryRequested = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 5, 100); page = Math.Max(1, page);
        var query = _db.Admins.AsNoTracking().Where(a => !a.IsDeleted);
        if (!User.IsInRole("SuperAdmin")) query = query.Where(a => a.UserType != ShmsBackend.Data.Enums.UserType.SuperAdmin);   // SuperAdmin accounts are invisible to every other role
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLower();
            query = query.Where(a => a.Email.ToLower().Contains(t) || (a.FirstName + " " + a.LastName).ToLower().Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<ShmsBackend.Data.Enums.UserType>(role, out var rt)) query = query.Where(a => a.UserType == rt);
        if (activated.HasValue) query = query.Where(a => (a.TotpEnabledAt != null) == activated.Value);
        if (recoveryRequested) query = query.Where(a => a.RecoveryRequestedAt != null);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(a => a.RecoveryRequestedAt != null).ThenBy(a => a.UserType).ThenBy(a => a.FirstName)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new { id = a.Id, name = a.FirstName + " " + a.LastName, email = a.Email, role = a.UserType.ToString(), isActive = a.IsActive,
                               authenticatorActivated = a.TotpEnabledAt != null, activatedAt = a.TotpEnabledAt, recoveryRequestedAt = a.RecoveryRequestedAt }).ToListAsync();
        return Ok(new { items, total, page, pageSize });
    }

    /// <summary>For a lost or replaced phone. Nobody resets their own; SuperAdmin resets anyone, Admin resets only Manager, Accountant and Secretary.</summary>
    [HttpPost("accounts/{id:guid}/authenticator-reset")]
    public async Task<IActionResult> ResetAuthenticator(Guid id)
    {
        if (id.ToString() == Me) return BadRequest(new { success = false, message = "You cannot reset your own authenticator." });
        var target = await _db.Admins.FirstOrDefaultAsync(a => a.Id == id && !a.IsDeleted);
        if (target == null) return NotFound();
        var iAmSuper = User.IsInRole("SuperAdmin");
        if (!iAmSuper && target.UserType is ShmsBackend.Data.Enums.UserType.SuperAdmin or ShmsBackend.Data.Enums.UserType.Admin)
            return StatusCode(403, new { success = false, message = "Only a SuperAdmin can reset a SuperAdmin or an Admin." });

        target.TotpSecretEnc = null; target.TotpEnabledAt = null; target.TotpLastStep = 0;
        target.RefreshToken = null; target.RefreshTokenExpiryTime = null; target.RecoveryRequestedAt = null;
        _db.AdminRecoveryCodes.RemoveRange(_db.AdminRecoveryCodes.Where(r => r.AdminId == id));
        foreach (var c in _db.LoginChallenges.Where(c => c.AdminId == id && c.UsedAt == null && !c.Burned)) c.Burned = true;
        await _db.SaveChangesAsync();
        await _events.RecordAsync("Access", "AuthenticatorReset", "Warning",
            $"The authenticator of the {target.UserType} account {target.Email} was reset by staff. The person must set it up again at the next sign-in.",
            Me, User.FindFirstValue(ClaimTypes.Role), null, "Account", id);
        return Ok(new { success = true });
    }

    [HttpGet("sign-in-activity")]
    public async Task<IActionResult> LoginActivity([FromQuery] string? email, [FromQuery] string? ip, [FromQuery] bool? success, [FromQuery] string? role, [FromQuery] int take = 100)
    {
        var q = _db.LoginActivities.AsNoTracking().AsQueryable();
        if (!User.IsInRole("SuperAdmin")) q = q.Where(a => a.Role == null || a.Role != "SuperAdmin");
        if (!string.IsNullOrWhiteSpace(email)) q = q.Where(a => a.Email.Contains(email.Trim().ToLower()));
        if (!string.IsNullOrWhiteSpace(ip)) q = q.Where(a => a.IpAddress.Contains(ip.Trim()));
        if (success.HasValue) q = q.Where(a => a.Success == success.Value);
        if (!string.IsNullOrWhiteSpace(role)) q = q.Where(a => a.Role == role.Trim());
        return Ok(await q.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync());
    }

    // ---- emergency lockdown ----
    [HttpGet("lockdown")]
    public async Task<IActionResult> Lockdown()
    {
        var s = await _guard.GetSettingsAsync(true);
        return Ok(new { active = s.LockdownActive, s.LockdownReason, s.LockdownBy, s.LockdownAt });
    }

    public class LockdownDto { public bool Active { get; set; } public string? Reason { get; set; } }

    [HttpPost("lockdown")]
    public async Task<IActionResult> SetLockdown([FromBody] LockdownDto d)
    {
        var s = await _db.SecuritySettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (s == null) { s = new ShmsBackend.Data.Models.Entities.SecuritySettings(); _db.SecuritySettings.Add(s); }
        var wasActive = s.LockdownActive;
        s.LockdownActive = d.Active;
        s.LockdownReason = d.Active ? (string.IsNullOrWhiteSpace(d.Reason) ? "Emergency lockdown" : d.Reason.Trim()) : null;
        s.LockdownBy = d.Active ? Me : null;
        s.LockdownAt = d.Active ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync();
        _guard.InvalidateSettings();
        await _guard.GetSettingsAsync(true);
        await _events.RecordAsync("System", d.Active ? "LockdownOn" : "LockdownOff", d.Active ? "Critical" : "High",
            d.Active ? $"EMERGENCY LOCKDOWN switched on: {s.LockdownReason}. Only SuperAdmin and Admin can use the system." : "Emergency lockdown switched off. The system is open again.",
            Me, "Staff");
        // Tell everyone when the system goes down and when it is back (only when the state really changed).
        if (d.Active && !wasActive)
            _events.BroadcastToEveryone("We are sorry: the system is temporarily unavailable while we deal with a security issue. It will be restored shortly, and we will notify you as soon as it is back.");
        else if (!d.Active && wasActive)
            _events.BroadcastToEveryone("Good news: the system is back and working normally. Thank you for your patience.");
        return Ok(new { success = true, active = d.Active });
    }
}
