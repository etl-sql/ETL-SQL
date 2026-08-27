using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Reporting;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>
/// The one place a visual's resolved styles and options become <see cref="ThemeSpec"/> tokens and a
/// <see cref="FormattingSpec"/>. Named visuals and <c>CUSTOM</c> charts both lower through here, so a
/// <c>CREATE STYLE</c> theme or a <c>SET REPORT</c> setting cannot reach one authoring path and miss
/// the other.
/// </summary>
internal static class ChartStyleTokens
{
    /// <summary>Visual-level option carrying the most specific NULL label.</summary>
    private const string NullLabelOption = "NULL_LABEL";

    /// <summary>Options that configure layout rather than presentation, and are handled elsewhere.</summary>
    private static readonly string[] Excluded = ["STACKED"];

    /// <summary>Builds the resolved theme for a visual: its declared theme name plus every style token.</summary>
    public static ThemeSpec Theme(VisualManifest manifest) =>
        new(manifest.Styles?.GetValueOrDefault("THEME") ?? "default", Build(manifest));

    /// <summary>
    /// Resolves the report formatting for a visual. Precedence, most specific first:
    /// visual <c>OPTIONS (NULL_LABEL)</c>, then <c>SET REPORT</c>, then configuration, then the fallback.
    /// </summary>
    public static FormattingSpec Formatting(
        IExecutionContext? context,
        VisualManifest manifest,
        ImmutableArray<FieldFormat> fields)
    {
        var effective = context?.ReportContext?.EffectiveFormatting ?? ReportFormattingSettings.Default;
        var nullLabel = manifest.Options.GetValueOrDefault(NullLabelOption) ?? effective.NullLabel;
        return new FormattingSpec(effective.Locale, effective.TimeZone, nullLabel,
            fields.IsDefault ? [] : fields);
    }

    /// <summary>Styles first, then options, then palette tokens, with options winning a name collision; ordered for stable hashes.</summary>
    public static ImmutableArray<StyleToken> Build(VisualManifest manifest)
    {
        var styleTokens = (manifest.Styles ?? new Dictionary<string, string>())
            .Where(pair => !IsExcluded(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new StyleToken(pair.Key, pair.Value));

        var optionTokens = manifest.Options
            .Where(pair => !IsExcluded(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new StyleToken(pair.Key, pair.Value));

        var paletteTokens = manifest.Palette is { Count: > 0 }
            ? manifest.Palette.Select((color, i) => new StyleToken($"PALETTE:{i}", color))
            : Enumerable.Empty<StyleToken>();

        return styleTokens
            .Concat(optionTokens)
            .Concat(paletteTokens)
            .GroupBy(token => token.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(token => token.Name, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static bool IsExcluded(string name) =>
        Excluded.Any(excluded => name.Equals(excluded, StringComparison.OrdinalIgnoreCase));
}
