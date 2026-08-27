using System;
using System.Globalization;

namespace ETL_SQL.Reporting.Contracts;

/// <summary>
/// WCAG 2.1 relative luminance and contrast ratio evaluation for report color palettes and visual surfaces.
/// Zero-dependency, purely deterministic calculation.
/// </summary>
public static class ColorContrast
{
    /// <summary>
    /// Parses a 3-, 4-, 6-, or 8-digit hex color into RGBA byte components.
    /// Returns false if the input is malformed or contains unsafe characters.
    /// </summary>
    public static bool TryParseHexColor(string? hex, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = 0;
        a = 255;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var s = hex.Trim();
        if (s.StartsWith("#")) s = s.Substring(1);

        if (s.Length == 3)
        {
            if (!byte.TryParse(new string(s[0], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !byte.TryParse(new string(s[1], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !byte.TryParse(new string(s[2], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
            {
                return false;
            }
            return true;
        }

        if (s.Length == 4)
        {
            if (!byte.TryParse(new string(s[0], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !byte.TryParse(new string(s[1], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !byte.TryParse(new string(s[2], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b) ||
                !byte.TryParse(new string(s[3], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
            {
                return false;
            }
            return true;
        }

        if (s.Length == 6)
        {
            if (!byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
            {
                return false;
            }
            return true;
        }

        if (s.Length == 8)
        {
            if (!byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b) ||
                !byte.TryParse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
            {
                return false;
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates the WCAG 2.1 relative luminance for a given hex color.
    /// Luminance is normalized to [0.0, 1.0] where black is 0.0 and white is 1.0.
    /// </summary>
    public static double CalculateLuminance(string hexColor)
    {
        if (!TryParseHexColor(hexColor, out var r, out var g, out var b, out _))
        {
            return 0.0;
        }

        return CalculateLuminance(r, g, b);
    }

    /// <summary>
    /// Calculates relative luminance from RGB channels according to WCAG 2.1 definition.
    /// </summary>
    public static double CalculateLuminance(byte r, byte g, byte b)
    {
        var rs = LinearizeComponent(r / 255.0);
        var gs = LinearizeComponent(g / 255.0);
        var bs = LinearizeComponent(b / 255.0);

        return 0.2126 * rs + 0.7152 * gs + 0.0722 * bs;
    }

    private static double LinearizeComponent(double c)
    {
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Computes the WCAG contrast ratio between two colors (ranging from 1.0 to 21.0).
    /// If foreground has an alpha channel, composites it over background first.
    /// </summary>
    public static double CalculateContrastRatio(string foreground, string background)
    {
        if (!TryParseHexColor(foreground, out var fr, out var fg, out var fb, out var fa))
            return 1.0;
        if (!TryParseHexColor(background, out var br, out var bg, out var bb, out _))
            return 1.0;

        // Composite foreground over background if alpha < 255
        byte effectiveFr = fr, effectiveFg = fg, effectiveFb = fb;
        if (fa < 255)
        {
            var alpha = fa / 255.0;
            effectiveFr = (byte)Math.Clamp((int)Math.Round(alpha * fr + (1.0 - alpha) * br), 0, 255);
            effectiveFg = (byte)Math.Clamp((int)Math.Round(alpha * fg + (1.0 - alpha) * bg), 0, 255);
            effectiveFb = (byte)Math.Clamp((int)Math.Round(alpha * fb + (1.0 - alpha) * bb), 0, 255);
        }

        var l1 = CalculateLuminance(effectiveFr, effectiveFg, effectiveFb);
        var l2 = CalculateLuminance(br, bg, bb);

        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Evaluates whether a foreground color meets the minimum contrast threshold against a background.
    /// Standard minimum ratio for chart marks and data visualization is 3.0:1 (WCAG 2.1 SC 1.4.11 Non-text Contrast).
    /// </summary>
    public static ContrastEvaluation Evaluate(string foreground, string background, double minRatio = 3.0)
    {
        if (!TryParseHexColor(foreground, out _, out _, out _, out _))
        {
            return new ContrastEvaluation(false, 1.0, $"Foreground '{foreground}' is not a valid hex color.");
        }
        if (!TryParseHexColor(background, out _, out _, out _, out _))
        {
            return new ContrastEvaluation(false, 1.0, $"Background '{background}' is not a valid hex color.");
        }

        var ratio = CalculateContrastRatio(foreground, background);
        var passed = ratio >= minRatio;
        var message = passed
            ? $"Contrast ratio {ratio:F2}:1 meets the required {minRatio:F1}:1 threshold."
            : $"Contrast ratio {ratio:F2}:1 is below the required {minRatio:F1}:1 threshold.";

        return new ContrastEvaluation(passed, ratio, message);
    }
}

/// <summary>
/// Result of a contrast evaluation.
/// </summary>
public record struct ContrastEvaluation(bool Passed, double Ratio, string Diagnostic);
