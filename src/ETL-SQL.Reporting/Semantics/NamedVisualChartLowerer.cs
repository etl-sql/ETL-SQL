using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Lowers the existing named Report-SQL visual syntax into renderer-neutral intent.</summary>
public sealed class NamedVisualChartLowerer
{
    private static readonly HashSet<VisualType> Supported =
    [
        VisualType.Bar, VisualType.HorizontalBar, VisualType.Line, VisualType.Scatter,
        VisualType.Bubble, VisualType.HeatMap, VisualType.Funnel, VisualType.Gauge,
        VisualType.BoxPlot, VisualType.Waterfall, VisualType.Candlestick,
        VisualType.Trellis,
        VisualType.Gantt,
        VisualType.Radar,
        VisualType.Pie, VisualType.Donut, VisualType.Combo
    ];

    public static bool Supports(VisualType type) => Supported.Contains(type);

    public ChartSpec Lower(CreateVisualStatement statement, VisualManifest manifest)
    {
        if (!Supports(statement.VisualType))
            throw new NotSupportedException($"Visual type {statement.VisualType} is not in the representative GoG slice.");

        var bindings = BuildBindings(statement, manifest).ToImmutableArray();
        var layers = BuildLayers(statement, bindings).ToImmutableArray();
        var scales = BuildScales(statement, bindings, manifest).ToImmutableArray();
        var title = manifest.Options.GetValueOrDefault("title") ?? manifest.Name;

        var spec = ChartSpec.Create(
            id: manifest.Name,
            dataReference: statement.Source.TempTableName ?? $"inline:{manifest.Name}",
            bindings: bindings,
            layers: layers,
            coordinate: new CoordinateSpec(IsPolar(statement.VisualType)
                ? CoordinateKind.Polar
                : statement.VisualType is VisualType.HorizontalBar or VisualType.Gantt
                    ? CoordinateKind.TransposedCartesian
                    : CoordinateKind.Cartesian,
                InnerRadius: statement.VisualType == VisualType.Donut ? ResolveInnerRadius(manifest) : null),
            scales: scales,
            formatting: new FormattingSpec(
                CultureInfo.InvariantCulture.Name,
                "UTC",
                manifest.Options.GetValueOrDefault("NULL_LABEL") ?? "",
                statement.Mappings.Select(mapping => new FieldFormat(mapping.Column, mapping.Format)).ToImmutableArray()),
            nullHandling: new NullHandlingSpec(
                statement.VisualType == VisualType.Line || statement.VisualType == VisualType.Combo ||
                    statement.VisualType == VisualType.Trellis && TrellisMark(statement) == MarkKind.Line
                    ? NullValuePolicy.Gap
                    : NullValuePolicy.Skip,
                []),
            theme: new ThemeSpec(manifest.Styles?.GetValueOrDefault("THEME") ?? "default", BuildStyleTokens(manifest)),
            accessibility: new AccessibilitySpec(
                title,
                manifest.Options.GetValueOrDefault("subtitle"),
                null,
                true),
            title: title,
            interactions: BuildInteractions(statement),
            facet: BuildFacet(statement, bindings));
        spec.Validate();
        return spec;
    }

    private static IEnumerable<FieldBinding> BuildBindings(CreateVisualStatement statement, VisualManifest manifest)
    {
        if (statement.VisualType == VisualType.Radar && statement.Mappings.Count == 0)
        {
            if (manifest.Columns.Count > 0)
                yield return new FieldBinding(FieldChannel.Color, manifest.Columns[0], DataSemanticKind.Nominal, "color");
            foreach (var column in manifest.Columns.Skip(1))
                yield return new FieldBinding(FieldChannel.Detail, column, DataSemanticKind.Quantitative);
            yield break;
        }
        foreach (var mapping in statement.Mappings)
        {
            var channel = MapChannel(statement.VisualType, mapping.Role);
            if (channel is null) continue;
            yield return new FieldBinding(
                channel.Value,
                mapping.Column,
                InferSemanticKind(statement.VisualType, channel.Value, mapping, statement.Options),
                ScaleId(channel.Value),
                channel == FieldChannel.Y2 ? AxisRole.Secondary : AxisRole.Primary,
                ParseSort(statement.Options),
                mapping.Format);
        }

        if (statement.VisualType == VisualType.Combo && statement.TypedSeries.Count > 0)
        {
            var used = statement.Mappings.Select(mapping => mapping.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var firstType = statement.TypedSeries[0].SeriesType;
            foreach (var series in statement.TypedSeries)
            {
                if (!used.Add(series.Column)) continue;
                var secondary = !series.SeriesType.Equals(firstType, StringComparison.OrdinalIgnoreCase);
                var channel = secondary ? FieldChannel.Y2 : FieldChannel.Y;
                yield return new FieldBinding(channel, series.Column, DataSemanticKind.Quantitative,
                    ScaleId(channel), secondary ? AxisRole.Secondary : AxisRole.Primary);
            }
        }
    }

    private static IEnumerable<MarkLayerSpec> BuildLayers(
        CreateVisualStatement statement,
        ImmutableArray<FieldBinding> bindings)
    {
        if (statement.VisualType == VisualType.Combo && statement.TypedSeries.Count > 0)
        {
            var x = bindings.Where(binding => binding.Channel == FieldChannel.X).ToImmutableArray();
            var firstType = statement.TypedSeries[0].SeriesType;
            for (var index = 0; index < statement.TypedSeries.Count; index++)
            {
                var series = statement.TypedSeries[index];
                var secondary = !series.SeriesType.Equals(firstType, StringComparison.OrdinalIgnoreCase);
                var channel = secondary ? FieldChannel.Y2 : FieldChannel.Y;
                var y = bindings.First(binding => binding.Field.Equals(series.Column, StringComparison.OrdinalIgnoreCase));
                yield return new MarkLayerSpec(
                    $"series-{index:D2}-{Sanitize(series.Column)}",
                    series.SeriesType.Equals("bar", StringComparison.OrdinalIgnoreCase) ? MarkKind.Rect : MarkKind.Line,
                    index,
                    x.Add(y with { Channel = channel, Axis = secondary ? AxisRole.Secondary : AxisRole.Primary }),
                    [new StyleToken("series", series.Column)],
                    series.Column);
            }
        }
        else
        {
            var style = statement.VisualType switch
            {
                VisualType.HeatMap => ImmutableArray.Create(new StyleToken("layout", "heatmap"), new StyleToken("preserveRows", "true")),
                VisualType.Funnel => ImmutableArray.Create(new StyleToken("layout", "funnel")),
                VisualType.Gauge => ImmutableArray.Create(new StyleToken("layout", "gauge")),
                VisualType.BoxPlot => ImmutableArray.Create(new StyleToken("layout", "boxplot"), new StyleToken("preserveRows", "true")),
                VisualType.Waterfall => ImmutableArray.Create(new StyleToken("layout", "waterfall"), new StyleToken("preserveRows", "true")),
                VisualType.Candlestick => ImmutableArray.Create(new StyleToken("layout", "candlestick"), new StyleToken("preserveRows", "true")),
                VisualType.Gantt => ImmutableArray.Create(new StyleToken("layout", "gantt"), new StyleToken("preserveRows", "true")),
                VisualType.Radar => ImmutableArray.Create(new StyleToken("layout", "radar"), new StyleToken("preserveRows", "true")),
                _ => ImmutableArray<StyleToken>.Empty
            };
            yield return new MarkLayerSpec(
                "primary",
                statement.VisualType switch
                {
                    VisualType.Bar or VisualType.HorizontalBar => MarkKind.Rect,
                    VisualType.Line => MarkKind.Line,
                    VisualType.Scatter or VisualType.Bubble => MarkKind.Point,
                    VisualType.HeatMap or VisualType.Funnel => MarkKind.Rect,
                    VisualType.BoxPlot or VisualType.Waterfall or VisualType.Candlestick => MarkKind.Rect,
                    VisualType.Trellis => TrellisMark(statement),
                    VisualType.Gantt => MarkKind.Rect,
                    VisualType.Radar => MarkKind.Line,
                    VisualType.Gauge => MarkKind.Arc,
                    VisualType.Pie or VisualType.Donut => MarkKind.Arc,
                    VisualType.Combo => MarkKind.Line,
                    _ => throw new InvalidOperationException()
                },
                0,
                bindings,
                style,
                statement.Name);
        }

        for (var index = 0; index < statement.Overlays.Count; index++)
        {
            var overlay = statement.Overlays[index];
            yield return new MarkLayerSpec(
                $"rule-{index:D2}-{overlay.OverlayType.ToString().ToLowerInvariant()}",
                overlay.OverlayType is OverlayType.Goal or OverlayType.Average ? MarkKind.Rule : MarkKind.Line,
                100 + index,
                [],
                [
                    new StyleToken("overlayType", overlay.OverlayType.ToString()),
                    new StyleToken("parameter", overlay.Parameter?.ToString(CultureInfo.InvariantCulture) ?? ""),
                    new StyleToken("lineStyle", overlay.LineStyle.ToString().ToLowerInvariant()),
                    new StyleToken("color", overlay.Color ?? "#888888"),
                    new StyleToken("label", overlay.Label ?? overlay.OverlayType.ToString())
                ],
                overlay.Label ?? overlay.OverlayType.ToString());
        }
    }

    private static IEnumerable<ScaleSpec> BuildScales(
        CreateVisualStatement statement,
        ImmutableArray<FieldBinding> bindings,
        VisualManifest manifest)
    {
        if (statement.VisualType == VisualType.Radar)
        {
            var dimensions = bindings.Where(binding => binding.Channel == FieldChannel.Detail).Select(binding => binding.Field).ToImmutableArray();
            var values = manifest.Rows.SelectMany(row => manifest.Columns.Skip(1).Select((_, index) => index + 1 < row.Count ? row[index + 1] : null))
                .Select(value => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? (decimal?)number : null)
                .Where(value => value.HasValue).Select(value => value!.Value).ToList();
            var minimum = ParseStandardDomain(manifest, "MIN") ?? ChartValue.From(0m);
            var maximum = ParseStandardDomain(manifest, "MAX") ?? ChartValue.From(values.DefaultIfEmpty(100m).Max() * 1.1m);
            var series = manifest.Rows
                .Select(row => row.Count > 0 ? row[0] : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray();
            yield return new ScaleSpec("theta", FieldChannel.Theta, ScaleKind.Ordinal, false, dimensions);
            yield return new ScaleSpec("radius", FieldChannel.Radius, ScaleKind.Linear, true, [], minimum, maximum);
            yield return new ScaleSpec("color", FieldChannel.Color, ScaleKind.Ordinal, false, series);
            yield break;
        }
        foreach (var group in bindings.Where(binding => binding.ScaleId is not null).GroupBy(binding => binding.ScaleId!))
        {
            var binding = group.First();
            var scaleChannel = group.Key.Equals("x", StringComparison.OrdinalIgnoreCase) && binding.Channel == FieldChannel.X2
                ? FieldChannel.X
                : group.Key.Equals("y", StringComparison.OrdinalIgnoreCase) &&
                binding.Channel is FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or
                    FieldChannel.High or FieldChannel.Open or FieldChannel.Close
                ? FieldChannel.Y
                : binding.Channel;
            var kind = binding.SemanticKind switch
            {
                DataSemanticKind.Temporal => ScaleKind.Time,
                DataSemanticKind.Quantitative => ScaleKind.Linear,
                DataSemanticKind.Ordinal => ScaleKind.Point,
                _ => binding.Channel is FieldChannel.Color ? ScaleKind.Ordinal : ScaleKind.Band
            };
            yield return new ScaleSpec(group.Key, scaleChannel, kind,
                IncludeZero: (scaleChannel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or FieldChannel.Size)
                    && statement.VisualType is not VisualType.Scatter and not VisualType.Bubble,
                CategoryOrder: [],
                DomainMinimum: GaugeBound(statement, manifest, binding.Channel, "MIN")
                    ?? ParseDomain(manifest, binding.Channel, "min") ?? ParseStandardDomain(manifest, "MIN"),
                DomainMaximum: GaugeBound(statement, manifest, binding.Channel, "MAX")
                    ?? ParseDomain(manifest, binding.Channel, "max") ?? ParseStandardDomain(manifest, "MAX"));
        }
    }

    private static InteractionSpec BuildInteractions(CreateVisualStatement statement)
    {
        var bindings = statement.Actions.Select(action => new InteractionBinding(
            action.Trigger,
            action switch
            {
                SetParameterAction => InteractionEffect.SetParameter,
                DrillDownAction or DrillInAction => InteractionEffect.Drill,
                DrillReportAction or NavigatePageAction => InteractionEffect.Navigate,
                _ => InteractionEffect.Highlight
            }))
            .Concat(statement.Interactions.Select(interaction => new InteractionBinding(
                interaction.Key,
                interaction.Value.Equals("FILTER", StringComparison.OrdinalIgnoreCase)
                    ? InteractionEffect.Filter
                    : InteractionEffect.Highlight)))
            .ToImmutableArray();
        return new InteractionSpec([], bindings);
    }

    private static ImmutableArray<StyleToken> BuildStyleTokens(VisualManifest manifest) =>
        (manifest.Styles ?? new Dictionary<string, string>())
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new StyleToken(pair.Key, pair.Value))
            .Concat(manifest.Options
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new StyleToken(pair.Key, pair.Value)))
            .GroupBy(token => token.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(token => token.Name, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

    private static FieldChannel? MapChannel(VisualType type, string role) => role.ToUpperInvariant() switch
    {
        "X" or "START" => FieldChannel.X,
        "X2" or "END" when type == VisualType.Gantt => FieldChannel.X2,
        "Y" or "LABEL" when type == VisualType.Gantt => FieldChannel.Y,
        "PROGRESS" when type == VisualType.Gantt => FieldChannel.Size,
        "MILESTONE" when type == VisualType.Gantt => FieldChannel.Shape,
        "DEPENDS_ON" when type == VisualType.Gantt => FieldChannel.Detail,
        "NAME" or "LABEL" or "CATEGORY" when type == VisualType.Funnel => FieldChannel.X,
        "VALUE" when type == VisualType.Funnel => FieldChannel.Y,
        "VALUE" when type == VisualType.Gauge => FieldChannel.Radius,
        "LABEL" when type == VisualType.Gauge => FieldChannel.Text,
        "MIN" when type == VisualType.Gauge => FieldChannel.Y,
        "MAX" when type == VisualType.Gauge => FieldChannel.Y2,
        "GOAL" when type == VisualType.Gauge => FieldChannel.Detail,
        "FACET" when type == VisualType.Trellis => FieldChannel.Column,
        "VALUE" when type == VisualType.HeatMap => FieldChannel.Size,
        "NAME" when type == VisualType.Waterfall => FieldChannel.X,
        "VALUE" when type == VisualType.Waterfall => FieldChannel.Y,
        "TOTAL" when type == VisualType.Waterfall => FieldChannel.Detail,
        "LOW" when type == VisualType.BoxPlot => FieldChannel.Low,
        "Q1" when type == VisualType.BoxPlot => FieldChannel.Q1,
        "MEDIAN" when type == VisualType.BoxPlot => FieldChannel.Median,
        "Q3" when type == VisualType.BoxPlot => FieldChannel.Q3,
        "HIGH" when type == VisualType.BoxPlot => FieldChannel.High,
        "OPEN" when type == VisualType.Candlestick => FieldChannel.Open,
        "HIGH" when type == VisualType.Candlestick => FieldChannel.High,
        "LOW" when type == VisualType.Candlestick => FieldChannel.Low,
        "CLOSE" when type == VisualType.Candlestick => FieldChannel.Close,
        "Y" or "VALUE" when type is not VisualType.Pie and not VisualType.Donut => FieldChannel.Y,
        "Y2" => FieldChannel.Y2,
        "LABEL" or "CATEGORY" when type is VisualType.Pie or VisualType.Donut => FieldChannel.Theta,
        "VALUE" when type is VisualType.Pie or VisualType.Donut => FieldChannel.Radius,
        "SERIES" or "COLOR" => FieldChannel.Color,
        "SIZE" => FieldChannel.Size,
        "LABEL" when type == VisualType.Bubble => FieldChannel.Text,
        "TOOLTIP" => FieldChannel.Tooltip,
        _ => null
    };

    private static DataSemanticKind InferSemanticKind(
        VisualType type,
        FieldChannel channel,
        VisualMapping mapping,
        IReadOnlyList<VisualOption> options)
    {
        if (type is VisualType.HeatMap or VisualType.Gantt && channel == FieldChannel.Y)
            return DataSemanticKind.Nominal;
        if (channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.YStart or FieldChannel.YEnd or
            FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or FieldChannel.High or
            FieldChannel.Open or FieldChannel.Close or FieldChannel.Radius or FieldChannel.Size)
            return DataSemanticKind.Quantitative;
        if ((type is VisualType.Scatter or VisualType.Bubble ||
            type == VisualType.Trellis && TrellisChartTypeFromOptions(options) == "SCATTER") && channel == FieldChannel.X)
            return DataSemanticKind.Quantitative;
        if (mapping.Format?.Contains("date", StringComparison.OrdinalIgnoreCase) == true ||
            mapping.Column.Contains("date", StringComparison.OrdinalIgnoreCase) ||
            mapping.Column.Contains("time", StringComparison.OrdinalIgnoreCase))
            return DataSemanticKind.Temporal;
        return DataSemanticKind.Nominal;
    }

    private static string? ScaleId(FieldChannel channel) => channel switch
    {
        FieldChannel.X => "x",
        FieldChannel.X2 => "x",
        FieldChannel.Y => "y",
        FieldChannel.Y2 => "y2",
        FieldChannel.YStart or FieldChannel.YEnd or FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or
            FieldChannel.Q3 or FieldChannel.High or FieldChannel.Open or FieldChannel.Close => "y",
        FieldChannel.Color => "color",
        FieldChannel.Theta => "theta",
        FieldChannel.Radius => "radius",
        FieldChannel.Size => "size",
        _ => null
    };

    private static SortDirection ParseSort(IReadOnlyList<VisualOption> options)
    {
        var value = options.FirstOrDefault(option => option.Key.Equals("AXIS_SORT", StringComparison.OrdinalIgnoreCase))?.Value.ToUpperInvariant();
        if (value is null) return SortDirection.None;
        return value switch
        {
            "DESC" or "VALUE_DESC" => SortDirection.Descending,
            "NONE" or "SOURCE" => SortDirection.None,
            _ => SortDirection.Ascending
        };
    }

    private static bool IsPolar(VisualType type) => type is VisualType.Pie or VisualType.Donut or VisualType.Gauge or VisualType.Radar;
    private static FacetSpec? BuildFacet(CreateVisualStatement statement, ImmutableArray<FieldBinding> bindings)
    {
        if (statement.VisualType != VisualType.Trellis) return null;
        var facet = bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Column);
        if (facet is null) return null;
        var shared = !statement.Options.Any(option => option.Key.Equals("SHARED_AXIS", StringComparison.OrdinalIgnoreCase) &&
            option.Value.Equals("OFF", StringComparison.OrdinalIgnoreCase));
        return new FacetSpec(null, facet.Field, new ScaleResolutionSpec(Y: shared ? ScaleResolutionMode.Shared : ScaleResolutionMode.Independent));
    }
    private static MarkKind TrellisMark(CreateVisualStatement statement) =>
        TrellisChartTypeFromOptions(statement.Options) switch
        {
            "LINE" => MarkKind.Line,
            "SCATTER" => MarkKind.Point,
            _ => MarkKind.Rect
        };
    private static string TrellisChartTypeFromOptions(IReadOnlyList<VisualOption>? options) =>
        options?.FirstOrDefault(option => option.Key.Equals("CHART_TYPE", StringComparison.OrdinalIgnoreCase))?.Value.ToUpperInvariant() ?? "BAR";
    private static ChartValue? ParseDomain(VisualManifest manifest, FieldChannel channel, string bound)
    {
        var axis = channel == FieldChannel.X ? "x" : "y";
        var text = manifest.Options.GetValueOrDefault($"axis:{axis}:{bound}");
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? ChartValue.From(value)
            : null;
    }
    private static ChartValue? ParseStandardDomain(VisualManifest manifest, string option) =>
        decimal.TryParse(manifest.Options.GetValueOrDefault(option), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? ChartValue.From(value)
            : null;
    private static ChartValue? GaugeBound(CreateVisualStatement statement, VisualManifest manifest, FieldChannel channel, string role)
    {
        if (statement.VisualType != VisualType.Gauge || channel != FieldChannel.Radius || manifest.Rows.Count == 0) return null;
        var field = statement.Mappings.FirstOrDefault(mapping => mapping.Role.Equals(role, StringComparison.OrdinalIgnoreCase))?.Column;
        var index = field is null ? -1 : manifest.Columns.FindIndex(column => column.Equals(field, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < manifest.Rows[0].Count && decimal.TryParse(manifest.Rows[0][index], NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? ChartValue.From(value)
            : null;
    }
    private static decimal ResolveInnerRadius(VisualManifest manifest)
    {
        var text = manifest.Options.GetValueOrDefault("INNER_RADIUS")?.Trim().TrimEnd('%');
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value > 1m ? value / 100m : value, 0m, 0.9m)
            : 0.45m;
    }
    private static string Sanitize(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray());
}
