using System.Net;
using Microsoft.Extensions.Caching.Memory;
using ShmsBackend.Data.Models.Entities;

namespace ShmsBackend.Api.Services.Security;

/// <summary>
/// Optional rule: SuperAdmin and Admin accounts may only sign in, and only use the system, from a list of
/// addresses (single IPs or ranges such as 41.90.0.0/16). Off by default. A server-side setting
/// (Security:DisablePrivilegedIpRestriction=true) switches the rule off if the list ever locks everyone out.
/// </summary>
public static class PrivilegedIpRule
{
    public static List<string> Parse(string? raw) =>
        (raw ?? "").Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static bool IsValidEntry(string e) =>
        e.Contains('/') ? IPNetwork.TryParse(e, out _) : IPAddress.TryParse(e, out _);

    private static IPAddress Norm(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;

    public static bool IsAllowed(string? ip, string? raw)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return false;
        addr = Norm(addr);
        foreach (var e in Parse(raw))
        {
            if (e.Contains('/')) { if (IPNetwork.TryParse(e, out var net) && net.Contains(addr)) return true; }
            else if (IPAddress.TryParse(e, out var b) && Norm(b).Equals(addr)) return true;
        }
        return false;
    }

    /// <summary>True when the rule is switched on in settings and not overridden on the server.</summary>
    public static bool Active(SecuritySettings s, IConfiguration cfg) =>
        s.RestrictPrivilegedIps && !cfg.GetValue<bool>("Security:DisablePrivilegedIpRestriction");

    /// <summary>One alert per address per 30 minutes, so a scan cannot flood the bell.</summary>
    public static bool ShouldReport(IMemoryCache cache, string ip)
    {
        var key = "pipref:" + ip;
        if (cache.TryGetValue(key, out _)) return false;
        cache.Set(key, true, TimeSpan.FromMinutes(30));
        return true;
    }
}
