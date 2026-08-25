using System;
using System.Collections.Immutable;
using System.Linq;

namespace ETL_SQL.Reporting.Semantics;

/// <summary>
/// The one series-colour rule in the reporting layer.
///
/// <c>PlotPlanResolver</c> and the focused native layout modules (TREEMAP, SUNBURST, SANKEY,
/// NETWORK, MAP, MATRIX) both resolve through here, so a <c>CREATE STYLE</c> theme or a
/// <c>COLOR:&lt;series&gt;</c> token reaches every visual on a page identically. A focused module
/// keeping its own array is how two visuals of the same data end up different colours.
/// </summary>
public static class ChartPalette
{
    /// <summary>Default categorical series colours, in assignment order.</summary>
    public static readonly ImmutableArray<string> Series =
        ["#5470c6", "#91cc75", "#fac858", "#ee6666", "#73c0de", "#3ba272", "#fc8452"];

    /// <summary>The default colour for the n-th series. Wraps, and tolerates a negative index.</summary>
    public static string Default(int index) =>
        Series[((index % Series.Length) + Series.Length) % Series.Length];

    /// <summary>
    /// Resolves a series colour: the authored <c>COLOR:&lt;key&gt;</c> style token when present,
    /// otherwise the default for that position.
    /// </summary>
    public static string Resolve(ImmutableArray<StyleToken> tokens, string seriesKey, int index) =>
        Token(tokens, seriesKey) ?? Default(index);

    /// <summary>The authored colour override for a series, or null when the author declared none.</summary>
    public static string? Token(ImmutableArray<StyleToken> tokens, string seriesKey)
    {
        if (tokens.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(seriesKey)) return null;
        var name = $"COLOR:{seriesKey}";
        return tokens.FirstOrDefault(token => token.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    /// <summary>
    /// Whether a candidate is a paint value a renderer may emit unescaped: a 3- or 6-digit hex
    /// colour. Anything else falls back, because an unvalidated token reaches an SVG attribute.
    /// </summary>
    public static bool IsSafePaint(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        var value = candidate.Trim();
        return value.Length is 4 or 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    }
}
