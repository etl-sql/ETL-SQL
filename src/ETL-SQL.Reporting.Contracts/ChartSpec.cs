using System.Collections.Immutable;

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
    Y,
    Y2,
    Color,
    Size,
    Shape,
    Theta,
    Radius,
    Text,
    Tooltip,
    Detail,
    Row,
    Column
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
    Text
}

public enum CoordinateKind
{
    Cartesian,
    TransposedCartesian,
    Polar,
    Geographic
}

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

public sealed record FieldBinding(
    FieldChannel Channel,
    string Field,
    DataSemanticKind SemanticKind,
    string? ScaleId = null,
    AxisRole Axis = AxisRole.None,
    SortDirection Sort = SortDirection.None,
    string? Format = null);

public sealed record MarkLayerSpec(
    string Id,
    MarkKind Mark,
    int ZIndex,
    ImmutableArray<FieldBinding> Bindings,
    ImmutableArray<StyleToken> Style,
    string? LegendTitle = null);

public sealed record StyleToken(string Name, string Value);

public sealed record CoordinateSpec(
    CoordinateKind Kind,
    decimal? StartAngle = null,
    decimal? EndAngle = null,
    decimal? InnerRadius = null);

public sealed record ScaleSpec(
    string Id,
    FieldChannel Channel,
    ScaleKind Kind,
    bool IncludeZero,
    ImmutableArray<string> CategoryOrder,
    ChartValue? DomainMinimum = null,
    ChartValue? DomainMaximum = null);

public sealed record ScaleResolutionSpec(
    ScaleResolutionMode X = ScaleResolutionMode.Shared,
    ScaleResolutionMode Y = ScaleResolutionMode.Shared,
    ScaleResolutionMode Color = ScaleResolutionMode.Shared);

public sealed record FacetSpec(
    string? RowField,
    string? ColumnField,
    ScaleResolutionSpec Resolution);

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
            ChartContractVersions.Current,
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
        ChartContractValidation.RequireVersion(Schema, Version, ChartContractVersions.ChartSpecSchema, nameof(ChartSpec));
        ChartContractValidation.RequireName(Id, nameof(Id));
        ChartContractValidation.RequireName(DataReference, nameof(DataReference));
        ChartContractValidation.RequireUnique(Layers.Select(layer => layer.Id), "layer id");
        ChartContractValidation.RequireUnique(Scales.Select(scale => scale.Id), "scale id");

        if (Layers.IsDefaultOrEmpty)
            throw new InvalidDataException("A ChartSpec must contain at least one mark layer.");

        var scaleIds = Scales.Select(scale => scale.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var scale in Scales)
        {
            ChartContractValidation.RequireName(scale.Id, "scale id");
            if (scale.CategoryOrder.IsDefault)
                throw new InvalidDataException($"Scale '{scale.Id}' category order must be initialized.");
            scale.DomainMinimum?.Validate();
            scale.DomainMaximum?.Validate();
        }
        foreach (var binding in Bindings.Concat(Layers.SelectMany(layer => layer.Bindings)))
        {
            ChartContractValidation.RequireName(binding.Field, "binding field");
            if (binding.ScaleId is not null && !scaleIds.Contains(binding.ScaleId))
                throw new InvalidDataException($"Binding scale '{binding.ScaleId}' is not declared by the ChartSpec.");
        }

        foreach (var layer in Layers)
            if (layer.ZIndex < 0)
                throw new InvalidDataException($"Layer '{layer.Id}' has a negative z-index.");
    }
}
