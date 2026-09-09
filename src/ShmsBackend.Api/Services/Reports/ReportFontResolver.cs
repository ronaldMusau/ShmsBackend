using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Fonts;

namespace ShmsBackend.Api.Services.Reports;

/// <summary>
/// PdfSharp 6.x/MigraDoc dropped GDI+ font access to stay cross-platform, so a font resolver must be
/// registered explicitly even on Windows — without one, rendering throws before the first page is laid
/// out. This resolver reads the actual .ttf files straight off disk from the Windows Fonts folder (plain
/// file I/O, not System.Drawing/GDI+), covering every family name MigraDoc itself falls back to
/// internally (e.g. its predefined "error font" uses Courier New) plus the plain body/heading fonts this
/// app's reports use. Falls back to Arial for any unrecognized family so a future font name added to a
/// report template can't crash rendering.
/// </summary>
public class ReportFontResolver : IFontResolver
{
    private static readonly string FontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    private static readonly Dictionary<string, (string Regular, string Bold)> FamilyFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = ("arial.ttf", "arialbd.ttf"),
            ["Segoe UI"] = ("segoeui.ttf", "segoeuib.ttf"),
            ["Courier New"] = ("cour.ttf", "courbd.ttf"),
            ["Times New Roman"] = ("times.ttf", "timesbd.ttf"),
            ["Verdana"] = ("verdana.ttf", "verdanab.ttf"),
        };

    public string DefaultFontName => "Arial";

    public byte[]? GetFont(string faceName)
    {
        var path = Path.Combine(FontsDir, faceName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var (regular, bold) = FamilyFiles.TryGetValue(familyName, out var files)
            ? files
            : FamilyFiles["Arial"];

        var fileName = isBold ? bold : regular;
        return new FontResolverInfo(fileName);
    }
}
