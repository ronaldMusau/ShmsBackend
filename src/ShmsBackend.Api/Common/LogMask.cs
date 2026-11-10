namespace ShmsBackend.Api.Common;

/// <summary>Masks personal data before it is written to a log, so logs can be shared or kept without exposing people.</summary>
public static class LogMask
{
    /// <summary>ronald@example.com becomes r*****@example.com.</summary>
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(none)";
        var at = email.IndexOf('@');
        if (at <= 0) return "***";
        return email[0] + "*****" + email[at..];
    }

    /// <summary>0712345678 becomes 07******78.</summary>
    public static string Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "(none)";
        var p = phone.Trim();
        return p.Length <= 4 ? "****" : p[..2] + new string('*', p.Length - 4) + p[^2..];
    }
}
