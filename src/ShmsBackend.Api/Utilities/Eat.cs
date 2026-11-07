namespace ShmsBackend.Api.Utilities;

/// <summary>
/// East Africa Time (UTC+3, no daylight saving). All stored times are UTC; anything the server
/// writes into text itself (emails, notification wording, report headers) must go through here
/// so people read Nairobi time, not UTC. Screens do not need this: the API sends a "Z" marker
/// and the browser converts to the device's own time zone.
/// </summary>
public static class Eat
{
    public static DateTime From(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(3);
}
