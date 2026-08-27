using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ETL_SQL.Reporting.Contracts;

namespace ETL_SQL.Reporting.Semantics;

/// <summary>
/// Resolves authored STYLE and THEME properties into validated, scoped --etl-* design token dictionaries.
/// </summary>
public static class DesignTokenResolver
{
    private static readonly Regex PxRegex = new(@"^\s*(\d+(?:\.\d+)?)\s*px\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Sanitizes a series identity string for CSS variable token naming.
    /// </summary>
    public static string SanitizeSeriesTokenName(string seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesName)) return "unnamed";
        var s = seriesName.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9]+", "-");
        s = s.Trim('-', '_');
        if (string.IsNullOrEmpty(s)) return "series";
        if (char.IsDigit(s[0])) s = "s-" + s;
        return s;
    }

    /// <summary>
    /// Resolves style dictionary key-value pairs into normalized --etl-* tokens.
    /// Only returns overrides explicitly defined or derived from the supplied styles.
    /// </summary>
    public static Dictionary<string, string> ResolveScopedTokens(
        IReadOnlyDictionary<string, string>? styles,
        bool isPageOrReportLevel = false,
        IReadOnlyList<string>? palette = null,
        IEnumerable<KeyValuePair<string, string>>? seriesAssignments = null)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (palette is { Count: > 0 })
        {
            for (var i = 0; i < palette.Count; i++)
            {
                var color = palette[i];
                if (DesignTokens.IsSafeCssValue(color) && ColorContrast.TryParseHexColor(color, out _, out _, out _, out _))
                {
                    tokens[$"--etl-color-{i + 1}"] = color;
                    tokens[$"--etl-palette-{i + 1}"] = color;
                }
            }
        }

        // Resolved series assignments (both explicit and palette-derived)
        if (seriesAssignments != null)
        {
            var usedTokenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sortedAssignments = seriesAssignments
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var (seriesName, color) in sortedAssignments)
            {
                if (!DesignTokens.IsSafeCssValue(color) || !ColorContrast.TryParseHexColor(color, out _, out _, out _, out _))
                    continue;

                var baseSanitized = SanitizeSeriesTokenName(seriesName);
                var tokenName = $"--etl-series-{baseSanitized}";

                if (usedTokenNames.Contains(tokenName))
                {
                    var suffix = 2;
                    while (usedTokenNames.Contains($"--etl-series-{baseSanitized}-{suffix}"))
                    {
                        suffix++;
                    }
                    tokenName = $"--etl-series-{baseSanitized}-{suffix}";
                }

                usedTokenNames.Add(tokenName);
                if (DesignTokens.IsAllowedTokenName(tokenName))
                {
                    tokens[tokenName] = color;
                }
            }
        }

        if (styles == null || styles.Count == 0)
            return tokens;

        foreach (var (rawKey, rawValue) in styles)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || string.IsNullOrWhiteSpace(rawValue))
                continue;

            var key = rawKey.Trim();
            var value = rawValue.Trim();

            // Direct --etl-* custom properties
            if (key.StartsWith("--etl-", StringComparison.OrdinalIgnoreCase))
            {
                if (DesignTokens.IsAllowedTokenName(key) && DesignTokens.IsSafeCssValue(value))
                {
                    tokens[key.ToLowerInvariant()] = value;
                }
                continue;
            }

            // Explicit series color: COLOR:<series> -> --etl-series-<series> (if not already handled)
            if (key.StartsWith("COLOR:", StringComparison.OrdinalIgnoreCase))
            {
                var seriesName = key.Substring("COLOR:".Length).Trim();
                if (!string.IsNullOrEmpty(seriesName) && DesignTokens.IsSafeCssValue(value) && ColorContrast.TryParseHexColor(value, out _, out _, out _, out _))
                {
                    var sanitized = SanitizeSeriesTokenName(seriesName);
                    var seriesToken = $"--etl-series-{sanitized}";
                    if (DesignTokens.IsAllowedTokenName(seriesToken) && !tokens.ContainsKey(seriesToken))
                    {
                        tokens[seriesToken] = value;
                    }
                }
                continue;
            }

            // Standard ETL-SQL STYLE / THEME property mappings
            switch (key.ToUpperInvariant())
            {
                case "BACKGROUND":
                case "BACKGROUND_COLOR":
                case "BACKGROUND-COLOR":
                case "SURFACE_CARD":
                case "SURFACE-CARD":
                case "ETL_SURFACE_CARD":
                case "ETL-SURFACE-CARD":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.SurfaceCard] = value;
                        tokens[DesignTokens.Surface] = value;
                        if (isPageOrReportLevel)
                            tokens[DesignTokens.Bg] = value;
                    }
                    break;

                case "BG":
                case "BG_COLOR":
                case "PAGE_BACKGROUND":
                case "REPORT_BACKGROUND":
                case "ETL_BG":
                case "ETL-BG":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.Bg] = value;
                    }
                    break;

                case "COLOR":
                case "FONT_COLOR":
                case "FONT-COLOR":
                case "TEXT_COLOR":
                case "TEXT-COLOR":
                case "TEXT_PRIMARY":
                case "TEXT-PRIMARY":
                case "ETL_TEXT_PRIMARY":
                case "ETL-TEXT-PRIMARY":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.TextPrimary] = value;
                        tokens[DesignTokens.Text] = value;
                    }
                    break;

                case "MUTED_COLOR":
                case "MUTED-COLOR":
                case "TEXT_MUTED":
                case "TEXT-MUTED":
                case "SECONDARY_COLOR":
                case "SUBTITLE_COLOR":
                case "ETL_TEXT_MUTED":
                case "ETL-TEXT-MUTED":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.TextMuted] = value;
                        tokens[DesignTokens.TextSecondary] = value;
                    }
                    break;

                case "BORDER":
                case "BORDER_COLOR":
                case "BORDER-COLOR":
                case "ETL_BORDER":
                case "ETL-BORDER":
                    var borderColor = ExtractBorderColor(value);
                    if (borderColor != null && DesignTokens.IsSafeCssValue(borderColor))
                    {
                        tokens[DesignTokens.Border] = borderColor;
                    }
                    break;

                case "ACCENT":
                case "ACCENT_COLOR":
                case "ACCENT-COLOR":
                case "PRIMARY":
                case "PRIMARY_COLOR":
                case "BRAND_PRIMARY":
                case "ETL_ACCENT":
                case "ETL-ACCENT":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.Accent] = value;
                    }
                    break;

                case "SUCCESS":
                case "SUCCESS_COLOR":
                case "SUCCESS-COLOR":
                case "ETL_SUCCESS":
                case "ETL-SUCCESS":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.Success] = value;
                    }
                    break;

                case "WARNING":
                case "WARNING_COLOR":
                case "WARNING-COLOR":
                case "ETL_WARNING":
                case "ETL-WARNING":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.Warning] = value;
                    }
                    break;

                case "DANGER":
                case "DANGER_COLOR":
                case "DANGER-COLOR":
                case "ERROR_COLOR":
                case "ETL_DANGER":
                case "ETL-DANGER":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.Danger] = value;
                    }
                    break;

                case "INFO":
                case "INFO_COLOR":
                case "INFO-COLOR":
                case "ETL_INFO":
                case "ETL-INFO":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.Info] = value;
                    }
                    break;

                case "BORDER_RADIUS":
                case "BORDER-RADIUS":
                case "RADIUS":
                case "ETL_RADIUS":
                case "ETL-RADIUS":
                case "ETL_RADIUS_MD":
                case "ETL-RADIUS-MD":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.RadiusMd] = value;
                        tokens[DesignTokens.Radius] = value;
                        var pxMatch = PxRegex.Match(value);
                        if (pxMatch.Success && double.TryParse(pxMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
                        {
                            tokens[DesignTokens.RadiusSm] = $"{Math.Max(0, Math.Round(px * 0.5))}px";
                            tokens[DesignTokens.RadiusLg] = $"{Math.Round(px * 1.5)}px";
                        }
                    }
                    break;

                case "RADIUS_SM":
                case "RADIUS-SM":
                case "BORDER_RADIUS_SM":
                case "ETL_RADIUS_SM":
                case "ETL-RADIUS-SM":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.RadiusSm] = value;
                    }
                    break;

                case "RADIUS_MD":
                case "RADIUS-MD":
                case "BORDER_RADIUS_MD":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.RadiusMd] = value;
                        tokens[DesignTokens.Radius] = value;
                    }
                    break;

                case "RADIUS_LG":
                case "RADIUS-LG":
                case "BORDER_RADIUS_LG":
                case "ETL_RADIUS_LG":
                case "ETL-RADIUS-LG":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.RadiusLg] = value;
                    }
                    break;

                case "SHADOW":
                case "BOX_SHADOW":
                case "BOX-SHADOW":
                case "ETL_SHADOW":
                case "ETL-SHADOW":
                    var shadowVal = string.Equals(value, "ON", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase)
                        ? "0 6px 18px rgba(15, 23, 42, 0.16)"
                        : (string.Equals(value, "OFF", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "NONE", StringComparison.OrdinalIgnoreCase) || value == "0")
                            ? "none"
                            : value;
                    if (DesignTokens.IsSafeCssValue(shadowVal))
                    {
                        tokens[DesignTokens.Shadow] = shadowVal;
                    }
                    break;

                case "FONT":
                case "FONT_FAMILY":
                case "FONT-FAMILY":
                case "ETL_FONT_FAMILY":
                case "ETL-FONT-FAMILY":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.FontFamily] = value;
                    }
                    break;

                case "FONT_MONO":
                case "FONT-MONO":
                case "ETL_FONT_MONO":
                case "ETL-FONT-MONO":
                    if (DesignTokens.IsSafeCssValue(value))
                    {
                        tokens[DesignTokens.FontMono] = value;
                    }
                    break;
            }
        }

        return tokens;
    }

    private static readonly HashSet<string> BorderStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "hidden", "solid", "dashed", "dotted", "double", "groove", "ridge", "inset", "outset"
    };

    private static readonly HashSet<string> BorderWidths = new(StringComparer.OrdinalIgnoreCase)
    {
        "thin", "medium", "thick"
    };

    private static readonly Regex ColorFuncRegex = new(@"\b(?:rgb|rgba|hsl|hsla)\s*\([^)]+\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HexColorRegex = new(@"#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\b", RegexOptions.Compiled);

    public static string? ExtractBorderColor(string? borderValue)
    {
        if (string.IsNullOrWhiteSpace(borderValue))
            return null;

        var val = borderValue.Trim().Trim('\'', '"').Trim();
        if (val.Equals("none", StringComparison.OrdinalIgnoreCase) || val == "0" || val.Equals("hidden", StringComparison.OrdinalIgnoreCase))
            return "transparent";

        var funcMatch = ColorFuncRegex.Match(val);
        if (funcMatch.Success)
            return funcMatch.Value;

        var hexMatch = HexColorRegex.Match(val);
        if (hexMatch.Success)
            return hexMatch.Value;

        var parts = val.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0];

        foreach (var part in parts)
        {
            if (!BorderStyles.Contains(part) &&
                !BorderWidths.Contains(part) &&
                !Regex.IsMatch(part, @"^\s*(\d+(?:\.\d+)?)\s*(px|em|rem|pt|%)?\s*$", RegexOptions.IgnoreCase))
            {
                return part;
            }
        }

        return val;
    }
}
