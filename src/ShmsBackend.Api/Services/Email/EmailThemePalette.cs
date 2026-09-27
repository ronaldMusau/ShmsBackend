using System;
using System.Collections.Generic;

namespace ShmsBackend.Api.Services.Email;

public class EmailThemePalette
{
    public string BgBase { get; init; } = "#080808";
    public string BgCard { get; init; } = "#161616";
    public string BgElevated { get; init; } = "#1e1e1e";
    public string TextPrimary { get; init; } = "#FFFFFF";
    public string TextSecondary { get; init; } = "rgba(255,255,255,0.7)";
    public string TextMuted { get; init; } = "rgba(255,255,255,0.45)";
    public string Success { get; init; } = "#10B981";
    public string Error { get; init; } = "#EF4444";
    public string AccentPrimary { get; init; } = "#D4AF37";
    public string AccentSecondary { get; init; } = "#AA8C2F";
    public string TextOnAccent { get; init; } = "#000000";
    public string AccentGlow { get; init; } = "rgba(212,175,55,0.12)";
    public string AccentBorder { get; init; } = "rgba(212,175,55,0.3)";
}

/// <summary>
/// Fixed reference data mirroring the frontend's 21-theme system (theme.service.ts / gold-theme.css).
/// Every dark theme shares one background/text/success/error base (only the accent hue changes);
/// light mode has its own separate base. Vintage is the one theme with a mode-dependent accent pair.
/// </summary>
public static class EmailThemeCatalog
{
    private sealed record ModeBase(
        string BgBase, string BgCard, string BgElevated,
        string TextPrimary, string TextSecondary, string TextMuted,
        string Success, string Error);

    private sealed record AccentPair(string Primary, string Secondary);

    private static readonly ModeBase DarkBase = new(
        "#080808", "#161616", "#1e1e1e",
        "#FFFFFF", "rgba(255,255,255,0.7)", "rgba(255,255,255,0.45)",
        "#10B981", "#EF4444");

    private static readonly ModeBase LightBase = new(
        "#F5F3EE", "#FFFFFF", "#FAFAF8",
        "#1A1A1A", "rgba(26,26,26,0.75)", "rgba(26,26,26,0.5)",
        "#059669", "#DC2626");

    // Exact hex pairs confirmed against gold-theme.css's [data-theme="X"] blocks.
    private static readonly Dictionary<string, AccentPair> Accents = new()
    {
        ["gold-dark"] = new("#D4AF37", "#AA8C2F"),
        ["light"] = new("#C9A227", "#9A7B1E"),
        ["ocean"] = new("#0EA5E9", "#0369A1"),
        ["emerald"] = new("#14A87E", "#0D7A5C"),
        ["amber"] = new("#F59E0B", "#B45309"),
        ["forest"] = new("#10B981", "#065F46"),
        ["rose"] = new("#F43F5E", "#BE123C"),
        ["mono"] = new("#E5E5E5", "#A3A3A3"),
        ["default"] = new("#635BFF", "#4A42D6"),
        ["modern"] = new("#3B82F6", "#1D4ED8"),
        ["bold-tech"] = new("#8B5CF6", "#6D28D9"),
        ["caffeine"] = new("#C2813F", "#92400E"),
        ["nature"] = new("#65A30D", "#3F6212"),
        ["notebook"] = new("#D97706", "#92400E"),
        ["retro-arcade"] = new("#EC4899", "#BE185D"),
        ["soft-pop"] = new("#7C3AED", "#5B21B6"),
        ["starry-night"] = new("#3B82F6", "#1D4ED8"),
        ["tangerine"] = new("#F97316", "#C2410C"),
        ["vintage"] = new("#B45309", "#78350F"),
        ["twitter"] = new("#1DA1F2", "#0284C7"),
        ["mocha"] = new("#A0714F", "#6B4226"),
    };

    // Vintage is the one theme whose accent pair depends on mode ([data-theme="vintage"][data-mode="dark"]
    // overrides the default/light pair defined on [data-theme="vintage"] alone).
    private static readonly AccentPair VintageDarkMode = new("#D08A4C", "#B4700F");

    public static EmailThemePalette Resolve(string? theme, string? mode)
    {
        var resolvedTheme = string.IsNullOrWhiteSpace(theme) ? "default" : theme;
        var resolvedMode = string.IsNullOrWhiteSpace(mode) ? "dark" : mode;

        var modeBase = resolvedMode == "light" ? LightBase : DarkBase;

        var accent = resolvedTheme == "vintage" && resolvedMode == "dark"
            ? VintageDarkMode
            : Accents.GetValueOrDefault(resolvedTheme, Accents["default"]);

        return new EmailThemePalette
        {
            BgBase = modeBase.BgBase,
            BgCard = modeBase.BgCard,
            BgElevated = modeBase.BgElevated,
            TextPrimary = modeBase.TextPrimary,
            TextSecondary = modeBase.TextSecondary,
            TextMuted = modeBase.TextMuted,
            Success = modeBase.Success,
            Error = modeBase.Error,
            AccentPrimary = accent.Primary,
            AccentSecondary = accent.Secondary,
            TextOnAccent = ComputeTextOnAccent(accent.Primary),
            // Same alpha values gold-theme.css's --accent-glow (0.12) and --accent-border (0.3) use,
            // derived from each resolved theme's own accent instead of staying fixed to gold.
            AccentGlow = HexToRgba(accent.Primary, 0.12),
            AccentBorder = HexToRgba(accent.Primary, 0.3)
        };
    }

    private static string HexToRgba(string hex, double alpha)
    {
        var (r, g, b) = ParseHex(hex);
        return $"rgba({r},{g},{b},{alpha.ToString(System.Globalization.CultureInfo.InvariantCulture)})";
    }

    // Standard WCAG relative-luminance formula — picks whichever of #000000/#FFFFFF gives the
    // genuinely better contrast ratio against AccentPrimary, rather than assuming.
    private static string ComputeTextOnAccent(string accentHex)
    {
        var (r, g, b) = ParseHex(accentHex);
        var luminance = RelativeLuminance(r, g, b);

        var contrastWithBlack = ContrastRatio(luminance, 0.0);
        var contrastWithWhite = ContrastRatio(luminance, 1.0);

        return contrastWithBlack >= contrastWithWhite ? "#000000" : "#FFFFFF";
    }

    private static double RelativeLuminance(int r, int g, int b)
    {
        double Channel(int c)
        {
            var srgb = c / 255.0;
            return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double ContrastRatio(double luminanceA, double luminanceB) =>
        (Math.Max(luminanceA, luminanceB) + 0.05) / (Math.Min(luminanceA, luminanceB) + 0.05);

    private static (int R, int G, int B) ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        return (
            Convert.ToInt32(hex.Substring(0, 2), 16),
            Convert.ToInt32(hex.Substring(2, 2), 16),
            Convert.ToInt32(hex.Substring(4, 2), 16));
    }
}
