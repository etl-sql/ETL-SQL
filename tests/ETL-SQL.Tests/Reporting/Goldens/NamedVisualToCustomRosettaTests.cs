using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Semantics;
using ETL_SQL.Tests.Reporting.Conformance;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Goldens;

/// <summary>
/// The learning path from named visuals into <c>CUSTOM</c> claims that a named visual is a concise
/// spelling of a chart the grammar can also express. These tests assert that claim instead of leaving it
/// in prose, so it cannot rot the next time <c>NamedVisualChartLowerer</c> changes.
///
/// Each pair ships as one fixture holding both spellings over one staged source, so the comparison cannot
/// drift apart through the data.
///
/// Scope is deliberate. Whole-plan equality does not hold and should not be asserted:
/// <list type="bullet">
///   <item><description>Identity — spec id, scale ids, layer ids — names the authoring form. Ordering is
///   already deterministic, so position carries identity in the comparison.</description></item>
///   <item><description>Null policy is set per visual type by named lowering and has no <c>MAPPINGS</c>
///   equivalent for <c>CUSTOM</c> to produce.</description></item>
///   <item><description>Style tokens transcribing the named authoring clauses (<c>mapping:*</c> from
///   <c>MAPPINGS</c>, and <c>OPTIONS</c> keys that <c>CUSTOM</c> expresses structurally in its scale
///   declarations) describe how the chart was written, not what it resolved to.</description></item>
/// </list>
///
/// Everything else is compared: layers, scales, palette, series, data, theme tokens, and the resolved
/// <see cref="FormattingSpec"/>. Theme and formatting are inside the scope on purpose — both lowerers
/// build them through <c>ChartStyleTokens</c>, so a difference there is a regression, not test noise.
///
/// Each exclusion is pinned by <see cref="TheExclusionsFromTheComparedScope_AreExactlyWhatIsDocumented"/>,
/// so it cannot quietly widen into hiding a real divergence.
/// </summary>
public class NamedVisualToCustomRosettaTests
{
    /// <summary>
    /// Style/theme token names produced only by the named spelling, pinned per pair. These transcribe the
    /// named authoring clauses; every other token must match on both sides.
    /// </summary>
    private static readonly string[] AuthoringFormTokens = ["AXIS_SORT", "mapping:x", "mapping:y"];

    /// <summary>
    /// The per-visual-type null policy named lowering sets. A bar with no value has nothing to draw, so the
    /// row is skipped; a line must break rather than close over the hole, so the row becomes a gap. The
    /// CHART grammar has no MAPPINGS clause to carry this, so CUSTOM resolves the grammar default in both
    /// cases. Pinned per type so the exclusion cannot quietly absorb a policy change.
    /// </summary>
    private static readonly Dictionary<string, NullValuePolicy> NamedNullPolicy = new(StringComparer.Ordinal)
    {
        ["RosettaBarNamed"] = NullValuePolicy.Skip,
        ["RosettaLineNamed"] = NullValuePolicy.Gap,
    };

    public static IEnumerable<object[]> Pairs() =>
    [
        ["rosetta_bar_named_and_custom.rptsql", "RosettaBarNamed", "RosettaBarCustom"],
        ["rosetta_line_named_and_custom.rptsql", "RosettaLineNamed", "RosettaLineCustom"],
    ];

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task NamedVisual_AndItsCustomSpelling_ResolveTheSameLayersScalesPaletteAndData(
        string fixtureFileName, string namedVisual, string customVisual)
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);

        var named = Require(manifest, namedVisual);
        var custom = Require(manifest, customVisual);

        Assert.Equal("CUSTOM", custom.VisualType, ignoreCase: true);
        Assert.NotEqual("CUSTOM", named.VisualType?.ToUpperInvariant());

        Assert.Equal(Render(Project(Require(named))), Render(Project(Require(custom))));
    }

    /// <summary>
    /// Theme tokens are built by both lowerers through the same shared component. Tokens present on both
    /// sides must agree; the only tokens allowed to be one-sided are the named authoring-clause
    /// transcriptions, and <c>CUSTOM</c> may not introduce any of its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task NamedVisual_AndItsCustomSpelling_ResolveTheSameThemeTokens(
        string fixtureFileName, string namedVisual, string customVisual)
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);

        var namedTokens = Tokens(Require(manifest, namedVisual));
        var customTokens = Tokens(Require(manifest, customVisual));

        foreach (var shared in namedTokens.Keys.Intersect(customTokens.Keys, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
            Assert.Equal($"{shared}={namedTokens[shared]}", $"{shared}={customTokens[shared]}");

        var customOnly = customTokens.Keys.Except(namedTokens.Keys, StringComparer.Ordinal).ToArray();
        Assert.True(customOnly.Length == 0,
            $"The CUSTOM spelling resolved theme tokens the named spelling did not: {string.Join(", ", customOnly)}.");

        var namedOnly = namedTokens.Keys.Except(customTokens.Keys, StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(AuthoringFormTokens.OrderBy(name => name, StringComparer.Ordinal), namedOnly);
    }

    /// <summary>
    /// Locale, time zone, and null label are the resolved formatting precedence and must match exactly.
    /// The named spelling additionally lists the columns its <c>MAPPINGS</c> named; those entries carry no
    /// format of their own, and this test fails the moment one starts to, rather than letting a real
    /// formatting divergence hide behind the exclusion.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task NamedVisual_AndItsCustomSpelling_ResolveTheSameEffectiveFormatting(
        string fixtureFileName, string namedVisual, string customVisual)
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);

        var named = RequireSpec(Require(manifest, namedVisual)).Formatting;
        var custom = RequireSpec(Require(manifest, customVisual)).Formatting;

        Assert.Equal(named.Locale, custom.Locale);
        Assert.Equal(named.TimeZone, custom.TimeZone);
        Assert.Equal(named.NullLabel, custom.NullLabel);
        Assert.Equal(Render(EffectiveFieldFormats(named)), Render(EffectiveFieldFormats(custom)));

        Assert.All(Fields(named).Where(field => !EffectiveFieldFormats(named).Contains(field)), field =>
        {
            Assert.Null(field.Format);
            Assert.Null(field.NullLabel);
        });
    }

    /// <summary>
    /// The excluded divergence is real and intentional, not an oversight. Pinning it keeps the exclusion
    /// honest: if named lowering ever stops adding what CUSTOM cannot express, the narrower comparison
    /// should be widened rather than left permanently loose.
    /// </summary>
    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task TheExclusionsFromTheComparedScope_AreExactlyWhatIsDocumented(
        string fixtureFileName, string namedVisual, string customVisual)
    {
        var (_, manifest, _) = await RepresentativeVisualConformanceHarness.CompileFixtureAsync(fixtureFileName);

        var named = Require(Require(manifest, namedVisual));
        var custom = Require(Require(manifest, customVisual));

        // Identity names the authoring form, which is why it sits outside the compared scope.
        Assert.NotEqual(named.SpecId, custom.SpecId);

        // Null policy: named lowering sets a per-visual-type default; CUSTOM resolves the grammar default.
        Assert.True(NamedNullPolicy.TryGetValue(namedVisual, out var expectedNamedPolicy),
            $"'{namedVisual}' has no pinned null policy. Add one so the exclusion stays explicit.");
        Assert.Equal(expectedNamedPolicy, named.Nulls.Default);
        Assert.Equal(NullValuePolicy.Gap, custom.Nulls.Default);

        // Interaction is no longer part of the divergence: the resolved key and value key agree, so the
        // cross-filter an author gets is the same whichever spelling they used.
        Assert.Equal(named.Interaction?.Key, custom.Interaction?.Key);
        Assert.Equal(named.Interaction?.ValueKey, custom.Interaction?.ValueKey);

        // The compared scope is non-trivial: both plans really do resolve marks and data.
        Assert.NotEmpty(named.Layers);
        Assert.Equal(named.Layers.Sum(layer => layer.Data.Length), custom.Layers.Sum(layer => layer.Data.Length));
    }

    // ── Comparison projection ────────────────────────────────────────────────

    private sealed record ScaleProjection(
        FieldChannel Channel,
        ScaleKind Kind,
        ImmutableArray<ChartValue> Domain,
        ImmutableArray<string> Categories,
        ImmutableArray<PlotTick> Ticks,
        bool IncludesZero,
        ResolvedColorRange? ColorRange);

    private sealed record DatumProjection(
        int RowIndex,
        bool IsGap,
        string? Tooltip,
        ImmutableArray<ResolvedChannelValue> Channels,
        ImmutableArray<ResolvedEncodingValue> Encodings,
        decimal DisplayOffsetX,
        decimal DisplayOffsetY);

    private sealed record LayerProjection(
        MarkKind Mark,
        int ZIndex,
        StackMode Stack,
        decimal BandSize,
        MarkExtentAxis ExtentAxis,
        MarkExtentAnchor ExtentAnchor,
        ImmutableArray<StyleToken> Style,
        ImmutableArray<DatumProjection> Data);

    private sealed record PlanProjection(
        ImmutableArray<ScaleProjection> Scales,
        ImmutableArray<LayerProjection> Layers,
        ImmutableArray<string> PaletteColors,
        ImmutableArray<string> SeriesColors,
        string AccessibleSummary,
        SemanticFallback Fallback);

    private static PlanProjection Project(PlotPlan plan) => new(
        Scales: plan.Scales
            .OrderBy(scale => scale.Channel)
            .ThenBy(scale => scale.Kind)
            .Select(scale => new ScaleProjection(
                scale.Channel, scale.Kind, scale.Domain, scale.Categories, scale.Ticks,
                scale.IncludesZero, scale.ColorRange))
            .ToImmutableArray(),
        Layers: plan.Layers
            .OrderBy(layer => layer.ZIndex)
            .Select(layer => new LayerProjection(
                layer.Mark, layer.ZIndex, layer.Stack, layer.BandSize, layer.ExtentAxis, layer.ExtentAnchor,
                layer.Style.IsDefault ? [] : layer.Style,
                layer.Data.Select(datum => new DatumProjection(
                    datum.RowIndex, datum.IsGap, datum.Tooltip, datum.Channels,
                    datum.Encodings.IsDefault ? [] : datum.Encodings,
                    datum.DisplayOffsetX, datum.DisplayOffsetY)).ToImmutableArray()))
            .ToImmutableArray(),
        PaletteColors: plan.Palette.Select(entry => entry.Color).ToImmutableArray(),
        SeriesColors: plan.Series.OrderBy(series => series.Order).Select(series => series.Color).ToImmutableArray(),
        // The accessible summary and the semantic fallback are what non-visual consumers read. If the two
        // spellings resolved the same chart, a screen reader and the terminal must describe it the same way.
        AccessibleSummary: plan.AccessibleSummary,
        Fallback: plan.Fallback);

    private static Dictionary<string, string> Tokens(VisualManifest visual)
    {
        var theme = RequireSpec(visual).Theme;
        var tokens = theme.Tokens.IsDefault ? [] : theme.Tokens;
        return tokens.ToDictionary(token => token.Name, token => token.Value ?? string.Empty, StringComparer.Ordinal);
    }

    private static ImmutableArray<FieldFormat> Fields(FormattingSpec formatting) =>
        formatting.Fields.IsDefault ? [] : formatting.Fields;

    /// <summary>Field entries that actually set something; bare column listings carry no formatting.</summary>
    private static ImmutableArray<FieldFormat> EffectiveFieldFormats(FormattingSpec formatting) =>
        Fields(formatting)
            .Where(field => field.Format is not null || field.NullLabel is not null)
            .OrderBy(field => field.Field, StringComparer.Ordinal)
            .ToImmutableArray();

    // Rendering the projection makes an inequality readable in the assertion output rather than the
    // "Expected: PlanProjection { ... }" wall a record comparison produces.
    private static readonly JsonSerializerOptions Readable = CreateReadable();

    private static string Render<T>(T value) => JsonSerializer.Serialize(value, Readable);

    private static JsonSerializerOptions CreateReadable()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static VisualManifest Require(ReportManifest manifest, string visualName)
    {
        var visual = manifest.Visuals.FirstOrDefault(candidate =>
            candidate.Name.Equals(visualName, StringComparison.OrdinalIgnoreCase));
        Assert.True(visual is not null,
            $"Fixture has no visual named '{visualName}'. Present: " +
            string.Join(", ", manifest.Visuals.Select(candidate => candidate.Name)));
        return visual!;
    }

    private static PlotPlan Require(VisualManifest visual)
    {
        Assert.True(visual.PlotPlan is not null,
            $"Visual '{visual.Name}' resolved no PlotPlan, so there is nothing to compare.");
        return visual.PlotPlan!;
    }

    private static ChartSpec RequireSpec(VisualManifest visual)
    {
        Assert.True(visual.ChartSpec is not null, $"Visual '{visual.Name}' resolved no ChartSpec.");
        return visual.ChartSpec!;
    }
}
