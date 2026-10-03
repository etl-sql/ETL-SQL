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
    bool IncludesZero,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Reverse = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool MinorTicks = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LabelRotation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? LabelSkip = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] decimal OuterPadding = 0m,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TickFormat = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TimeUnit = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedColorRange? ColorRange { get; init; }
}

public sealed record ResolvedSeries(string Key, string Label, int Order, string Color);

public sealed record PaletteAssignment(string SeriesKey, string Color);

public sealed record LegendEntry(string SeriesKey, string Label, int Order, string Color);

public sealed record ResolvedChannelValue(FieldChannel Channel, ChartValue Value, string? DisplayValue);
public sealed record ResolvedEncodingValue(ConditionalEncodingChannel Channel, ChartValue Value);

public sealed record ResolvedRadialInterval(decimal StartAngle, decimal EndAngle, decimal Start, decimal End, decimal Maximum);

public sealed record ResolvedDatum(
    int RowIndex,
    ImmutableArray<ResolvedChannelValue> Channels,
    bool IsGap)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<ResolvedEncodingValue> Encodings { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedConnectionCoordinates? ConnectionCoordinates { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedConnectionDecoration? ConnectionDecoration { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal DisplayOffsetX { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedRadialInterval? RadialInterval { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal DisplayOffsetY { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? SegmentLineDash { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? SegmentColor { get; init; }
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
    public ImmutableArray<ResolvedMarkConnection> Connections { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<int> ConnectionSkippedRows { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ConnectionDecorations { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConnectedInterpolationKind? ConnectionInterpolation { get; init; }
    /// <summary>Ordinary transposed LINE/AREA interpolation in mapped display coordinates.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConnectedInterpolationKind? PathInterpolation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AreaRibbon { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AreaConfidence { get; init; }
    /// <summary>Explicit shared ribbon scale when it differs from the first primary Y scale.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AreaRibbonScaleId { get; init; }
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedCartesianAxes? CartesianAxes { get; init; }

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
        var connected = Layers.Any(layer => !layer.Connections.IsDefault);
        var decorated = Layers.Any(layer => layer.ConnectionDecorations);
        var interpolated = Layers.Any(layer => layer.ConnectionInterpolation is not null);
        var ordinaryInterpolation = Layers.Any(layer => layer.PathInterpolation is not null);
        var ordinaryAreaInterpolation = Layers.Any(layer => layer.Mark == MarkKind.Area && layer.PathInterpolation is not null);
        var confidence = Layers.Any(layer => layer.AreaConfidence);
        var connect = Layers.Any(layer => !layer.Connections.IsDefault && ResolvedMarkConnection.ConnectsAcrossNulls(layer));
        var zero = Layers.Any(layer => !layer.Connections.IsDefault && ResolvedMarkConnection.FillsNullsWithZero(layer));
        var scaledRibbon = Layers.Any(layer => layer.AreaRibbonScaleId is not null);
        var transposedConnected = connected && Coordinate?.Kind == CoordinateKind.TransposedCartesian;
        var transposedArea = !connected && Coordinate is { Kind: CoordinateKind.TransposedCartesian, AspectRatio: not null } && Layers.Any(layer => layer.Mark == MarkKind.Area);
        var composition = connected && Coordinate?.Kind is (CoordinateKind.Cartesian or CoordinateKind.TransposedCartesian) && CartesianAxes is not null;
        if (transposedConnected && !composition)
            throw new InvalidDataException("Transposed connections require resolved shared primary axes.");
        if (zero && !composition)
            throw new InvalidDataException("ZERO connected geometry requires resolved Cartesian primary axes.");
        if (decorated && !composition)
            throw new InvalidDataException("Connected decorations require resolved shared primary axes and typed connections.");
        if (interpolated && !composition)
            throw new InvalidDataException("Interpolated connections require resolved shared primary axes and typed connections.");
        foreach (var layer in Layers)
        {
            if (layer.AreaConfidence && (layer.Mark != MarkKind.Area || !layer.AreaRibbon || !layer.Connections.IsDefault ||
                Coordinate is not { Kind: CoordinateKind.TransposedCartesian, AspectRatio: not null }) ||
                !layer.AreaConfidence && Coordinate is { Kind: CoordinateKind.TransposedCartesian, AspectRatio: not null } &&
                layer.Mark == MarkKind.Area && layer.Data.Any(datum => datum.Channels.Any(channel => channel.Channel is FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh)))
                throw new InvalidDataException("Confidence bands require declared ordinary transposed aspect AREA intent.");
            if (layer.AreaConfidence && layer.Data.Any(datum => datum.Channels.Any(channel => channel.Channel is not
                (FieldChannel.X or FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh or FieldChannel.Row or FieldChannel.Column or FieldChannel.Wrap))))
                throw new InvalidDataException("Confidence bands require only X and paired confidence bounds.");
            var expectedPathInterpolation = Coordinate is { Kind: CoordinateKind.TransposedCartesian, AspectRatio: not null } &&
                layer.Mark is (MarkKind.Line or MarkKind.Area) && layer.Connections.IsDefault ? ResolvedConnectionGeometry.Kind(layer.Style) : null;
            if (layer.PathInterpolation != expectedPathInterpolation)
                throw new InvalidDataException("Ordinary path interpolation must preserve transposed aspect LINE/AREA intent.");
            if (layer.PathInterpolation is not null &&
                (CartesianAxes is null || layer.Stack != StackMode.None ||
                 layer.Position is not (null or { Kind: PositionAdjustmentKind.Identity } or { Kind: PositionAdjustmentKind.Jitter } or
                 { Kind: PositionAdjustmentKind.Nudge, Unit: PositionAdjustmentUnit.Em or PositionAdjustmentUnit.Band or PositionAdjustmentUnit.Data }) ||
                 !layer.Style.Any(token => token.Name.Equals("nullHandling", StringComparison.OrdinalIgnoreCase) && token.Value.Equals("GAP", StringComparison.OrdinalIgnoreCase)) ||
                 layer.Data.Any(datum => !datum.Encodings.IsDefaultOrEmpty || datum.Channels.Any(channel => channel.Channel is not
                     (FieldChannel.X or FieldChannel.Row or FieldChannel.Column or FieldChannel.Wrap) &&
                     (layer.AreaConfidence ? channel.Channel is not (FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh) : layer.AreaRibbon ? channel.Channel is not (FieldChannel.YStart or FieldChannel.YEnd) : channel.Channel != FieldChannel.Y)) ||
                     !datum.IsGap && !ResolvedMarkConnection.HasCompleteCoordinates(datum, layer.AreaRibbon, layer.AreaConfidence))))
                throw new InvalidDataException("Ordinary interpolated LINE/AREA requires primary axes, unstacked scalar X/Y or ribbon bounds, GAP and supported placement without conditions.");
            if (layer.ConnectionInterpolation is not null && (layer.Connections.IsDefault || layer.ConnectionInterpolation != ResolvedConnectionGeometry.Kind(layer.Style)) ||
                layer.ConnectionInterpolation is null && !layer.Connections.IsDefault && (ResolvedConnectionGeometry.Kind(layer.Style) is not null || layer.Connections.Any(connection => connection.Geometry is not null)))
                throw new InvalidDataException("Connection interpolation and geometry must preserve declared layer intent.");
            if (layer.ConnectionDecorations && layer.Connections.IsDefault || !layer.ConnectionDecorations && layer.Data.Any(datum => datum.ConnectionDecoration is not null))
                throw new InvalidDataException("Row decorations belong only to a declared decorated connected layer.");
            if (layer.ConnectionDecorations)
                foreach (var datum in layer.Data)
                    if (datum.ConnectionDecoration != ResolvedConnectionDecoration.FromRaw(datum, layer, Style))
                        throw new InvalidDataException("Connected decorations must preserve complete raw anchors, row presentation and resolved defaults.");
        }
        if ((transposedArea || composition || ordinaryInterpolation) != (CartesianAxes is not null))
            throw new InvalidDataException("Resolved primary axes require transposed aspect AREA, ordinary interpolated LINE or a connected Cartesian composition.");
        if (CartesianAxes is { } axes)
        {
            void ValidateAxes(ImmutableArray<ResolvedScale> scales)
            {
                foreach (var (id, channel) in new[] { (axes.XScaleId, FieldChannel.X), (axes.YScaleId, FieldChannel.Y) })
                    if (!scales.Any(scale => scale.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && scale.Channel == channel && (composition ? scale.Kind == ScaleKind.Linear : scale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic) &&
                        scale.Domain.Length >= 2 && scale.Domain.All(value => value.Kind is ChartValueKind.Integer or ChartValueKind.Decimal or ChartValueKind.FloatingPoint) &&
                        (scale.Kind != ScaleKind.Logarithmic || !scale.Domain.Any(value => value.Decimal is <= 0m || value.Integer is <= 0L || value.FloatingPoint is <= 0d))))
                        throw new InvalidDataException("Resolved primary axes must reference supported quantitative X/Y scales.");
                if (Layers.Any(layer => !layer.AreaRibbon && (layer.Mark == MarkKind.Area && (transposedArea || transposedConnected || ordinaryAreaInterpolation || !layer.Connections.IsDefault) || !layer.Connections.IsDefault && ResolvedMarkConnection.FillsNullsWithZero(layer))))
                {
                    var y = scales.First(scale => scale.Id.Equals(axes.YScaleId, StringComparison.OrdinalIgnoreCase));
                    if (y.Kind != ScaleKind.Linear || !y.IncludesZero ||
                        !y.Domain.Any(value => value.Decimal is <= 0m || value.Integer is <= 0L || value.FloatingPoint is <= 0d) ||
                        !y.Domain.Any(value => value.Decimal is >= 0m || value.Integer is >= 0L || value.FloatingPoint is >= 0d))
                        throw new InvalidDataException("AREA zero baselines must be included in the resolved linear Y domain.");
                }
            }
            ValidateAxes(Scales);
            foreach (var panel in Facets.IsDefault ? [] : Facets) ValidateAxes(panel.Scales);
            if (composition && Layers.Any(layer => layer.AreaRibbonScaleId is { } id && !id.Equals(axes.YScaleId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Connected ribbon bounds must use the resolved shared Y axis.");
            if (Layers.Any(layer => layer.Stack != StackMode.None || transposedArea && !layer.Connections.IsDefault ||
                layer.Mark is not (MarkKind.Point or MarkKind.Text or MarkKind.Rule or MarkKind.Rect or MarkKind.Line or MarkKind.Area or MarkKind.Tick) || (transposedArea || transposedConnected || ordinaryInterpolation) && layer.Mark == MarkKind.Tick ||
                layer.Data.Any(datum => datum.Channels.Any(value => value.Channel == FieldChannel.Y2))))
                throw new InvalidDataException("Resolved primary axes require supported unstacked Cartesian marks without secondary axes; transposed aspect AREA does not support conditional connections.");
            foreach (var layer in Layers.Where(layer => layer.Mark == MarkKind.Area && (transposedArea || transposedConnected && layer.Connections.IsDefault)))
            {
                if (layer.AreaRibbonScaleId is { } ribbonScale && !ribbonScale.Equals(axes.YScaleId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Transposed AREA ribbons must use the resolved primary Y scale.");
                if (!(layer.Position is null or { Kind: PositionAdjustmentKind.Identity } || Coordinate?.AspectRatio is not null && layer.Position is ({ Kind: PositionAdjustmentKind.Jitter } or { Kind: PositionAdjustmentKind.Nudge, Unit: PositionAdjustmentUnit.Em or PositionAdjustmentUnit.Band or PositionAdjustmentUnit.Data })) ||
                    !layer.Style.Any(token => token.Name.Equals("nullHandling", StringComparison.OrdinalIgnoreCase) && token.Value.Equals("GAP", StringComparison.OrdinalIgnoreCase)) ||
                    !layer.Style.Any(token => token.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase) && (token.Value.Equals("LINEAR", StringComparison.OrdinalIgnoreCase) || layer.PathInterpolation is not null && ResolvedConnectionGeometry.Supports(token.Value))) ||
                    !layer.AreaRibbon && !layer.Style.Any(token => token.Name.Equals("areaBaseline", StringComparison.OrdinalIgnoreCase) && token.Value.Equals("ZERO", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Transposed AREA plans require IDENTITY (or JITTER/NUDGE UNIT EM/BAND/DATA with ASPECT_RATIO), GAP, declared interpolation and an explicit zero baseline or ribbon.");
                foreach (var datum in layer.Data.Where(datum => !datum.IsGap))
                    if (!ResolvedMarkConnection.HasCompleteCoordinates(datum, layer.AreaRibbon, layer.AreaConfidence) || !datum.Encodings.IsDefaultOrEmpty)
                        throw new InvalidDataException("Transposed AREA aspect endpoints require complete raw numeric coordinates without conditions.");
            }
        }
        foreach (var layer in Layers.Where(layer => layer.AreaRibbonScaleId is not null))
        {
            if (layer.Mark != MarkKind.Area || !Scales.Any(scale => scale.Id.Equals(layer.AreaRibbonScaleId, StringComparison.OrdinalIgnoreCase) &&
                scale.Channel == FieldChannel.Y) || layer.Data.Any(datum => !datum.IsGap &&
                (!datum.Channels.Any(value => value.Channel == (layer.AreaConfidence ? FieldChannel.ConfidenceLow : FieldChannel.YStart)) || !datum.Channels.Any(value => value.Channel == (layer.AreaConfidence ? FieldChannel.ConfidenceHigh : FieldChannel.YEnd)))))
                throw new InvalidDataException("Explicit ribbon scales require AREA endpoints and a declared Y scale.");
        }
        if (Layers.Any(layer => layer.AreaRibbon && (layer.Mark != MarkKind.Area || layer.Connections.IsDefault && !transposedArea && !composition ||
            layer.Style.Any(token => token.Name.Equals("areaBaseline", StringComparison.OrdinalIgnoreCase)))))
            throw new InvalidDataException("Connected ribbons require AREA connections and no baseline.");
        var radial = Layers.Any(layer => layer.Mark == MarkKind.Arc && layer.Stack != StackMode.None);
        ChartContractValidation.RequireVersion(Schema, Version, confidence ? ChartContractVersions.TransposedConfidencePlotPlanSchema : ordinaryAreaInterpolation ? ChartContractVersions.OrdinaryAreaInterpolationPlotPlanSchema : ordinaryInterpolation ? ChartContractVersions.OrdinaryInterpolationPlotPlanSchema : interpolated ? ChartContractVersions.InterpolatedConnectedPlotPlanSchema : decorated ? ChartContractVersions.DecoratedConnectedPlotPlanSchema : transposedConnected ? ChartContractVersions.TransposedConnectedPlotPlanSchema : zero ? ChartContractVersions.ZeroConnectedPlotPlanSchema : composition ? ChartContractVersions.ConnectedCompositionPlotPlanSchema : transposedArea ? ChartContractVersions.TransposedAreaPlotPlanSchema : connect ? ChartContractVersions.ConnectPlotPlanSchema : scaledRibbon ? ChartContractVersions.ScaledRibbonPlotPlanSchema : connected ? ChartContractVersions.ConnectedPlotPlanSchema : radial ? ChartContractVersions.RadialPlotPlanSchema : ChartContractVersions.PlotPlanSchema,
            confidence ? ChartContractVersions.TransposedConfidencePlotPlanVersion : ordinaryAreaInterpolation ? ChartContractVersions.OrdinaryAreaInterpolationPlotPlanVersion : ordinaryInterpolation ? ChartContractVersions.OrdinaryInterpolationPlotPlanVersion : interpolated ? ChartContractVersions.InterpolatedConnectedPlotPlanVersion : decorated ? ChartContractVersions.DecoratedConnectedPlotPlanVersion : transposedConnected ? ChartContractVersions.TransposedConnectedPlotPlanVersion : zero ? ChartContractVersions.ZeroConnectedPlotPlanVersion : composition ? ChartContractVersions.ConnectedCompositionPlotPlanVersion : transposedArea ? ChartContractVersions.TransposedAreaPlotPlanVersion : connect ? ChartContractVersions.ConnectPlotPlanVersion : scaledRibbon ? ChartContractVersions.ScaledRibbonPlotPlanVersion : connected ? ChartContractVersions.ConnectedPlotPlanVersion : radial ? ChartContractVersions.RadialPlotPlanVersion : ChartContractVersions.PlotPlanCurrent, nameof(PlotPlan));
        if (Layers.Any(layer => (layer.Connections.IsDefault || !ResolvedMarkConnection.FillsNullsWithZero(layer)) && layer.Data.Any(datum => datum.ConnectionCoordinates is not null)))
            throw new InvalidDataException("Explicit ZERO geometry belongs only to a ZERO connected layer.");
        if (composition) ValidateConnectedComposition();
        else if (connected)
        {
            if (Layers.Length != 1 || Layers[0].Mark is not (MarkKind.Line or MarkKind.Area) || Coordinate?.Kind != CoordinateKind.Cartesian ||
                !Facets.IsDefaultOrEmpty || Layers[0].Stack != StackMode.None)
                throw new InvalidDataException("Connected condition plans require one unstacked Cartesian LINE or AREA layer without facets.");
            var layer = Layers[0];
            if (layer.Mark == MarkKind.Area && !layer.AreaRibbon &&
                (!layer.Style.Any(token => token.Name.Equals("areaBaseline", StringComparison.OrdinalIgnoreCase) && token.Value.Equals("ZERO", StringComparison.OrdinalIgnoreCase)) ||
                 !Scales.Any(scale => scale.Channel == FieldChannel.Y && scale.IncludesZero &&
                     scale.Domain.Any(value => value.Decimal is <= 0m || value.Integer is <= 0L || value.FloatingPoint is <= 0d) &&
                     scale.Domain.Any(value => value.Decimal is >= 0m || value.Integer is >= 0L || value.FloatingPoint is >= 0d))))
                throw new InvalidDataException("Conditional AREA strips require an explicit zero baseline included in the Y domain.");
            if (layer.Position is not (null or { Kind: PositionAdjustmentKind.Identity }) ||
                !layer.Style.Any(token => token.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase) && token.Value.Equals("LINEAR", StringComparison.OrdinalIgnoreCase)) ||
                !layer.Style.Any(token => token.Name.Equals("nullHandling", StringComparison.OrdinalIgnoreCase) && token.Value.Equals(connect ? "CONNECT" : "GAP", StringComparison.OrdinalIgnoreCase)) ||
                Nulls.Default != (connect ? NullValuePolicy.Skip : NullValuePolicy.Gap) ||
                !Scales.Any(scale => scale.Channel == FieldChannel.X && scale.Kind == ScaleKind.Linear) ||
                !Scales.Any(scale => scale.Channel == FieldChannel.Y && scale.Kind == ScaleKind.Linear))
                throw new InvalidDataException("Connected condition plans require linear X/Y scales, IDENTITY, GAP or CONNECT handling and LINEAR interpolation.");
            foreach (var datum in layer.Data.Where(datum => !datum.IsGap))
                foreach (var channel in layer.AreaRibbon ? new[] { FieldChannel.X, FieldChannel.YStart, FieldChannel.YEnd } : new[] { FieldChannel.X, FieldChannel.Y })
                {
                    var kind = datum.Channels.FirstOrDefault(value => value.Channel == channel)?.Value.Kind;
                    if (kind is not (ChartValueKind.Integer or ChartValueKind.Decimal or ChartValueKind.FloatingPoint) &&
                        !(connect && kind is null or ChartValueKind.Null))
                        throw new InvalidDataException("Connected condition endpoints require numeric X and Y coordinates.");
                }
            if (connect && (layer.Data.Any(datum => datum.IsGap) || !Nulls.SkippedRows.SequenceEqual(layer.Data
                .Where(datum => !ResolvedMarkConnection.HasCompleteCoordinates(datum, layer.AreaRibbon, layer.AreaConfidence)).Select(datum => datum.RowIndex).Distinct().Order())))
                throw new InvalidDataException("CONNECT plans must identify incomplete raw rows as skipped endpoints.");
            var expected = ResolvedMarkConnection.ExpectedPairs(layer);
            if (!layer.Connections.Select(connection => (connection.SourceIndex, connection.DestinationIndex)).SequenceEqual(expected))
                throw new InvalidDataException("Connections must cover each adjacent eligible pair exactly once in row order.");
            foreach (var connection in layer.Connections) connection.Validate(layer);
        }
        if (!composition && Layers.Any(layer => !layer.ConnectionSkippedRows.IsDefault || !layer.Connections.IsDefault && layer.Connections.Any(connection => connection.FacetId is not null)))
            throw new InvalidDataException("Partitioned connection metadata requires the v9 composition envelope.");
        if (radial && (Coordinate?.Kind != CoordinateKind.Polar || Layers.Any(layer => layer.Mark != MarkKind.Arc || layer.Stack == StackMode.None)))
            throw new InvalidDataException("Radial plans require only stacked polar ARC layers.");
        foreach (var layer in Layers)
            foreach (var datum in layer.Data)
            {
                if (datum.RadialInterval is { } interval)
                {
                    if (!radial || layer.Mark != MarkKind.Arc || layer.Stack == StackMode.None || datum.IsGap || interval.Start < 0m || interval.End < interval.Start ||
                        interval.Maximum <= 0m || interval.End > interval.Maximum || interval.EndAngle <= interval.StartAngle || interval.EndAngle - interval.StartAngle > 360m)
                        throw new InvalidDataException("Invalid resolved radial interval.");
                }
                else if (radial && !datum.IsGap)
                    throw new InvalidDataException("Stacked radial data require resolved intervals.");
            }
        ChartContractValidation.RequireName(SpecId, nameof(SpecId));
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            throw new InvalidDataException("Plot bounds must have positive width and height.");
        if (string.IsNullOrWhiteSpace(AccessibleSummary))
            throw new InvalidDataException("A PlotPlan must include an accessible summary.");
        if (!Style.IsDefault)
            ChartContractValidation.RequireUnique(Style.Select(token => token.Name), "plot style token");

        ChartContractValidation.RequireUnique(Scales.Select(scale => scale.Id), "resolved scale id");
        ChartContractValidation.RequireUnique(Series.Select(series => series.Key), "series key", composition ? StringComparer.Ordinal : null);
        ChartContractValidation.RequireUnique(Layers.Select(layer => layer.Id), "resolved layer id");
        if (!Facets.IsDefault) ChartContractValidation.RequireUnique(Facets.Select(facet => facet.Id), "resolved facet id");
        ChartContractValidation.RequireUnique(Palette.Select(entry => entry.SeriesKey), "palette series key", composition ? StringComparer.Ordinal : null);
        ChartContractValidation.RequireUnique(Legend.Select(entry => entry.SeriesKey), "legend series key", composition ? StringComparer.Ordinal : null);

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

    private void ValidateConnectedComposition()
    {
        var facetRows = (Facets.IsDefault ? [] : Facets).ToDictionary(facet => facet.Id, facet => (IReadOnlySet<int>)facet.RowIndices.ToHashSet(), StringComparer.Ordinal);
        if (!Facets.IsDefault && Facets.SelectMany(facet => facet.RowIndices).GroupBy(index => index).Any(group => group.Count() > 1))
            throw new InvalidDataException("Connected facets must have disjoint source row membership.");
        foreach (var layer in Layers)
        {
            if (layer.Connections.IsDefault)
            {
                if (!layer.ConnectionSkippedRows.IsDefault)
                    throw new InvalidDataException("Only connected layers carry connection skipped-row metadata.");
                continue;
            }
            var connect = ResolvedMarkConnection.ConnectsAcrossNulls(layer);
            var zero = ResolvedMarkConnection.FillsNullsWithZero(layer);
            if (layer.Mark is not (MarkKind.Line or MarkKind.Area) || layer.Position is not (null or { Kind: PositionAdjustmentKind.Identity }) ||
                !layer.Style.Any(token => token.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase) && ResolvedConnectionGeometry.Supports(token.Value)) ||
                !layer.Style.Any(token => token.Name.Equals("nullHandling", StringComparison.OrdinalIgnoreCase) && token.Value.Equals(zero ? "ZERO" : connect ? "CONNECT" : "GAP", StringComparison.OrdinalIgnoreCase)) ||
                layer.Mark == MarkKind.Area && !layer.AreaRibbon && !layer.Style.Any(token => token.Name.Equals("areaBaseline", StringComparison.OrdinalIgnoreCase) && token.Value.Equals("ZERO", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Connected compositions require LINE or zero-baseline AREA/ribbons with supported interpolation, IDENTITY and GAP, CONNECT or ZERO.");
            if (zero)
                foreach (var datum in layer.Data)
                {
                    var expectedCoordinates = ResolvedConnectionCoordinates.FromRaw(datum, layer.AreaRibbon, true);
                    if (datum.ConnectionCoordinates != expectedCoordinates || datum.IsGap != (expectedCoordinates is null))
                        throw new InvalidDataException("ZERO geometry must preserve raw X and ribbon bounds, fill only null scalar Y, and gap incomplete endpoints.");
                    foreach (var channel in datum.Channels.Where(channel => channel.Channel is FieldChannel.X or FieldChannel.Y or FieldChannel.YStart or FieldChannel.YEnd))
                        if (channel.Value.Kind is not (ChartValueKind.Null or ChartValueKind.Integer or ChartValueKind.Decimal or ChartValueKind.FloatingPoint))
                            throw new InvalidDataException("ZERO connected coordinates require numeric or null raw channels.");
                    if (!datum.Encodings.IsDefaultOrEmpty && datum.Encodings.Any(value => value.Channel is not (ConditionalEncodingChannel.Color or ConditionalEncodingChannel.Opacity) && (!layer.ConnectionDecorations || !ResolvedConnectionDecoration.IsDecorationChannel(value.Channel))))
                        throw new InvalidDataException("ZERO connected presentation supports connection channels and declared row decorations, including gap rows.");
                }
            var skipped = connect ? layer.Data.Where(datum => !ResolvedMarkConnection.HasCompleteCoordinates(datum, layer.AreaRibbon, layer.AreaConfidence)).Select(datum => datum.RowIndex).Distinct().Order().ToArray() : [];
            if (layer.ConnectionSkippedRows.IsDefault || !layer.ConnectionSkippedRows.SequenceEqual(skipped) || connect && layer.Data.Any(datum => datum.IsGap))
                throw new InvalidDataException("Each connected layer must retain its own incomplete CONNECT rows.");
            foreach (var datum in layer.Data.Where(datum => !datum.IsGap))
            {
                foreach (var channel in layer.AreaRibbon ? new[] { FieldChannel.X, FieldChannel.YStart, FieldChannel.YEnd } : new[] { FieldChannel.X, FieldChannel.Y })
                {
                    var kind = datum.Channels.FirstOrDefault(value => value.Channel == channel)?.Value.Kind;
                    if (kind is not (ChartValueKind.Integer or ChartValueKind.Decimal or ChartValueKind.FloatingPoint) && !(connect && kind is null or ChartValueKind.Null) && !(zero && channel == FieldChannel.Y && kind == ChartValueKind.Null))
                        throw new InvalidDataException("Connected composition endpoints require numeric coordinates or incomplete CONNECT rows.");
                }
                if (!datum.Encodings.IsDefaultOrEmpty && datum.Encodings.Any(value => value.Channel is not (ConditionalEncodingChannel.Color or ConditionalEncodingChannel.Opacity) && (!layer.ConnectionDecorations || !ResolvedConnectionDecoration.IsDecorationChannel(value.Channel))))
                    throw new InvalidDataException("Connected composition presentation supports connection channels and declared row decorations.");
            }
            if (facetRows.Count > 0 && layer.Data.Any(datum => !facetRows.Values.Any(rows => rows.Contains(datum.RowIndex))))
                throw new InvalidDataException("Every connected source row must belong to a resolved facet.");
            var expected = facetRows.Count == 0
                ? ResolvedMarkConnection.ExpectedPairs(layer).Select(pair => (pair.SourceIndex, pair.DestinationIndex, FacetId: (string?)null))
                : Facets.SelectMany(facet => ResolvedMarkConnection.ExpectedPairs(layer, facetRows[facet.Id]).Select(pair => (pair.SourceIndex, pair.DestinationIndex, FacetId: (string?)facet.Id)));
            if (!layer.Connections.Select(connection => (connection.SourceIndex, connection.DestinationIndex, connection.FacetId)).SequenceEqual(expected))
                throw new InvalidDataException("Connections must cover each adjacent eligible pair within its own facet exactly once.");
            foreach (var connection in layer.Connections)
                connection.Validate(layer, connection.FacetId is null ? null : facetRows[connection.FacetId]);
            if (layer.ConnectionInterpolation is not null)
            {
                var geometry = ResolvedConnectionGeometry.Resolve(layer);
                for (var index = 0; index < geometry.Length; index++)
                    if (layer.Connections[index].Geometry != geometry[index])
                        throw new InvalidDataException("Connection geometry must preserve eligible-run interpolation, endpoints and ribbon boundaries.");
            }
        }
    }
}
