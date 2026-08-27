using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ETL_SQL.Reporting.Contracts;

/// <summary>
/// Authoritative public contract for ETL-SQL report design tokens.
/// Resolves report, page, container, and visual styles into scoped --etl-* CSS custom properties.
/// </summary>
public static class DesignTokens
{
    // ── Public Token Names ──────────────────────────────────────────────────

    public const string SurfaceCard = "--etl-surface-card";
    public const string Surface = "--etl-surface";
    public const string Bg = "--etl-bg";

    public const string TextPrimary = "--etl-text-primary";
    public const string TextMuted = "--etl-text-muted";
    public const string Text = "--etl-text";
    public const string TextSecondary = "--etl-text-secondary";

    public const string Border = "--etl-border";
    public const string Shadow = "--etl-shadow";

    public const string Accent = "--etl-accent";
    public const string Success = "--etl-success";
    public const string Warning = "--etl-warning";
    public const string Danger = "--etl-danger";
    public const string Info = "--etl-info";

    public const string RadiusSm = "--etl-radius-sm";
    public const string RadiusMd = "--etl-radius-md";
    public const string RadiusLg = "--etl-radius-lg";
    public const string Radius = "--etl-radius";

    public const string FontFamily = "--etl-font-family";
    public const string FontMono = "--etl-font-mono";

    // ── All Known Allowed Public Tokens ─────────────────────────────────────

    public static readonly HashSet<string> AllTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        SurfaceCard, Surface, Bg,
        TextPrimary, TextMuted, Text, TextSecondary,
        Border, Shadow,
        Accent, Success, Warning, Danger, Info,
        RadiusSm, RadiusMd, RadiusLg, Radius,
        FontFamily, FontMono
    };

    // ── Built-in Light Fallback Tokens ──────────────────────────────────────

    public static IReadOnlyDictionary<string, string> LightTokens { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [Bg] = "#f5f5f5",
        [Surface] = "#ffffff",
        [SurfaceCard] = "#ffffff",
        [Text] = "#0f172a",
        [TextPrimary] = "#0f172a",
        [TextMuted] = "#64748b",
        [TextSecondary] = "#64748b",
        [Border] = "#e2e8f0",
        [Shadow] = "0 1px 3px rgba(0, 0, 0, 0.1)",
        [Accent] = "#2563eb",
        [Success] = "#16a34a",
        [Warning] = "#eab308",
        [Danger] = "#dc2626",
        [Info] = "#0284c7",
        [RadiusSm] = "4px",
        [RadiusMd] = "8px",
        [RadiusLg] = "12px",
        [Radius] = "8px",
        [FontFamily] = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
        [FontMono] = "ui-monospace, SFMono-Regular, 'SF Mono', Menlo, Consolas, monospace"
    };

    // ── Built-in Dark Fallback Tokens ───────────────────────────────────────

    public static IReadOnlyDictionary<string, string> DarkTokens { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [Bg] = "#1e1e1e",
        [Surface] = "#252526",
        [SurfaceCard] = "#252526",
        [Text] = "#f8fafc",
        [TextPrimary] = "#f8fafc",
        [TextMuted] = "#94a3b8",
        [TextSecondary] = "#94a3b8",
        [Border] = "#334155",
        [Shadow] = "0 4px 6px -1px rgba(0, 0, 0, 0.3)",
        [Accent] = "#3b82f6",
        [Success] = "#22c55e",
        [Warning] = "#f59e0b",
        [Danger] = "#ef4444",
        [Info] = "#38bdf8",
        [RadiusSm] = "4px",
        [RadiusMd] = "8px",
        [RadiusLg] = "12px",
        [Radius] = "8px",
        [FontFamily] = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif",
        [FontMono] = "ui-monospace, SFMono-Regular, 'SF Mono', Menlo, Consolas, monospace"
    };

    // ── Safety Validation Rules ─────────────────────────────────────────────

    private static readonly Regex UnsafeCssPattern = new(
        @"@import|@font-face|expression\s*\(|-moz-binding|behavior\s*:|javascript\s*:|vbscript\s*:|data\s*:|url\s*\(|var\s*\(\s*--(?!etl-)|[;{}\\\0\r\n\f\v<>/\*]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DynamicSeriesOrPalettePattern = new(
        @"^--etl-(?:color-\d+|palette-\d+|series-[a-z0-9_-]+|color-series-[a-z0-9_-]+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Validates whether a token name belongs to the approved --etl-* public design token contract.
    /// Strictly rejects host-private variables like --portal-* as well as arbitrary undeclared tokens.
    /// </summary>
    public static bool IsAllowedTokenName(string? tokenName)
    {
        if (string.IsNullOrWhiteSpace(tokenName)) return false;
        var trimmed = tokenName.Trim();
        if (AllTokens.Contains(trimmed)) return true;
        return DynamicSeriesOrPalettePattern.IsMatch(trimmed);
    }

    /// <summary>
    /// Validates whether a CSS value string is safe for custom property serialization.
    /// Rejects injections, control characters, external urls, expressions, and forbidden host variables.
    /// </summary>
    public static bool IsSafeCssValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var trimmed = value.Trim();
        if (trimmed.Length > 256) return false;
        if (UnsafeCssPattern.IsMatch(trimmed)) return false;
        return true;
    }
}
