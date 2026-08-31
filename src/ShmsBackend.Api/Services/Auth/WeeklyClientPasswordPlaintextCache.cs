namespace ShmsBackend.Api.Services.Auth;

/// <summary>
/// Holds the CURRENT weekly client-portal support password in plaintext, in memory only, for the
/// lifetime of the current rotation cycle. Registered as a singleton so it survives across request
/// scopes and can be read by an HTTP request (immediate opt-in email) after the background scheduler
/// wrote it. The plaintext is never persisted anywhere — if the process restarts mid-cycle the cache
/// is empty until the next rotation. Completely separate instance from WeeklyPasswordPlaintextCache.
/// </summary>
public interface IWeeklyClientPasswordPlaintextCache
{
    void Set(string plaintext);
    string? Get();
    void Clear();
}

public class WeeklyClientPasswordPlaintextCache : IWeeklyClientPasswordPlaintextCache
{
    private readonly object _lock = new();
    private string? _plaintext;

    public void Set(string plaintext)
    {
        lock (_lock) { _plaintext = plaintext; }
    }

    public string? Get()
    {
        lock (_lock) { return _plaintext; }
    }

    public void Clear()
    {
        lock (_lock) { _plaintext = null; }
    }
}
