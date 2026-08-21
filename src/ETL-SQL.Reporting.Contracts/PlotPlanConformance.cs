using System.Collections.Immutable;

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
    ImmutableArray<int> GapRows);

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
                layer.Data.Where(datum => datum.IsGap).Select(datum => datum.RowIndex).ToImmutableArray())).ToImmutableArray(),
            plan.Nulls.GapRows,
            plan.Nulls.SkippedRows,
            plan.AccessibleSummary,
            plan.Fallback);
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
        AddIfDifferent(backend, "series-order", expected.SeriesOrder, actual.SeriesOrder, issues);
        AddIfDifferent(backend, "palette", expected.Palette, actual.Palette, issues);
        AddIfDifferent(backend, "legend", expected.Legend, actual.Legend, issues);
        if (!LayersEqual(expected.Layers, actual.Layers))
            issues.Add(new PlotConformanceIssue(backend, "layers", "layers differs from the PlotPlan."));
        AddIfDifferent(backend, "null-gaps", expected.GapRows, actual.GapRows, issues);
        AddIfDifferent(backend, "null-skips", expected.SkippedRows, actual.SkippedRows, issues);
        if (!string.Equals(expected.AccessibleSummary, actual.AccessibleSummary, StringComparison.Ordinal))
            issues.Add(new PlotConformanceIssue(backend, "accessibility", "Accessible summary differs from the PlotPlan."));
        if (expected.Fallback != actual.Fallback)
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
            && pair.First.ZIndex == pair.Second.ZIndex
            && pair.First.RowOrder.SequenceEqual(pair.Second.RowOrder)
            && pair.First.GapRows.SequenceEqual(pair.Second.GapRows));
}
