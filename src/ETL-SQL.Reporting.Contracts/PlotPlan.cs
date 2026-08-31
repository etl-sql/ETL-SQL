using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

public enum SemanticFallbackKind
{
    Summary,
    RankedTable,
    TimeSeriesTable,
    ProportionalBreakdown,
    Hierarchy,
    TransitionTable,
    NetworkConnections
}

public sealed record PlotBounds(decimal X, decimal Y, decimal Width, decimal Height);

public sealed record GeographicPoint(decimal Longitude, decimal Latitude);
public sealed record GeographicFeature(string Key, ImmutableArray<ImmutableArray<GeographicPoint>> Rings);
public sealed record ResolvedGeographicGeometry(
    GeographicProjectionKind Projection,
    string SourceAuthority,
    string FeatureKey,
    ImmutableArray<GeographicFeature> Features);

public sealed record PlotTick(ChartValue Value, string Label);

public sealed record ResolvedColorRange(
    ColorRangeKind Kind,
    string Low,
    string High,
    string? Mid,
    decimal? Midpoint,
    string NullColor,
    ImmutableArray<PlotTick> Ticks,
    string AccessibleDescription);

public sealed record ResolvedScale(
    string Id,
    FieldChannel Channel,
    ScaleKind Kind,
    ImmutableArray<ChartValue> Domain,
    ImmutableArray<string> Categories,
    ImmutableArray<PlotTick> Ticks,
    bool IncludesZero)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedColorRange? ColorRange { get; init; }
}

public sealed record ResolvedSeries(string Key, string Label, int Order, string Color);

public sealed record PaletteAssignment(string SeriesKey, string Color);

public sealed record LegendEntry(string SeriesKey, string Label, int Order, string Color);

public sealed record ResolvedChannelValue(FieldChannel Channel, ChartValue Value, string? DisplayValue);
public sealed record ResolvedEncodingValue(ConditionalEncodingChannel Channel, ChartValue Value);

public sealed record ResolvedDatum(
    int RowIndex,
    ImmutableArray<ResolvedChannelValue> Channels,
    bool IsGap)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<ResolvedEncodingValue> Encodings { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal DisplayOffsetX { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal DisplayOffsetY { get; init; }
}

/// <summary>
/// The axis along which a mark's quantitative extent grows from its baseline. Resolved once,
/// server-side, so a renderer never has to recognise a chart by name to know which dimension of a
/// mark carries its value.
/// </summary>
public enum MarkExtentAxis
{
    None,
    X,
    Y
}

/// <summary>Which edge of <see cref="MarkExtentAxis"/> the mark's baseline sits on.</summary>
public enum MarkExtentAnchor
{
    Start,
    End
}

public sealed record ResolvedMarkLayer(
    string Id,
    MarkKind Mark,
    int ZIndex,
    string? SeriesKey,
    ImmutableArray<ResolvedDatum> Data)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<StyleToken> Style { get; init; }

    /// <summary>Axis carrying this layer's value extent, or <see cref="MarkExtentAxis.None"/> when
    /// the mark has no baseline-anchored extent (points, lines, ranged rects, arcs).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MarkExtentAxis ExtentAxis { get; init; }

    /// <summary>Edge the extent grows from. Vertical bars anchor at <see cref="MarkExtentAnchor.End"/>
    /// because screen Y grows downward; transposed bars anchor at the start.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public MarkExtentAnchor ExtentAnchor { get; init; }
    public StackMode Stack { get; init; }
    public decimal BandSize { get; init; } = .75m;
    public decimal TickThickness { get; init; } = .15m;
    public TickOrientation TickOrientation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PositionAdjustmentSpec? Position { get; init; }
}

public sealed record ResolvedNullPolicy(
    NullValuePolicy Default,
    ImmutableArray<FieldNullPolicy> Fields,
    ImmutableArray<int> GapRows,
    ImmutableArray<int> SkippedRows);

public sealed record ResolvedFacetPanel(
    string Id,
    string? RowLabel,
    string? ColumnLabel,
    PlotBounds Bounds,
    ImmutableArray<int> RowIndices,
    ImmutableArray<ResolvedScale> Scales)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlotBounds? CartesianViewport { get; init; }

}

public sealed record SemanticFallbackItem(string Label, string Value, int Order)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Group { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Level { get; init; }
}

public sealed record SemanticFallback(
    SemanticFallbackKind Kind,
    string Heading,
    ImmutableArray<SemanticFallbackItem> Items)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; init; }
}

/// <summary>How a selection is drawn over the unselected universe.</summary>
public enum SelectionHighlightMode
{
    None,
    /// <summary>Selected marks are emphasised and the rest dimmed.</summary>
    Categorical,
    /// <summary>Each mark shows the selected share of its own value as an inset overlay.</summary>
    Proportional
}

public sealed record ResolvedInteractionTrigger(
    string Trigger,
    InteractionEffect Effect,
    string? Target = null,
    string? Parameter = null);

/// <summary>
/// The resolved interaction semantics for one chart: which column a selection is keyed on, which
/// column carries its measure, and how a selection is drawn. Every decision is made here, once,
/// from resolved encodings — never re-derived downstream from mappings or a visual type name.
/// </summary>
public sealed record ResolvedInteraction(
    SelectionMode Selection,
    InteractionEffect Effect,
    SelectionHighlightMode Highlight,
    ImmutableArray<ResolvedInteractionTrigger> Triggers)
{
    /// <summary>Resolved selection/cross-filter key column.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Key { get; init; }

    /// <summary>Resolved quantitative measure column backing proportional highlighting.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ValueKey { get; init; }

    public static readonly ResolvedInteraction Inert =
        new(SelectionMode.None, InteractionEffect.Highlight, SelectionHighlightMode.None, []);
}

public sealed record PlotPlan(
    string Schema,
    int Version,
    string SpecId,
    string? Title,
    PlotBounds Bounds,
    ImmutableArray<ResolvedScale> Scales,
    ImmutableArray<ResolvedSeries> Series,
    ImmutableArray<PaletteAssignment> Palette,
    ImmutableArray<LegendEntry> Legend,
    ImmutableArray<ResolvedMarkLayer> Layers,
    ResolvedNullPolicy Nulls,
    string AccessibleSummary,
    SemanticFallback Fallback) : IVersionedChartContract
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CoordinateSpec? Coordinate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<StyleToken> Style { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<ResolvedFacetPanel> Facets { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlotBounds? CartesianViewport { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedGeographicGeometry? Geography { get; init; }

    /// <summary>Resolved interaction semantics. The compact browser interaction manifest is projected
    /// from this; browser clients never receive the plan itself.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedInteraction? Interaction { get; init; }

    public static PlotPlan Create(
        string specId,
        PlotBounds bounds,
        ImmutableArray<ResolvedScale> scales,
        ImmutableArray<ResolvedSeries> series,
        ImmutableArray<PaletteAssignment> palette,
        ImmutableArray<LegendEntry> legend,
        ImmutableArray<ResolvedMarkLayer> layers,
        ResolvedNullPolicy nulls,
        string accessibleSummary,
        SemanticFallback fallback,
        string? title = null,
        CoordinateSpec? coordinate = null,
        ImmutableArray<StyleToken> style = default,
        ImmutableArray<ResolvedFacetPanel> facets = default) => new(
            ChartContractVersions.PlotPlanSchema,
            ChartContractVersions.PlotPlanCurrent,
            specId,
            title,
            bounds,
            scales,
            series,
            palette,
            legend,
            layers,
            nulls,
            accessibleSummary,
            fallback)
        {
            Coordinate = coordinate,
            Style = style,
            Facets = facets
        };

    public void Validate()
    {
        ChartContractValidation.RequireVersion(Schema, Version, ChartContractVersions.PlotPlanSchema, ChartContractVersions.PlotPlanCurrent, nameof(PlotPlan));
        ChartContractValidation.RequireName(SpecId, nameof(SpecId));
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            throw new InvalidDataException("Plot bounds must have positive width and height.");
        if (string.IsNullOrWhiteSpace(AccessibleSummary))
            throw new InvalidDataException("A PlotPlan must include an accessible summary.");
        if (!Style.IsDefault)
            ChartContractValidation.RequireUnique(Style.Select(token => token.Name), "plot style token");

        ChartContractValidation.RequireUnique(Scales.Select(scale => scale.Id), "resolved scale id");
        ChartContractValidation.RequireUnique(Series.Select(series => series.Key), "series key");
        ChartContractValidation.RequireUnique(Layers.Select(layer => layer.Id), "resolved layer id");
        if (!Facets.IsDefault) ChartContractValidation.RequireUnique(Facets.Select(facet => facet.Id), "resolved facet id");
        ChartContractValidation.RequireUnique(Palette.Select(entry => entry.SeriesKey), "palette series key");
        ChartContractValidation.RequireUnique(Legend.Select(entry => entry.SeriesKey), "legend series key");

        var orderedSeries = Series.OrderBy(series => series.Order).ThenBy(series => series.Key, StringComparer.Ordinal).ToArray();
        if (!Series.SequenceEqual(orderedSeries))
            throw new InvalidDataException("Resolved series must be stored in deterministic order.");
        var orderedLegend = Legend.OrderBy(entry => entry.Order).ThenBy(entry => entry.SeriesKey, StringComparer.Ordinal).ToArray();
        if (!Legend.SequenceEqual(orderedLegend))
            throw new InvalidDataException("Legend entries must be stored in deterministic order.");
        var orderedLayers = Layers.OrderBy(layer => layer.ZIndex).ThenBy(layer => layer.Id, StringComparer.Ordinal).ToArray();
        if (!Layers.SequenceEqual(orderedLayers))
            throw new InvalidDataException("Resolved layers must be stored in deterministic z-order.");

        var seriesKeys = Series.Select(series => series.Key).ToHashSet(StringComparer.Ordinal);
        if (Palette.Any(entry => !seriesKeys.Contains(entry.SeriesKey)))
            throw new InvalidDataException("Palette assignments must reference a resolved series.");
        if (Legend.Any(entry => !seriesKeys.Contains(entry.SeriesKey)))
            throw new InvalidDataException("Legend entries must reference a resolved series.");

        foreach (var scale in Scales)
        {
            foreach (var value in scale.Domain) value.Validate();
            foreach (var tick in scale.Ticks) tick.Value.Validate();
        }
        foreach (var datum in Layers.SelectMany(layer => layer.Data))
        {
            foreach (var channel in datum.Channels)
                channel.Value.Validate();
            if (!datum.Encodings.IsDefault)
                foreach (var encoding in datum.Encodings)
                    encoding.Value.Validate();
        }
        if (Coordinate?.Kind == CoordinateKind.Geographic && Geography is null)
            throw new InvalidDataException("A geographic PlotPlan requires resolved bounded geometry.");
        if (Geography is { } geography)
        {
            if (geography.Features.Length > 10000)
                throw new InvalidDataException("Geographic geometry exceeds the 10,000 feature limit.");
            if (geography.Features.SelectMany(feature => feature.Rings).Sum(ring => ring.Length) > 200000)
                throw new InvalidDataException("Geographic geometry exceeds the 200,000 coordinate limit.");
        }
    }
}
