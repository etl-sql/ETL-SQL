using System.Collections.Immutable;

namespace ETL_SQL.Reporting.Semantics;

public enum SemanticFallbackKind
{
    Summary,
    RankedTable,
    TimeSeriesTable,
    ProportionalBreakdown,
    Hierarchy,
    NetworkConnections
}

public sealed record PlotBounds(decimal X, decimal Y, decimal Width, decimal Height);

public sealed record PlotTick(ChartValue Value, string Label);

public sealed record ResolvedScale(
    string Id,
    FieldChannel Channel,
    ScaleKind Kind,
    ImmutableArray<ChartValue> Domain,
    ImmutableArray<string> Categories,
    ImmutableArray<PlotTick> Ticks,
    bool IncludesZero);

public sealed record ResolvedSeries(string Key, string Label, int Order, string Color);

public sealed record PaletteAssignment(string SeriesKey, string Color);

public sealed record LegendEntry(string SeriesKey, string Label, int Order, string Color);

public sealed record ResolvedChannelValue(FieldChannel Channel, ChartValue Value, string? DisplayValue);

public sealed record ResolvedDatum(
    int RowIndex,
    ImmutableArray<ResolvedChannelValue> Channels,
    bool IsGap,
    string? Tooltip);

public sealed record ResolvedMarkLayer(
    string Id,
    MarkKind Mark,
    int ZIndex,
    string? SeriesKey,
    ImmutableArray<ResolvedDatum> Data);

public sealed record ResolvedNullPolicy(
    NullValuePolicy Default,
    ImmutableArray<FieldNullPolicy> Fields,
    ImmutableArray<int> GapRows,
    ImmutableArray<int> SkippedRows);

public sealed record SemanticFallbackItem(string Label, string Value, int Order);

public sealed record SemanticFallback(
    SemanticFallbackKind Kind,
    string Heading,
    ImmutableArray<SemanticFallbackItem> Items);

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
        string? title = null) => new(
            ChartContractVersions.PlotPlanSchema,
            ChartContractVersions.Current,
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
            fallback);

    public void Validate()
    {
        ChartContractValidation.RequireVersion(Schema, Version, ChartContractVersions.PlotPlanSchema, nameof(PlotPlan));
        ChartContractValidation.RequireName(SpecId, nameof(SpecId));
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            throw new InvalidDataException("Plot bounds must have positive width and height.");
        if (string.IsNullOrWhiteSpace(AccessibleSummary))
            throw new InvalidDataException("A PlotPlan must include an accessible summary.");

        ChartContractValidation.RequireUnique(Scales.Select(scale => scale.Id), "resolved scale id");
        ChartContractValidation.RequireUnique(Series.Select(series => series.Key), "series key");
        ChartContractValidation.RequireUnique(Layers.Select(layer => layer.Id), "resolved layer id");
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
            foreach (var channel in datum.Channels)
                channel.Value.Validate();
    }
}
