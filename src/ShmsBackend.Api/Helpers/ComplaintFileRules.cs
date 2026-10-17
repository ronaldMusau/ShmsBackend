namespace ShmsBackend.Api.Helpers;

/// <summary>Shared upload rules for every complaint attachment endpoint (creation, inspection, agent evidence).</summary>
public static class ComplaintFileRules
{
    public const long MaxFileSize = 4 * 1024 * 1024; // 4MB
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly HashSet<string> DocExt = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt" };

    /// <summary>Returns an error message, or null when the upload is acceptable.</summary>
    public static string? Validate(IReadOnlyCollection<IFormFile> images, IReadOnlyCollection<IFormFile> documents, int maxEach)
    {
        if (images.Count == 0 && documents.Count == 0) return "Select at least one file to upload.";
        if (images.Count > maxEach) return $"Maximum {maxEach} images allowed.";
        if (documents.Count > maxEach) return $"Maximum {maxEach} documents allowed.";
        foreach (var f in images)
        {
            if (f.Length == 0) return $"{f.FileName} is empty.";
            if (f.Length > MaxFileSize) return $"{f.FileName} exceeds the 4MB limit.";
            if (!ImageExt.Contains(Path.GetExtension(f.FileName))) return $"{f.FileName} is not a supported image (JPG, PNG, WEBP, GIF).";
        }
        foreach (var f in documents)
        {
            if (f.Length == 0) return $"{f.FileName} is empty.";
            if (f.Length > MaxFileSize) return $"{f.FileName} exceeds the 4MB limit.";
            if (!DocExt.Contains(Path.GetExtension(f.FileName))) return $"{f.FileName} is not a supported document (PDF, DOC, DOCX, XLS, XLSX, TXT).";
        }
        return null;
    }
}
