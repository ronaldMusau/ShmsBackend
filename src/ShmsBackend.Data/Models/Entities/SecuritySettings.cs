using System;

namespace ShmsBackend.Data.Models.Entities;

/// <summary>Single row (the first by Id). Every threshold used by the threat guard is editable, none is hard-coded.</summary>
public class SecuritySettings
{
    public int Id { get; set; }

    // --- IP rules ---
    public int IpFailThreshold { get; set; } = 20;            // failed logins from one IP within the window
    public int IpDistinctAccountsThreshold { get; set; } = 8; // different accounts failed from one IP within the window
    public int IpFailWindowMinutes { get; set; } = 10;
    public int SuperAdminFailThreshold { get; set; } = 3;     // failed sign-ins on one SuperAdmin account within the window that raise a Critical alert (the account is never locked by this)
    public int RecoveryRequestCooldownMinutes { get; set; } = 60;   // one emergency-recovery request per account per this many minutes
    public int SharedIpFactor { get; set; } = 3;              // thresholds x this when the IP looks shared (many real users)
    public int NotFoundThreshold { get; set; } = 40;         // 404s from one IP within the window
    public int NotFoundWindowMinutes { get; set; } = 5;
    public int RateLimitedThreshold { get; set; } = 25;      // 429s from one IP within the window
    public int RateLimitedWindowMinutes { get; set; } = 5;
    public int BlockMinutesBase { get; set; } = 15;          // first block; each repeat within 24 h multiplies by 4
    public int BlockMaxHours { get; set; } = 24;
    public int EscalateAfterBlocks { get; set; } = 3;        // blocks of one IP within 24 h before accounts that used it are deactivated
    public bool BlockPrivateIps { get; set; } = false;       // loopback/private addresses are normally a proxy, never block them

    // --- Account rules ---
    public int SprayDistinctAccounts { get; set; } = 5;      // IP failed on this many accounts, then one succeeded -> likely taken over
    public int DistinctIpsThreshold { get; set; } = 5;       // one account used from this many IPs within the window
    public int DistinctIpsWindowMinutes { get; set; } = 10;
    public int ForbiddenThreshold { get; set; } = 8;         // 403s for one signed-in user within the window
    public int ForbiddenWindowMinutes { get; set; } = 10;
    public int ReadsPerMinuteThreshold { get; set; } = 400;  // GET requests per minute for one signed-in user
    public int AccountStrikesBeforeDeactivate { get; set; } = 2; // 1 = deactivate on the first breach, 2 = warn first (default)
    public bool AutoDeactivateEnabled { get; set; } = true;

    // --- Emergency lockdown ---
    public bool LockdownActive { get; set; }
    public string? LockdownReason { get; set; }
    public string? LockdownBy { get; set; }
    public DateTime? LockdownAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
