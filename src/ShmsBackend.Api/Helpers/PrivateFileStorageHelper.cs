using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ShmsBackend.Api.Helpers;

/// <summary>
/// Shared reader for files written under {cwd}/PrivateUploads/{subfolder}/{guid}{ext} — a folder
/// OUTSIDE wwwroot, so it is never served by UseStaticFiles. Extracted from CompanySettingsController's
/// (and mirrors AgreementService's) private static helpers of the same shape, generalized so any
/// in-process caller (report renderers included) can resolve a stored relative path without an HTTP round-trip.
/// </summary>
public static class PrivateFileStorageHelper
{
    private static readonly Dictionary<string, string> ContentTypeByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    /// <summary>
    /// Maps a DB-stored private relative path ("{subfolder}/{guid}.ext") to a physical file under
    /// {cwd}/PrivateUploads. Tolerates the legacy "/uploads/..." URL fragment and falls back to the
    /// old wwwroot/uploads location so pre-move rows still resolve.
    /// </summary>
    public static string ResolvePrivatePath(string storedPath)
    {
        var rel = storedPath.Replace('\\', '/').TrimStart('/');
        if (rel.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
            rel = rel["uploads/".Length..];

        var priv = Path.Combine(Directory.GetCurrentDirectory(), "PrivateUploads", rel);
        if (File.Exists(priv)) return priv;

        var legacy = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", rel);
        return File.Exists(legacy) ? legacy : priv;
    }

    /// <summary>
    /// Reads a private file's bytes directly off disk (no HTTP round-trip) given its DB-stored relative
    /// path. Returns null if the path is empty or the file doesn't exist on disk.
    /// </summary>
    public static async Task<(byte[] Bytes, string ContentType)?> ReadPrivateFileAsync(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return null;

        var disk = ResolvePrivatePath(storedPath);
        if (!File.Exists(disk)) return null;

        var bytes = await File.ReadAllBytesAsync(disk);
        var ext = Path.GetExtension(disk);
        var contentType = ContentTypeByExt.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
        return (bytes, contentType);
    }
}
