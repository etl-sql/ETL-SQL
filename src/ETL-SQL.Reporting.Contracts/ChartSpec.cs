using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

public enum DataSemanticKind
{
    Quantitative,
    Temporal,
    Nominal,
    Ordinal
}

public enum FieldChannel
{
    X,
    X2,
    XStart,
    XEnd,
    XOffset,
    Y,
    Y2,
    YStart,
    YEnd,
    YOffset,
    Low,
    Q1,
    Median,
    Q3,
    High,
    Mean,
    Open,
    Close,
    ErrorLow,
    ErrorHigh,
    ConfidenceLow,
    ConfidenceHigh,
    Color,
    Size,
    Shape,
    Theta,
    Radius,
    Longitude,
    Latitude,
    Region,
    Route,
    Text,
    Tooltip,
    Detail,
    Row,
    Column,
    Wrap
}

public enum AxisRole
{
    None,
    Primary,
    Secondary
}

public enum MarkKind
{
    Rect,
    Line,
    Area,
    Point,
    Rule,
    Arc,
    Text,
    Tick
}

public enum CoordinateKind
{
    Cartesian,
    TransposedCartesian,
    Polar,
    Geographic
}

public enum GeographicProjectionKind { Equirectangular, Mercator }
public enum GeographicMapSourceKind { BuiltIn, File }

public sealed record GeographicCoordinateSpec(
    GeographicProjectionKind Projection,
    GeographicMapSourceKind SourceKind,
    string Source,
    string FeatureKey = "name");

public enum ScaleKind
{
    Linear,
    Logarithmic,
    Time,
    Band,
    Point,
    Ordinal,
    Identity
}

public enum ScaleResolutionMode
{
    Shared,
    Independent
}

public enum SortDirection
{
    None,
    Ascending,
    Descending
}

public enum ConditionalEncodingChannel { Color, Opacity, Size, Shape, Text }
public enum PredicateKind { Comparison, And, Or, Not, IsNull, IsNotNull, Truthy }
public enum ComparisonKind { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual }
public enum PredicateOperandKind { Field, Literal }

public sealed record PredicateOperand(PredicateOperandKind Kind, string? Field, ChartValue? Literal);

public sealed record EncodingPredicate(
    PredicateKind Kind,
    PredicateOperand? Left = null,
    PredicateOperand? Right = null,
    ComparisonKind? Comparison = null,
    EncodingPredicate? First = null,
    EncodingPredicate? Second = null);

public sealed record EncodingConditionSpec(
    ConditionalEncodingChannel Channel,
    EncodingPredicate Predicate,
    ChartValue WhenTrue,
    ChartValue? WhenFalse = null);

public enum NullValuePolicy
{
    Gap,
    Skip,
    Zero,
    Preserve
}

public enum SelectionMode
{
    None,
    Single,
    Multiple,
    Interval
}

public enum InteractionEffect
{
    Highlight,
    Filter,
    SetParameter,
    Drill,
    Navigate
}

public enum BindingSourceKind
{
    Field,
    Datum,
    Value
}

public enum StackMode
{
    None,
    Zero,
    Normalize
}

public enum PositionAdjustmentKind { Identity, Jitter, Nudge }
public enum PositionAdjustmentUnit { Data, Band, Em }
public enum TickOrientation { Auto, Horizontal, Vertical }
public enum ColorRangeKind { Gradient, Diverging }

public sealed record ColorRangeSpec(
    ColorRangeKind Kind,
    string Low,
    string High,
    string? Mid = null,
    decimal? Midpoint = null,
    string NullColor = "#9ca3af");

public sealed record PositionAdjustmentSpec(
    PositionAdjustmentKind Kind,
    decimal X = 0m,
    decimal Y = 0m,
    string? StableKeyField = null,
    int Seed = 0,
    PositionAdjustmentUnit Unit = PositionAdjustmentUnit.Band);

public sealed record FieldBinding(
    FieldChannel Channel,
    string? Field,
    DataSemanticKind SemanticKind,
    string? ScaleId = null,
    AxisRole Axis = AxisRole.None,
    SortDirection Sort = SortDirection.None,
    string? Format = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public BindingSourceKind SourceKind { get; init; } = BindingSourceKind.Field;
    public ChartValue? Constant { get; init; }
    public string? Parameter { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public StackMode Stack { get; init; }

    public static FieldBinding Datum(FieldChannel channel, ChartValue value, DataSemanticKind semanticKind,
        string? scaleId = null, AxisRole axis = AxisRole.None, string? parameter = null) =>
        new(channel, null, semanticKind, scaleId, axis)
        {
            SourceKind = BindingSourceKind.Datum,
            Constant = value,
            Parameter = parameter
        };

    public static FieldBinding Value(FieldChannel channel, ChartValue value, DataSemanticKind semanticKind,
        string? parameter = null) => new(channel, null, semanticKind)
        {
            SourceKind = BindingSourceKind.Value,
            Constant = value,
            Parameter = parameter
        };
}

public sealed record MarkLayerSpec(
    string Id,
    MarkKind Mark,
    int ZIndex,
    ImmutableArray<FieldBinding> Bindings,
    ImmutableArray<StyleToken> Style,
    string? LegendTitle = null)
{
    public ImmutableArray<EncodingConditionSpec> Conditions { get; init; } = [];
    public decimal BandSize { get; init; } = .75m;
    public decimal TickThickness { get; init; } = .15m;
    public TickOrientation TickOrientation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PositionAdjustmentSpec? Position { get; init; }
}

public sealed record StyleToken(string Name, string Value);

public sealed record CoordinateSpec(
    CoordinateKind Kind,
    decimal? StartAngle = null,
    decimal? EndAngle = null,
    decimal? InnerRadius = null,
    decimal? AspectRatio = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeographicCoordinateSpec? Geography { get; init; }
}

public sealed record ScaleSpec(
    string Id,
    FieldChannel Channel,
    ScaleKind Kind,
    bool IncludeZero,
    ImmutableArray<string> CategoryOrder,
    ChartValue? DomainMinimum = null,
    ChartValue? DomainMaximum = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Reverse = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MajorTickCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? TickInterval = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool MinorTicks = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LabelRotation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? LabelSkip = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] decimal OuterPadding = 0m)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ColorRangeSpec? ColorRange { get; init; }
}

public sealed record ScaleResolutionSpec(
    ScaleResolutionMode X = ScaleResolutionMode.Shared,
    ScaleResolutionMode Y = ScaleResolutionMode.Shared,
    ScaleResolutionMode Color = ScaleResolutionMode.Shared);

public sealed record FacetSpec(
    string? RowField,
    string? ColumnField,
    ScaleResolutionSpec Resolution,
    string? WrapField = null,
    int? Columns = null);

public sealed record FieldFormat(string Field, string? Format, string? NullLabel = null);

public sealed record FormattingSpec(
    string Locale,
    string TimeZone,
    string NullLabel,
    ImmutableArray<FieldFormat> Fields);

public sealed record FieldNullPolicy(string Field, NullValuePolicy Policy);

public sealed record NullHandlingSpec(
    NullValuePolicy Default,
    ImmutableArray<FieldNullPolicy> Fields);

public sealed record SelectionSpec(string Id, SelectionMode Mode, ImmutableArray<string> Fields);

public sealed record InteractionBinding(
    string Trigger,
    InteractionEffect Effect,
    string? Target = null,
    string? Parameter = null);

public sealed record InteractionSpec(
    ImmutableArray<SelectionSpec> Selections,
    ImmutableArray<InteractionBinding> Bindings);

public sealed record ThemeSpec(string Name, ImmutableArray<StyleToken> Tokens);

public sealed record AccessibilitySpec(
    string Label,
    string? Description,
    string? SummaryTemplate,
    bool IncludeDataTableFallback);

public sealed record ChartSpec(
    string Schema,
    int Version,
    string Id,
    string? Title,
    string DataReference,
    ImmutableArray<FieldBinding> Bindings,
    ImmutableArray<MarkLayerSpec> Layers,
    CoordinateSpec Coordinate,
    ImmutableArray<ScaleSpec> Scales,
    ScaleResolutionSpec ScaleResolution,
    FacetSpec? Facet,
    FormattingSpec Formatting,
    NullHandlingSpec NullHandling,
    InteractionSpec Interactions,
    ThemeSpec Theme,
    AccessibilitySpec Accessibility) : IVersionedChartContract
{
    public static ChartSpec Create(
        string id,
        string dataReference,
        ImmutableArray<FieldBinding> bindings,
        ImmutableArray<MarkLayerSpec> layers,
        CoordinateSpec coordinate,
        ImmutableArray<ScaleSpec> scales,
        FormattingSpec formatting,
        NullHandlingSpec nullHandling,
        ThemeSpec theme,
        AccessibilitySpec accessibility,
        string? title = null,
        ScaleResolutionSpec? scaleResolution = null,
        FacetSpec? facet = null,
        InteractionSpec? interactions = null) => new(
            ChartContractVersions.ChartSpecSchema,
            ChartContractVersions.ChartSpecCurrent,
            id,
            title,
            dataReference,
            bindings,
            layers,
            coordinate,
            scales,
            scaleResolution ?? new ScaleResolutionSpec(),
            facet,
            formatting,
            nullHandling,
            interactions ?? new InteractionSpec([], []),
            theme,
            accessibility);

    public void Validate()
    {
        ChartContractValidation.RequireVersion(Schema, Version, ChartContractVersions.ChartSpecSchema, ChartContractVersions.ChartSpecCurrent, nameof(ChartSpec));
        ChartContractValidation.RequireName(Id, nameof(Id));
        ChartContractValidation.RequireName(DataReference, nameof(DataReference));
        ChartContractValidation.RequireUnique(Layers.Select(layer => layer.Id), "layer id");
        ChartContractValidation.RequireUnique(Scales.Select(scale => scale.Id), "scale id");

        if (Layers.IsDefaultOrEmpty)
            throw new InvalidDataException("A ChartSpec must contain at least one mark layer.");
        if (Coordinate.Kind == CoordinateKind.Polar && Coordinate.InnerRadius is < 0m or >= 1m)
            throw new InvalidDataException("Polar inner radius must be at least zero and less than one.");
        if (Coordinate.Kind == CoordinateKind.Geographic)
        {
            if (Coordinate.Geography is null)
                throw new InvalidDataException("Geographic coordinates require a geography contract.");
            ChartContractValidation.RequireName(Coordinate.Geography.Source, "geographic map source");
            ChartContractValidation.RequireName(Coordinate.Geography.FeatureKey, "geographic feature key");
            if (Facet is not null)
                throw new InvalidDataException("Geographic coordinates do not support facets.");
        }
        else if (Coordinate.Geography is not null)
            throw new InvalidDataException("A geography contract requires Geographic coordinates.");
        if (Coordinate.AspectRatio is <= 0m)
            throw new InvalidDataException("Cartesian ASPECT_RATIO must be greater than zero.");
        if (Coordinate.AspectRatio is not null)
        {
            if (Coordinate.Kind != CoordinateKind.Cartesian)
                throw new InvalidDataException("ASPECT_RATIO currently supports CARTESIAN coordinates only.");
            var xScale = Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.X);
            var yScale = Scales.FirstOrDefault(scale => scale.Channel == FieldChannel.Y);
            if (xScale?.Kind is not (ScaleKind.Linear or ScaleKind.Logarithmic) ||
                yScale?.Kind is not (ScaleKind.Linear or ScaleKind.Logarithmic))
                throw new InvalidDataException("ASPECT_RATIO requires continuous quantitative primary X and Y scales.");
        }
        if (Facet is not null)
        {
            if (string.IsNullOrWhiteSpace(Facet.RowField) && string.IsNullOrWhiteSpace(Facet.ColumnField) && string.IsNullOrWhiteSpace(Facet.WrapField))
                throw new InvalidDataException("A facet must declare ROW, COLUMN, or WRAP.");
            if (Facet.WrapField is not null && (Facet.RowField is not null || Facet.ColumnField is not null))
                throw new InvalidDataException("FACET WRAP is mutually exclusive with ROW and COLUMN.");
            if (Facet.Columns is not null && Facet.WrapField is null)
                throw new InvalidDataException("FACET COLUMNS may be used only with WRAP.");
            if (Facet.Columns is < 1 or > 12)
                throw new InvalidDataException("FACET COLUMNS must be between 1 and 12.");
            if (Facet.RowField is not null && Facet.RowField.Equals(Facet.ColumnField, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Facet row and column fields must be different.");
        }

        var scaleIds = Scales.Select(scale => scale.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var scale in Scales)
        {
            ChartContractValidation.RequireName(scale.Id, "scale id");
            if (scale.CategoryOrder.IsDefault)
                throw new InvalidDataException($"Scale '{scale.Id}' category order must be initialized.");
            scale.DomainMinimum?.Validate();
            scale.DomainMaximum?.Validate();
            if (scale.Kind == ScaleKind.Logarithmic && scale.IncludeZero)
                throw new InvalidDataException($"Logarithmic scale '{scale.Id}' cannot include zero.");
            if (scale.ColorRange is { } range)
            {
                if ((scale.Channel != FieldChannel.Color && scale.Channel != FieldChannel.Size) || scale.Kind is not (ScaleKind.Linear or ScaleKind.Logarithmic))
                    throw new InvalidDataException($"Scale '{scale.Id}' color RANGE requires a quantitative COLOR linear/logarithmic scale.");
                ValidatePortableColor(range.Low, scale.Id);
                ValidatePortableColor(range.High, scale.Id);
                ValidatePortableColor(range.NullColor, scale.Id);
                if (range.Kind == ColorRangeKind.Diverging && (range.Mid is null || range.Midpoint is null))
                    throw new InvalidDataException($"DIVERGING range on scale '{scale.Id}' requires MID and MIDPOINT.");
                if (range.Mid is not null) ValidatePortableColor(range.Mid, scale.Id);
                if (!Bindings.Concat(Layers.SelectMany(layer => layer.Bindings)).Any(binding =>
                    binding.ScaleId == scale.Id && (binding.Channel == FieldChannel.Color || binding.Channel == FieldChannel.Size) && binding.SemanticKind == DataSemanticKind.Quantitative))
                    throw new InvalidDataException($"Scale '{scale.Id}' color RANGE requires a quantitative COLOR binding.");
            }
        }
        foreach (var binding in Bindings.Concat(Layers.SelectMany(layer => layer.Bindings)))
        {
            if (binding.SourceKind == BindingSourceKind.Field)
            {
                ChartContractValidation.RequireName(binding.Field, "binding field");
                if (binding.Constant is not null || binding.Parameter is not null)
                    throw new InvalidDataException("A field binding cannot carry a constant or parameter dependency.");
            }
            else
            {
                if (binding.Field is not null)
                    throw new InvalidDataException($"A {binding.SourceKind.ToString().ToUpperInvariant()} binding cannot name a source field.");
                if (binding.Constant is null)
                    throw new InvalidDataException($"A {binding.SourceKind.ToString().ToUpperInvariant()} binding requires a typed constant.");
                binding.Constant.Validate();
                ValidateConstant(binding);
                if (binding.SourceKind == BindingSourceKind.Value && binding.ScaleId is not null)
                    throw new InvalidDataException("A VALUE binding bypasses data scales and cannot declare SCALE.");
                if (binding.SourceKind == BindingSourceKind.Value && binding.Axis != AxisRole.None)
                    throw new InvalidDataException("A VALUE binding cannot declare an axis.");
                if (binding.SourceKind == BindingSourceKind.Value && binding.Channel is FieldChannel.X or FieldChannel.X2 or FieldChannel.XStart or FieldChannel.XEnd or FieldChannel.Y or FieldChannel.Y2 or FieldChannel.YStart or FieldChannel.YEnd or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh or FieldChannel.Mean)
                    throw new InvalidDataException($"A visual-range VALUE cannot bind positional channel {binding.Channel}.");
            }
            if (binding.ScaleId is not null && !scaleIds.Contains(binding.ScaleId))
                throw new InvalidDataException($"Binding scale '{binding.ScaleId}' is not declared by the ChartSpec.");
            if (binding.ScaleId is not null)
            {
                var scale = Scales.First(item => item.Id.Equals(binding.ScaleId, StringComparison.OrdinalIgnoreCase));
                if (!CompatibleScaleChannel(scale.Channel, binding.Channel))
                    throw new InvalidDataException($"Binding channel {binding.Channel} is incompatible with scale '{scale.Id}' channel {scale.Channel}.");
                var compatibleKind = binding.SemanticKind switch
                {
                    DataSemanticKind.Quantitative => scale.Kind is ScaleKind.Linear or ScaleKind.Logarithmic or ScaleKind.Identity,
                    DataSemanticKind.Temporal => scale.Kind == ScaleKind.Time,
                    DataSemanticKind.Nominal or DataSemanticKind.Ordinal => scale.Kind is ScaleKind.Band or ScaleKind.Point or ScaleKind.Ordinal or ScaleKind.Identity,
                    _ => false
                };
                if (!compatibleKind)
                    throw new InvalidDataException($"Binding TYPE {binding.SemanticKind} is incompatible with {scale.Kind} scale '{scale.Id}'.");
            }
        }

        foreach (var layer in Layers)
        {
            if (layer.ZIndex < 0)
                throw new InvalidDataException($"Layer '{layer.Id}' has a negative z-index.");
            if (layer.BandSize <= 0m || layer.BandSize > 1m)
                throw new InvalidDataException($"Layer '{layer.Id}' BAND_SIZE must be greater than zero and at most one.");
            if (layer.TickThickness <= 0m || layer.TickThickness > 1m)
                throw new InvalidDataException($"Layer '{layer.Id}' TICK THICKNESS must be greater than zero and at most one em.");
            if (layer.Position is { } position)
            {
                if (position.Kind == PositionAdjustmentKind.Jitter && string.IsNullOrWhiteSpace(position.StableKeyField))
                    throw new InvalidDataException($"Layer '{layer.Id}' JITTER requires a stable KEY field.");
                if (position.Kind == PositionAdjustmentKind.Jitter && (position.X < 0m || position.Y < 0m || position.X > 1m || position.Y > 1m))
                    throw new InvalidDataException($"Layer '{layer.Id}' JITTER amplitudes must be between zero and one.");
                if (position.Kind == PositionAdjustmentKind.Nudge && position.Unit == PositionAdjustmentUnit.Data &&
                    Coordinate.Kind != CoordinateKind.Cartesian)
                    throw new InvalidDataException($"Layer '{layer.Id}' data-domain NUDGE requires Cartesian coordinates.");
            }
            foreach (var binding in layer.Bindings)
            {
                if (binding.Channel is FieldChannel.XOffset or FieldChannel.YOffset &&
                    binding.SemanticKind is not (DataSemanticKind.Nominal or DataSemanticKind.Ordinal))
                    throw new InvalidDataException($"Layer '{layer.Id}' offset channel {binding.Channel} requires NOMINAL or ORDINAL TYPE.");
                if (binding.Channel is FieldChannel.XOffset or FieldChannel.YOffset && binding.SourceKind == BindingSourceKind.Value)
                    throw new InvalidDataException($"Layer '{layer.Id}' offset channel {binding.Channel} cannot use a visual VALUE source.");
                if (binding.Channel is FieldChannel.ErrorLow or FieldChannel.ErrorHigh &&
                    (binding.SemanticKind != DataSemanticKind.Quantitative || binding.SourceKind == BindingSourceKind.Value ||
                     binding.Axis == AxisRole.Secondary))
                    throw new InvalidDataException($"Statistical error bar channel {binding.Channel} requires primary quantitative data.");
                if (binding.Channel is FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh &&
                    (binding.SemanticKind != DataSemanticKind.Quantitative || binding.SourceKind == BindingSourceKind.Value ||
                     binding.Axis == AxisRole.Secondary))
                    throw new InvalidDataException($"Confidence channel {binding.Channel} requires primary quantitative data.");
                if (binding.Stack != StackMode.None &&
                    (binding.SemanticKind != DataSemanticKind.Quantitative || binding.SourceKind == BindingSourceKind.Value ||
                     binding.Channel is not (FieldChannel.Y or FieldChannel.Y2) || Coordinate.Kind == CoordinateKind.Polar))
                    throw new InvalidDataException($"Layer '{layer.Id}' STACK requires a quantitative Cartesian/transposed Y or Y2 data-domain binding; polar/radial stacking is not yet portable.");
            }
            var stackModes = layer.Bindings.Where(binding => binding.Stack != StackMode.None).Select(binding => binding.Stack).Distinct().ToArray();
            if (stackModes.Length > 1)
                throw new InvalidDataException($"Layer '{layer.Id}' cannot declare multiple STACK modes.");
            if (layer.Bindings.Any(binding => binding.Stack != StackMode.None) &&
                layer.Bindings.Any(binding => binding.Channel is FieldChannel.XOffset or FieldChannel.YOffset))
                throw new InvalidDataException($"Layer '{layer.Id}' cannot combine STACK with an offset channel.");
            var channels = layer.Bindings.Select(binding => binding.Channel).ToHashSet();
            var hasErrorLow = channels.Contains(FieldChannel.ErrorLow);
            var hasErrorHigh = channels.Contains(FieldChannel.ErrorHigh);
            if (hasErrorLow || hasErrorHigh)
            {
                if (layer.Mark is not (MarkKind.Point or MarkKind.Rect))
                    throw new InvalidDataException($"{layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' does not support error bars; only POINT and RECT marks support error bars.");
                if (Coordinate.Kind is not (CoordinateKind.Cartesian or CoordinateKind.TransposedCartesian))
                    throw new InvalidDataException($"Layer '{layer.Id}' error bars require Cartesian or TransposedCartesian coordinates.");
                if (!hasErrorLow || !hasErrorHigh)
                    throw new InvalidDataException($"{layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' requires both ERROR_LOW and ERROR_HIGH.");
                var low = layer.Bindings.First(b => b.Channel == FieldChannel.ErrorLow);
                var high = layer.Bindings.First(b => b.Channel == FieldChannel.ErrorHigh);
                if (low.SemanticKind != DataSemanticKind.Quantitative || high.SemanticKind != DataSemanticKind.Quantitative)
                    throw new InvalidDataException($"{layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' error bar endpoints require QUANTITATIVE type.");

                var y = layer.Bindings.FirstOrDefault(b => b.Channel == FieldChannel.Y);
                if (y is null || y.SemanticKind != DataSemanticKind.Quantitative)
                    throw new InvalidDataException($"{layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' with error bars requires a quantitative primary Y binding.");

                if (y.Axis == AxisRole.Secondary || low.Axis == AxisRole.Secondary || high.Axis == AxisRole.Secondary)
                    throw new InvalidDataException($"Layer '{layer.Id}' error bars and Y binding must use the primary axis.");

                var yScaleId = y.ScaleId ?? Scales.FirstOrDefault(s => s.Channel == FieldChannel.Y)?.Id ?? "y";
                var lowScaleId = low.ScaleId ?? Scales.FirstOrDefault(s => s.Channel == FieldChannel.Y)?.Id ?? "y";
                var highScoreId = high.ScaleId ?? Scales.FirstOrDefault(s => s.Channel == FieldChannel.Y)?.Id ?? "y";
                if (!string.Equals(lowScaleId, highScoreId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(lowScaleId, yScaleId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Layer '{layer.Id}' ERROR_LOW, ERROR_HIGH, and Y must resolve to the same scale ID.");
            }
            var hasConfLow = channels.Contains(FieldChannel.ConfidenceLow);
            var hasConfHigh = channels.Contains(FieldChannel.ConfidenceHigh);
            if (hasConfLow || hasConfHigh)
            {
                if (layer.Mark != MarkKind.Area)
                    throw new InvalidDataException($"{layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' does not support confidence channels; only AREA marks support confidence channels.");
                if (Coordinate.Kind is not (CoordinateKind.Cartesian or CoordinateKind.TransposedCartesian))
                    throw new InvalidDataException($"Layer '{layer.Id}' confidence channels require Cartesian or TransposedCartesian coordinates.");
                if (!hasConfLow || !hasConfHigh)
                    throw new InvalidDataException($"{layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' requires both CONFIDENCE_LOW and CONFIDENCE_HIGH.");
                var low = layer.Bindings.First(b => b.Channel == FieldChannel.ConfidenceLow);
                var high = layer.Bindings.First(b => b.Channel == FieldChannel.ConfidenceHigh);
                if (low.SemanticKind != DataSemanticKind.Quantitative || high.SemanticKind != DataSemanticKind.Quantitative)
                    throw new InvalidDataException($"{layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' confidence endpoints require QUANTITATIVE type.");

                if (low.Axis == AxisRole.Secondary || high.Axis == AxisRole.Secondary)
                    throw new InvalidDataException($"Layer '{layer.Id}' confidence channels must use the primary axis.");

                var lowScaleId = low.ScaleId ?? Scales.FirstOrDefault(s => s.Channel == FieldChannel.Y)?.Id ?? "y";
                var highScoreId = high.ScaleId ?? Scales.FirstOrDefault(s => s.Channel == FieldChannel.Y)?.Id ?? "y";
                if (!string.Equals(lowScaleId, highScoreId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Layer '{layer.Id}' CONFIDENCE_LOW and CONFIDENCE_HIGH must resolve to the same scale ID.");

                if (!channels.Contains(FieldChannel.X))
                    throw new InvalidDataException($"{layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' with confidence channels requires an X binding.");

                if (channels.Contains(FieldChannel.Y) || channels.Contains(FieldChannel.Y2) ||
                    channels.Contains(FieldChannel.YStart) || channels.Contains(FieldChannel.YEnd))
                    throw new InvalidDataException($"AREA layer '{layer.Id}' cannot combine CONFIDENCE_LOW/CONFIDENCE_HIGH with Y, Y2, Y_START, or Y_END; confidence endpoints own the band's extent.");
            }
            if (Coordinate.Kind == CoordinateKind.Geographic)
            {
                var hasPoint = channels.Contains(FieldChannel.Longitude) && channels.Contains(FieldChannel.Latitude);
                if (layer.Mark == MarkKind.Rect && !channels.Contains(FieldChannel.Region))
                    throw new InvalidDataException($"Geographic RECT layer '{layer.Id}' requires REGION.");
                if (layer.Mark is MarkKind.Point or MarkKind.Text && !hasPoint)
                    throw new InvalidDataException($"Geographic {layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Id}' requires LONGITUDE and LATITUDE.");
                if (layer.Mark == MarkKind.Line && (!hasPoint || !channels.Contains(FieldChannel.Route)))
                    throw new InvalidDataException($"Geographic LINE layer '{layer.Id}' requires LONGITUDE, LATITUDE, and ROUTE.");
                if (layer.Mark is not (MarkKind.Rect or MarkKind.Point or MarkKind.Text or MarkKind.Line))
                    throw new InvalidDataException($"Geographic coordinates do not support {layer.Mark} layers.");
            }
            if (layer.Mark == MarkKind.Rect)
            {
                ValidateIntervalPair(layer, FieldChannel.XStart, FieldChannel.XEnd, "X_START/X_END");
                ValidateIntervalPair(layer, FieldChannel.YStart, FieldChannel.YEnd, "Y_START/Y_END");
                if (channels.Contains(FieldChannel.YStart) && (channels.Contains(FieldChannel.Y) || channels.Contains(FieldChannel.Y2)))
                    throw new InvalidDataException($"RECT layer '{layer.Id}' cannot combine Y or Y2 with Y_START/Y_END; the interval endpoints are the rectangle's extent.");
                if (channels.Contains(FieldChannel.XStart) && (channels.Contains(FieldChannel.X) || channels.Contains(FieldChannel.X2)))
                    throw new InvalidDataException($"RECT layer '{layer.Id}' cannot combine X or X2 with X_START/X_END; the interval endpoints are the rectangle's extent.");
            }
            if (layer.Mark == MarkKind.Area)
            {
                var hasStart = channels.Contains(FieldChannel.YStart);
                var hasEnd = channels.Contains(FieldChannel.YEnd);
                if (hasStart != hasEnd)
                    throw new InvalidDataException($"AREA layer '{layer.Id}' requires both Y_START and Y_END for a floating ribbon.");
                if (channels.Contains(FieldChannel.Y) && hasStart)
                    throw new InvalidDataException($"AREA layer '{layer.Id}' cannot combine Y with Y_START/Y_END.");
                if (hasStart)
                {
                    var start = layer.Bindings.First(binding => binding.Channel == FieldChannel.YStart);
                    var end = layer.Bindings.First(binding => binding.Channel == FieldChannel.YEnd);
                    if (start.SemanticKind != end.SemanticKind || start.SemanticKind is not (DataSemanticKind.Quantitative or DataSemanticKind.Temporal))
                        throw new InvalidDataException($"AREA layer '{layer.Id}' ribbon endpoints require matching quantitative or temporal types.");
                }
            }
            if (layer.Mark == MarkKind.Tick)
            {
                var x = layer.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.X);
                var y = layer.Bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Y);
                if (x?.SemanticKind is not (DataSemanticKind.Nominal or DataSemanticKind.Ordinal) ||
                    y?.SemanticKind != DataSemanticKind.Quantitative)
                    throw new InvalidDataException($"TICK layer '{layer.Id}' requires a nominal/ordinal X binding and a quantitative Y binding.");
            }
            if (layer.Mark == MarkKind.Rule)
            {
                ValidateIntervalPair(layer, FieldChannel.XStart, FieldChannel.XEnd, "X_START/X_END");
                ValidateIntervalPair(layer, FieldChannel.YStart, FieldChannel.YEnd, "Y_START/Y_END");
                if (channels.Contains(FieldChannel.X2) && !channels.Contains(FieldChannel.X))
                    throw new InvalidDataException($"RULE layer '{layer.Id}' requires X with X2.");
                if (channels.Contains(FieldChannel.Y2) && !channels.Contains(FieldChannel.Y))
                    throw new InvalidDataException($"RULE layer '{layer.Id}' requires Y with Y2.");
            }
            if (!layer.Style.IsDefault) ChartContractValidation.RequireUnique(layer.Style.Select(token => token.Name), $"style token in layer '{layer.Id}'");
            if (!layer.Conditions.IsDefaultOrEmpty && layer.Mark is MarkKind.Line or MarkKind.Area)
                throw new InvalidDataException($"Connected layer '{layer.Id}' cannot use row-level conditional encodings.");
            if (!layer.Conditions.IsDefault)
                foreach (var condition in layer.Conditions)
                {
                    condition.WhenTrue.Validate();
                    condition.WhenFalse?.Validate();
                    ValidatePredicate(condition.Predicate, layer.Id);
                }
        }
    }

    private static void ValidateConstant(FieldBinding binding)
    {
        var value = binding.Constant!;
        if (value.Kind == ChartValueKind.Null && binding.Channel is not (FieldChannel.Color or FieldChannel.Text or FieldChannel.Tooltip or FieldChannel.Detail))
            throw new InvalidDataException($"Channel {binding.Channel} does not support a null constant binding.");
        if (value.Kind == ChartValueKind.Null) return;
        var compatible = binding.SemanticKind switch
        {
            DataSemanticKind.Quantitative => value.Kind is ChartValueKind.Integer or ChartValueKind.FloatingPoint or ChartValueKind.Decimal,
            DataSemanticKind.Temporal => value.Kind is ChartValueKind.Date or ChartValueKind.Time or ChartValueKind.LocalDateTime or ChartValueKind.OffsetDateTime,
            DataSemanticKind.Nominal or DataSemanticKind.Ordinal => true,
            _ => false
        };
        if (!compatible)
            throw new InvalidDataException($"{binding.SourceKind.ToString().ToUpperInvariant()} value kind {value.Kind} is incompatible with declared {binding.SemanticKind} TYPE on {binding.Channel}.");
    }

    private static void ValidatePortableColor(string value, string scaleId)
    {
        if (value.Length != 7 || value[0] != '#' || value.Skip(1).Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException($"Scale '{scaleId}' color RANGE accepts portable #RRGGBB colors only; found '{value}'.");
    }

    private static void ValidateIntervalPair(MarkLayerSpec layer, FieldChannel start, FieldChannel end, string name)
    {
        var mark = layer.Mark.ToString().ToUpperInvariant();
        var first = layer.Bindings.FirstOrDefault(binding => binding.Channel == start);
        var second = layer.Bindings.FirstOrDefault(binding => binding.Channel == end);
        if ((first is null) == (second is null))
        {
            if (first is null) return;
            if (first.SemanticKind != second!.SemanticKind || first.SemanticKind is not (DataSemanticKind.Quantitative or DataSemanticKind.Temporal))
                throw new InvalidDataException($"{mark} layer '{layer.Id}' interval {name} requires matching quantitative or temporal endpoint types.");
            return;
        }
        throw new InvalidDataException($"{mark} layer '{layer.Id}' requires both endpoints in {name}.");
    }

    private static bool CompatibleScaleChannel(FieldChannel scale, FieldChannel binding) => scale == binding ||
        scale == FieldChannel.X && binding is FieldChannel.X2 or FieldChannel.XStart or FieldChannel.XEnd ||
        scale == FieldChannel.Y && binding is FieldChannel.Y2 or FieldChannel.YStart or FieldChannel.YEnd or
            FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or FieldChannel.High or FieldChannel.Mean or
            FieldChannel.Open or FieldChannel.Close or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or
            FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh;

    private static void ValidatePredicate(EncodingPredicate predicate, string layerId)
    {
        if (predicate.Kind == PredicateKind.Comparison &&
            (predicate.Comparison is null || predicate.Left is null || predicate.Right is null))
            throw new InvalidDataException($"Layer '{layerId}' has an incomplete comparison condition.");
        if (predicate.Kind is PredicateKind.And or PredicateKind.Or &&
            (predicate.First is null || predicate.Second is null))
            throw new InvalidDataException($"Layer '{layerId}' has an incomplete logical condition.");
        if (predicate.Kind == PredicateKind.Not && predicate.First is null)
            throw new InvalidDataException($"Layer '{layerId}' has an incomplete NOT condition.");
        predicate.Left?.Literal?.Validate();
        predicate.Right?.Literal?.Validate();
        if (predicate.First is not null) ValidatePredicate(predicate.First, layerId);
        if (predicate.Second is not null) ValidatePredicate(predicate.Second, layerId);
    }
}
