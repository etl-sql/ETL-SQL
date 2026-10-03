using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ETL_SQL.Reporting.Semantics;

public sealed record ScaleSemanticProjection(
    string Id,
    ImmutableArray<ChartValue> Domain,
    ImmutableArray<string> Categories,
    ImmutableArray<PlotTick> Ticks);

public sealed record LayerSemanticProjection(
    string Id,
    MarkKind Mark,
    int ZIndex,
    ImmutableArray<int> RowOrder,
    ImmutableArray<int> GapRows)
{
    public string? AreaRibbonScaleId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AreaConfidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<ResolvedMarkConnection> Connections { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<int> ConnectionSkippedRows { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<ResolvedConnectionCoordinates?> ConnectionCoordinates { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ConnectionDecorations { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConnectedInterpolationKind? ConnectionInterpolation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConnectedInterpolationKind? PathInterpolation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<ResolvedConnectionDecoration?> Decorations { get; init; }
}

public sealed record PlotSemanticProjection(
    ImmutableArray<ScaleSemanticProjection> Scales,
    ImmutableArray<string> SeriesOrder,
    ImmutableArray<PaletteAssignment> Palette,
    ImmutableArray<LegendEntry> Legend,
    ImmutableArray<LayerSemanticProjection> Layers,
    ImmutableArray<int> GapRows,
    ImmutableArray<int> SkippedRows,
    string AccessibleSummary,
    SemanticFallback Fallback)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ResolvedCartesianAxes? CartesianAxes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CoordinateKind? ConnectedCoordinate { get; init; }

    public static PlotSemanticProjection FromPlan(PlotPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();
        return new PlotSemanticProjection(
            plan.Scales.Select(scale => new ScaleSemanticProjection(
                scale.Id,
                scale.Domain,
                scale.Categories,
                scale.Ticks)).ToImmutableArray(),
            plan.Series.Select(series => series.Key).ToImmutableArray(),
            plan.Palette,
            plan.Legend,
            plan.Layers.Select(layer => new LayerSemanticProjection(
                layer.Id,
                layer.Mark,
                layer.ZIndex,
                layer.Data.Select(datum => datum.RowIndex).ToImmutableArray(),
                layer.Data.Where(datum => datum.IsGap).Select(datum => datum.RowIndex).ToImmutableArray())
            {
                AreaRibbonScaleId = layer.AreaRibbonScaleId,
                Connections = layer.Connections,
                ConnectionSkippedRows = layer.ConnectionSkippedRows,
                ConnectionDecorations = layer.ConnectionDecorations,
                ConnectionInterpolation = layer.ConnectionInterpolation,
                PathInterpolation = layer.PathInterpolation,
                AreaConfidence = layer.AreaConfidence,
                Decorations = layer.ConnectionDecorations ? layer.Data.Select(datum => datum.ConnectionDecoration).ToImmutableArray() : default,
                ConnectionCoordinates = !layer.Connections.IsDefault && ResolvedMarkConnection.FillsNullsWithZero(layer) ? layer.Data.Select(datum => datum.ConnectionCoordinates).ToImmutableArray() : default
            }).ToImmutableArray(),
            plan.Nulls.GapRows,
            plan.Nulls.SkippedRows,
            plan.AccessibleSummary,
            plan.Fallback)
        { CartesianAxes = plan.CartesianAxes, ConnectedCoordinate = plan.Layers.Any(layer => !layer.Connections.IsDefault || layer.PathInterpolation is not null || layer.AreaConfidence) ? plan.Coordinate?.Kind : null };
    }
}

public interface IPlotPlanSemanticBackend
{
    string Name { get; }
    PlotSemanticProjection Project(PlotPlan plan);
}

public sealed record PlotConformanceIssue(string Backend, string SemanticArea, string Message);

public sealed record PlotConformanceReport(ImmutableArray<PlotConformanceIssue> Issues)
{
    public bool IsConformant => Issues.IsDefaultOrEmpty;
}

public static class PlotPlanConformanceHarness
{
    public static PlotConformanceReport Evaluate(
        PlotPlan plan,
        IEnumerable<IPlotPlanSemanticBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(backends);
        var expected = PlotSemanticProjection.FromPlan(plan);
        var issues = ImmutableArray.CreateBuilder<PlotConformanceIssue>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var backend in backends)
        {
            if (backend is null) throw new ArgumentException("A backend cannot be null.", nameof(backends));
            if (string.IsNullOrWhiteSpace(backend.Name)) throw new InvalidDataException("A semantic backend must have a name.");
            if (!names.Add(backend.Name)) throw new InvalidDataException($"Duplicate semantic backend '{backend.Name}'.");
            var actual = backend.Project(plan) ?? throw new InvalidDataException($"Backend '{backend.Name}' returned no projection.");
            Compare(backend.Name, expected, actual, issues);
        }

        return new PlotConformanceReport(issues.ToImmutable());
    }

    private static void Compare(
        string backend,
        PlotSemanticProjection expected,
        PlotSemanticProjection actual,
        ImmutableArray<PlotConformanceIssue>.Builder issues)
    {
        if (!ScalesEqual(expected.Scales, actual.Scales))
            issues.Add(new PlotConformanceIssue(backend, "scales", "scales differs from the PlotPlan."));
        if (expected.CartesianAxes != actual.CartesianAxes)
            issues.Add(new PlotConformanceIssue(backend, "axes", "Primary axis ownership differs from the PlotPlan."));
        if (expected.ConnectedCoordinate != actual.ConnectedCoordinate)
            issues.Add(new PlotConformanceIssue(backend, "coordinates", "Connected coordinate orientation differs from the PlotPlan."));
        AddIfDifferent(backend, "series-order", expected.SeriesOrder, actual.SeriesOrder, issues);
        AddIfDifferent(backend, "palette", expected.Palette, actual.Palette, issues);
        AddIfDifferent(backend, "legend", expected.Legend, actual.Legend, issues);
        if (!LayersEqual(expected.Layers, actual.Layers))
            issues.Add(new PlotConformanceIssue(backend, "layers", "layers differs from the PlotPlan."));
        AddIfDifferent(backend, "null-gaps", expected.GapRows, actual.GapRows, issues);
        AddIfDifferent(backend, "null-skips", expected.SkippedRows, actual.SkippedRows, issues);
        if (!string.Equals(expected.AccessibleSummary, actual.AccessibleSummary, StringComparison.Ordinal))
            issues.Add(new PlotConformanceIssue(backend, "accessibility", "Accessible summary differs from the PlotPlan."));
        // COMPAT_BREAK: 0.20.0 — compare fallback content across serialization, not immutable-array identity.
        if (actual.Fallback is not { } fallback || expected.Fallback.Kind != fallback.Kind || expected.Fallback.Heading != fallback.Heading ||
            expected.Fallback.Summary != fallback.Summary || expected.Fallback.Items.IsDefault != fallback.Items.IsDefault ||
            !expected.Fallback.Items.IsDefault && !expected.Fallback.Items.SequenceEqual(fallback.Items))
            issues.Add(new PlotConformanceIssue(backend, "fallback", "Semantic fallback differs from the PlotPlan."));
    }

    private static void AddIfDifferent<T>(
        string backend,
        string area,
        ImmutableArray<T> expected,
        ImmutableArray<T> actual,
        ImmutableArray<PlotConformanceIssue>.Builder issues)
    {
        if (!expected.SequenceEqual(actual))
            issues.Add(new PlotConformanceIssue(backend, area, $"{area} differs from the PlotPlan."));
    }

    private static bool ScalesEqual(
        ImmutableArray<ScaleSemanticProjection> expected,
        ImmutableArray<ScaleSemanticProjection> actual) =>
        expected.Length == actual.Length && expected.Zip(actual).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Domain.SequenceEqual(pair.Second.Domain)
            && pair.First.Categories.SequenceEqual(pair.Second.Categories)
            && pair.First.Ticks.SequenceEqual(pair.Second.Ticks));

    private static bool LayersEqual(
        ImmutableArray<LayerSemanticProjection> expected,
        ImmutableArray<LayerSemanticProjection> actual) =>
        expected.Length == actual.Length && expected.Zip(actual).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Mark == pair.Second.Mark
            && pair.First.AreaRibbonScaleId == pair.Second.AreaRibbonScaleId
            && pair.First.ConnectionDecorations == pair.Second.ConnectionDecorations
            && pair.First.ConnectionInterpolation == pair.Second.ConnectionInterpolation
            && pair.First.PathInterpolation == pair.Second.PathInterpolation
            && pair.First.AreaConfidence == pair.Second.AreaConfidence
            && pair.First.Decorations.IsDefault == pair.Second.Decorations.IsDefault
            && (pair.First.Decorations.IsDefault || pair.First.Decorations.SequenceEqual(pair.Second.Decorations))
            && ConnectionsEqual(pair.First.Connections, pair.Second.Connections)
            && pair.First.ConnectionSkippedRows.IsDefault == pair.Second.ConnectionSkippedRows.IsDefault
            && (pair.First.ConnectionSkippedRows.IsDefault || pair.First.ConnectionSkippedRows.SequenceEqual(pair.Second.ConnectionSkippedRows))
            && pair.First.ConnectionCoordinates.IsDefault == pair.Second.ConnectionCoordinates.IsDefault
            && (pair.First.ConnectionCoordinates.IsDefault || pair.First.ConnectionCoordinates.SequenceEqual(pair.Second.ConnectionCoordinates))
            && pair.First.ZIndex == pair.Second.ZIndex
            && pair.First.RowOrder.SequenceEqual(pair.Second.RowOrder)
            && pair.First.GapRows.SequenceEqual(pair.Second.GapRows));

    private static bool ConnectionsEqual(ImmutableArray<ResolvedMarkConnection> expected, ImmutableArray<ResolvedMarkConnection> actual) =>
        expected.IsDefault || actual.IsDefault ? expected.IsDefault == actual.IsDefault :
        expected.Length == actual.Length && expected.Zip(actual).All(pair =>
            pair.First.SourceIndex == pair.Second.SourceIndex && pair.First.DestinationIndex == pair.Second.DestinationIndex &&
            pair.First.SourceRowIndex == pair.Second.SourceRowIndex && pair.First.DestinationRowIndex == pair.Second.DestinationRowIndex &&
            pair.First.FacetId == pair.Second.FacetId && pair.First.Geometry == pair.Second.Geometry && pair.First.Encodings.SequenceEqual(pair.Second.Encodings));
}
