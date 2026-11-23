using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Functions;

/// <summary>
/// Shared text cleaning and standardization engine used by scalar <c>CLEAN_TEXT</c> /
/// <c>STANDARDIZE</c> functions and <c>TRANSFORM ... USING CLEAN</c>.
/// </summary>
public static partial class TextCleaningEngine
{
    public const string DefaultRulesSpec = "STRIP_CONTROL, COLLAPSE_WHITESPACE, TRIM";

    private static readonly string[] DefaultRules = ["STRIP_CONTROL", "COLLAPSE_WHITESPACE", "TRIM"];

    private static readonly HashSet<string> SupportedRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRIM",
        "COLLAPSE_WHITESPACE",
        "STRIP_CONTROL",
        "NULL_IF_EMPTY",
        "UPPER",
        "LOWER",
        "TITLE",
        "INITCAP",
        "PHONE",
        "EMAIL",
        "POSTAL_CODE"
    };

    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "PHONE",
        "EMAIL",
        "POSTAL_CODE"
    };

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRunRegex();

    [GeneratedRegex(@"^[A-Z]\d[A-Z]\d[A-Z]\d$", RegexOptions.CultureInvariant)]
    private static partial Regex CanadianPostalRegex();

    public static IReadOnlyList<string> ParseRules(string? rulesSpec)
    {
        if (string.IsNullOrWhiteSpace(rulesSpec))
            return DefaultRules;

        var parsed = rulesSpec
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.ToUpperInvariant())
            .ToList();

        if (parsed.Count == 0)
            return DefaultRules;

        foreach (var rule in parsed)
        {
            if (!SupportedRules.Contains(rule))
            {
                throw new ExecutionException(
                    $"Unsupported CLEAN_TEXT rule '{rule}'. Supported rules: TRIM, COLLAPSE_WHITESPACE, STRIP_CONTROL, NULL_IF_EMPTY, UPPER, LOWER, TITLE, INITCAP, PHONE, EMAIL, POSTAL_CODE.");
            }
        }

        return parsed;
    }

    public static string? ApplyRules(string? value, IReadOnlyList<string> rules)
    {
        if (value == null) return null;

        string? current = value;
        foreach (var rule in rules)
        {
            if (current == null) return null;
            current = rule switch
            {
                "TRIM" => current.Trim(),
                "COLLAPSE_WHITESPACE" => WhitespaceRunRegex().Replace(current, " "),
                "STRIP_CONTROL" => StripControlCharacters(current),
                "NULL_IF_EMPTY" => current.Length == 0 ? null : current,
                "UPPER" => current.ToUpperInvariant(),
                "LOWER" => current.ToLowerInvariant(),
                "TITLE" or "INITCAP" => ToInitCap(current),
                "PHONE" => StandardizePhone(current),
                "EMAIL" => StandardizeEmail(current),
                "POSTAL_CODE" => StandardizePostalCode(current),
                _ => throw new ExecutionException($"Unsupported CLEAN_TEXT rule '{rule}'.")
            };
        }

        return current;
    }

    public static string? Standardize(string? value, string? format)
    {
        if (value == null || format == null) return null;
        var normalizedFormat = format.Trim().ToUpperInvariant();
        if (!SupportedFormats.Contains(normalizedFormat))
        {
            throw new ExecutionException(
                $"Unsupported STANDARDIZE format '{format}'. Supported formats: PHONE, EMAIL, POSTAL_CODE.");
        }

        return normalizedFormat switch
        {
            "PHONE" => StandardizePhone(value),
            "EMAIL" => StandardizeEmail(value),
            "POSTAL_CODE" => StandardizePostalCode(value),
            _ => throw new ExecutionException($"Unsupported STANDARDIZE format '{format}'.")
        };
    }

    private static string StripControlCharacters(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            // Keep standard whitespace (\t, \r, \n) so COLLAPSE_WHITESPACE can collapse words cleanly.
            if ((char.IsControl(c) && !char.IsWhiteSpace(c)) ||
                c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
            {
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ToInitCap(string text)
    {
        var chars = new char[text.Length];
        var inWord = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            chars[i] = inWord ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c);
            inWord = char.IsLetterOrDigit(c);
        }
        return new string(chars);
    }

    private static string? StandardizePhone(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        bool hasLeadingPlus = trimmed[0] == '+';

        var digits = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (char.IsAsciiDigit(c))
                digits.Append(c);
        }

        if (digits.Length == 0) return null;
        var d = digits.ToString();
        if (d.Length == 10)
            return $"{d[..3]}-{d[3..6]}-{d[6..]}";
        if (d.Length == 11 && d[0] == '1')
            return $"+1-{d[1..4]}-{d[4..7]}-{d[7..]}";
        return hasLeadingPlus ? $"+{d}" : d;
    }

    private static string? StandardizeEmail(string value)
    {
        var cleaned = StripControlCharacters(value).Trim().ToLowerInvariant();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static string? StandardizePostalCode(string value)
    {
        var cleaned = WhitespaceRunRegex().Replace(StripControlCharacters(value).Trim(), " ").ToUpperInvariant();
        if (cleaned.Length == 0) return null;

        var compact = cleaned.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (compact.Length == 5 && compact.All(char.IsAsciiDigit))
            return compact;
        if (compact.Length == 9 && compact.All(char.IsAsciiDigit))
            return $"{compact[..5]}-{compact[5..]}";
        if (CanadianPostalRegex().IsMatch(compact))
            return $"{compact[..3]} {compact[3..]}";

        return cleaned;
    }
}
