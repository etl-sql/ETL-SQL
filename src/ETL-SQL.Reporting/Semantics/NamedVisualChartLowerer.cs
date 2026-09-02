using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Lowers the existing named Report-SQL visual syntax into renderer-neutral intent.</summary>
public sealed class NamedVisualChartLowerer(IExecutionContext? context = null)
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
        if (statement.VisualType == VisualType.Scatter)
        {
            var hasErrorLow = statement.Mappings.Any(m => m.Role.Equals("ERROR_LOW", StringComparison.OrdinalIgnoreCase));
            var hasErrorHigh = statement.Mappings.Any(m => m.Role.Equals("ERROR_HIGH", StringComparison.OrdinalIgnoreCase));
            if (hasErrorLow != hasErrorHigh)
                throw new InvalidOperationException("SCATTER visual requires both ERROR_LOW and ERROR_HIGH mappings as a pair.");

            var errorBarStyleOption = statement.Options.FirstOrDefault(o => o.Key.Equals("ERROR_BAR_STYLE", StringComparison.OrdinalIgnoreCase))?.Value;
            if (errorBarStyleOption is not null)
            {
                var upper = errorBarStyleOption.ToUpperInvariant();
                if (upper is not ("CAPS" or "NO_CAPS"))
                    throw new InvalidOperationException($"Invalid ERROR_BAR_STYLE '{errorBarStyleOption}'. Valid values are CAPS or NO_CAPS.");
            }
        }

        foreach (var overlay in statement.Overlays)
        {
            if (overlay.OverlayType == OverlayType.Forecast)
            {
                if (statement.VisualType is not (VisualType.Line or VisualType.Combo))
                    throw new InvalidOperationException($"FORECAST overlay is supported only on LINE and COMBO visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");

                if (string.IsNullOrWhiteSpace(overlay.ForecastField))
                    throw new InvalidOperationException("FORECAST overlay requires a forecast field name: FORECAST(field).");

                var hasConfLow = !string.IsNullOrWhiteSpace(overlay.ConfidenceLowField);
                var hasConfHigh = !string.IsNullOrWhiteSpace(overlay.ConfidenceHighField);
                if (hasConfLow != hasConfHigh)
                    throw new InvalidOperationException("FORECAST overlay requires both CONFIDENCE_LOW and CONFIDENCE_HIGH as a pair.");

                var hasX = statement.Mappings.Any(m => m.Role.Equals("X", StringComparison.OrdinalIgnoreCase));
                var hasY = statement.Mappings.Any(m => m.Role.Equals("Y", StringComparison.OrdinalIgnoreCase))
                    || statement.TypedSeries.Count > 0;
                if (!hasX)
                    throw new InvalidOperationException("FORECAST overlay requires an X mapping.");
                if (!hasY)
                    throw new InvalidOperationException("FORECAST overlay requires a primary quantitative Y mapping.");
            }
            if (overlay.OverlayType == OverlayType.ReferenceLine)
            {
                if (statement.VisualType is not (VisualType.Bar or VisualType.HorizontalBar or VisualType.Line or VisualType.Combo or VisualType.Scatter or VisualType.Bubble))
                    throw new InvalidOperationException($"REFERENCE_LINE overlay is supported only on Cartesian charts with a primary value axis (BAR, HBAR, LINE, COMBO, SCATTER, BUBBLE); found {statement.VisualType.ToString().ToUpperInvariant()}.");

                if (!overlay.Parameter.HasValue || !double.IsFinite(overlay.Parameter.Value))
                    throw new InvalidOperationException("REFERENCE_LINE requires a finite numeric VALUE.");

                var hasPrimaryValue = statement.VisualType == VisualType.HorizontalBar
                    ? statement.Mappings.Any(m => m.Role.Equals("Y", StringComparison.OrdinalIgnoreCase) || m.Role.Equals("VALUE", StringComparison.OrdinalIgnoreCase))
                    : statement.Mappings.Any(m => m.Role.Equals("Y", StringComparison.OrdinalIgnoreCase) || m.Role.Equals("VALUE", StringComparison.OrdinalIgnoreCase))
                        || statement.TypedSeries.Count > 0;

                if (!hasPrimaryValue)
                    throw new InvalidOperationException("REFERENCE_LINE overlay requires a primary quantitative value mapping.");
            }
        }

        var bindings = BuildBindings(statement, manifest).ToImmutableArray();
        var layers = BuildLayers(statement, bindings).ToImmutableArray();
        var stackMode = ResolveNamedStackMode(manifest);
        if (stackMode != StackMode.None)
            layers = layers.Select(layer => layer with
            {
                Bindings = layer.Bindings.Select(binding => binding.Channel is FieldChannel.Y or FieldChannel.Y2
                    ? binding with { Stack = stackMode }
                    : binding).ToImmutableArray()
            }).ToImmutableArray();
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
            formatting: ChartStyleTokens.Formatting(context, manifest,
                statement.Mappings.Select(mapping => new FieldFormat(mapping.Column, mapping.Format)).ToImmutableArray()),
            nullHandling: new NullHandlingSpec(
                statement.VisualType == VisualType.Line || statement.VisualType == VisualType.Combo ||
                    statement.VisualType == VisualType.Trellis && TrellisMark(statement) == MarkKind.Line
                    ? NullValuePolicy.Gap
                    : NullValuePolicy.Skip,
                []),
            theme: ChartStyleTokens.Theme(manifest),
            accessibility: new AccessibilitySpec(
                title,
                manifest.Options.GetValueOrDefault("subtitle"),
                null,
                true),
            title: title,
            interactions: ChartInteractionResolver.Lower(statement, bindings),
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
            if (channel is null)
            {
                var valid = ValidRolesFor(statement.VisualType);
                throw new InvalidOperationException(
                    $"Unknown MAPPINGS role '{mapping.Role}' for {statement.VisualType} visual. " +
                    $"Valid roles: {string.Join(", ", valid)}.");
            }
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
        var bandSize = ResolveBandSize(statement);
        if (statement.VisualType == VisualType.Combo && statement.TypedSeries.Count > 0)
        {
            var x = bindings.Where(binding => binding.Channel == FieldChannel.X).ToImmutableArray();
            var firstType = statement.TypedSeries[0].SeriesType;
            for (var index = 0; index < statement.TypedSeries.Count; index++)
            {
                var series = statement.TypedSeries[index];
                var secondary = !series.SeriesType.Equals(firstType, StringComparison.OrdinalIgnoreCase);
                var channel = secondary ? FieldChannel.Y2 : FieldChannel.Y;
                var y = bindings.First(binding => binding.Field!.Equals(series.Column, StringComparison.OrdinalIgnoreCase));
                var mark = series.SeriesType.Equals("bar", StringComparison.OrdinalIgnoreCase)
                    ? MarkKind.Rect
                    : MarkKind.Line;
                yield return new MarkLayerSpec(
                    $"series-{index:D2}-{Sanitize(series.Column)}",
                    mark,
                    index,
                    x.Add(y with { Channel = channel, Axis = secondary ? AxisRole.Secondary : AxisRole.Primary }),
                    [new StyleToken("series", series.Column)],
                    series.Column)
                {
                    BandSize = mark == MarkKind.Rect ? bandSize : .75m
                };
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
                VisualType.Scatter when bindings.Any(b => b.Channel == FieldChannel.ErrorLow) && bindings.Any(b => b.Channel == FieldChannel.ErrorHigh) =>
                    ImmutableArray.Create(new StyleToken("errorBarStyle",
                        (statement.Options.FirstOrDefault(o => o.Key.Equals("ERROR_BAR_STYLE", StringComparison.OrdinalIgnoreCase))?.Value ?? "CAPS").ToUpperInvariant())),
                VisualType.Scatter =>
                    statement.Options.FirstOrDefault(o => o.Key.Equals("ERROR_BAR_STYLE", StringComparison.OrdinalIgnoreCase)) is { } styleOpt
                        ? ImmutableArray.Create(new StyleToken("errorBarStyle", styleOpt.Value.ToUpperInvariant()))
                        : ImmutableArray<StyleToken>.Empty,
                _ => ImmutableArray<StyleToken>.Empty
            };
            var mark = statement.VisualType switch
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
            };
            yield return new MarkLayerSpec(
                "primary",
                mark,
                0,
                bindings,
                style,
                statement.Name)
            {
                BandSize = mark == MarkKind.Rect ? bandSize : .75m
            };
        }

        for (var index = 0; index < statement.Overlays.Count; index++)
        {
            var overlay = statement.Overlays[index];
            if (overlay.OverlayType == OverlayType.Forecast)
            {
                var xField = statement.Mappings.FirstOrDefault(m => m.Role.Equals("X", StringComparison.OrdinalIgnoreCase))?.Column;
                var xSemanticKind = bindings.FirstOrDefault(b => b.Channel == FieldChannel.X)?.SemanticKind ?? DataSemanticKind.Nominal;
                var overlayColor = overlay.Color ?? "#2563eb";
                var overlayLabel = overlay.Label ?? "Forecast";

                // 1. Confidence band (if supplied)
                if (!string.IsNullOrWhiteSpace(overlay.ConfidenceLowField) && !string.IsNullOrWhiteSpace(overlay.ConfidenceHighField))
                {
                    yield return new MarkLayerSpec(
                        $"forecast-confidence-{index:D2}",
                        MarkKind.Area,
                        98 + index * 10,
                        [
                            new FieldBinding(FieldChannel.X, xField, xSemanticKind),
                            new FieldBinding(FieldChannel.ConfidenceLow, overlay.ConfidenceLowField, DataSemanticKind.Quantitative, ScaleId: "y"),
                            new FieldBinding(FieldChannel.ConfidenceHigh, overlay.ConfidenceHighField, DataSemanticKind.Quantitative, ScaleId: "y")
                        ],
                        [
                            new StyleToken("overlayType", "ForecastConfidence"),
                            new StyleToken("color", overlayColor),
                            new StyleToken("label", $"{overlayLabel} Confidence")
                        ],
                        $"{overlayLabel} Confidence");
                }

                // 2. Forecast line
                yield return new MarkLayerSpec(
                    $"forecast-line-{index:D2}",
                    MarkKind.Line,
                    100 + index * 10,
                    [
                        new FieldBinding(FieldChannel.X, xField, xSemanticKind),
                        new FieldBinding(FieldChannel.Y, overlay.ForecastField, DataSemanticKind.Quantitative, ScaleId: "y")
                    ],
                    [
                        new StyleToken("overlayType", "Forecast"),
                        new StyleToken("lineStyle", overlay.LineStyle.ToString().ToLowerInvariant()),
                        new StyleToken("color", overlayColor),
                        new StyleToken("label", overlayLabel)
                    ],
                    overlayLabel);

                // 3. Anomaly markers (if supplied)
                if (!string.IsNullOrWhiteSpace(overlay.AnomalyField))
                {
                    yield return new MarkLayerSpec(
                        $"forecast-anomaly-{index:D2}",
                        MarkKind.Point,
                        102 + index * 10,
                        [
                            new FieldBinding(FieldChannel.X, xField, xSemanticKind),
                            new FieldBinding(FieldChannel.Y, overlay.AnomalyField, DataSemanticKind.Quantitative, ScaleId: "y")
                        ],
                        [
                            new StyleToken("overlayType", "ForecastAnomaly"),
                            new StyleToken("color", overlayColor),
                            new StyleToken("label", $"{overlayLabel} Anomalies")
                        ],
                        $"{overlayLabel} Anomalies");
                }

                continue;
            }

            var hasAuthoredLabel = !string.IsNullOrWhiteSpace(overlay.Label);
            var styleTokens = new List<StyleToken>
            {
                new("overlayType", overlay.OverlayType.ToString()),
                new("parameter", overlay.Parameter?.ToString(CultureInfo.InvariantCulture) ?? ""),
                new("lineStyle", overlay.LineStyle.ToString().ToLowerInvariant()),
                new("color", overlay.Color ?? "#888888")
            };

            if (hasAuthoredLabel)
            {
                styleTokens.Add(new("label", overlay.Label!));
            }
            else if (overlay.OverlayType != OverlayType.ReferenceLine)
            {
                styleTokens.Add(new("label", overlay.OverlayType.ToString()));
            }

            var legendTitle = hasAuthoredLabel
                ? overlay.Label
                : (overlay.OverlayType == OverlayType.ReferenceLine ? null : overlay.OverlayType.ToString());

            yield return new MarkLayerSpec(
                $"rule-{index:D2}-{overlay.OverlayType.ToString().ToLowerInvariant()}",
                overlay.OverlayType is OverlayType.Goal or OverlayType.Average or OverlayType.ReferenceLine ? MarkKind.Rule : MarkKind.Line,
                100 + index,
                [],
                styleTokens.ToImmutableArray(),
                legendTitle);
        }
    }

    private static IEnumerable<ScaleSpec> BuildScales(
        CreateVisualStatement statement,
        ImmutableArray<FieldBinding> bindings,
        VisualManifest manifest)
    {
        if (statement.VisualType == VisualType.Radar)
        {
            var dimensions = bindings.Where(binding => binding.Channel == FieldChannel.Detail).Select(binding => binding.Field!).ToImmutableArray();
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
                    FieldChannel.High or FieldChannel.Open or FieldChannel.Close or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or
                    FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh
                ? FieldChannel.Y
                : binding.Channel;
            var kind = binding.SemanticKind switch
            {
                DataSemanticKind.Temporal => ScaleKind.Time,
                DataSemanticKind.Quantitative => ScaleKind.Linear,
                DataSemanticKind.Ordinal => ScaleKind.Point,
                _ => binding.Channel is FieldChannel.Color ? ScaleKind.Ordinal : ScaleKind.Band
            };
            var axis = AxisName(scaleChannel);
            var includeZero = ParseAxisBool(manifest, axis, "include_zero") ??
                ((scaleChannel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or FieldChannel.Size)
                    && statement.VisualType is not VisualType.Scatter and not VisualType.Bubble);
            yield return new ScaleSpec(group.Key, scaleChannel, kind,
                IncludeZero: includeZero,
                CategoryOrder: [],
                DomainMinimum: GaugeBound(statement, manifest, binding.Channel, "MIN")
                    ?? ParseDomain(manifest, binding.Channel, "min") ?? ParseStandardDomain(manifest, "MIN"),
                DomainMaximum: GaugeBound(statement, manifest, binding.Channel, "MAX")
                    ?? ParseDomain(manifest, binding.Channel, "max") ?? ParseStandardDomain(manifest, "MAX"),
                Reverse: ParseAxisBool(manifest, axis, "reverse") ?? false,
                MajorTickCount: ParseAxisInt(manifest, axis, "major_tick_count"),
                TickInterval: ParseAxisDecimal(manifest, axis, "tick_interval"),
                MinorTicks: ParseAxisBool(manifest, axis, "minor_ticks") ?? false,
                LabelRotation: ParseAxisText(manifest, axis, "label_rotation"),
                LabelSkip: ParseAxisInt(manifest, axis, "label_skip"),
                OuterPadding: kind == ScaleKind.Band && scaleChannel == FieldChannel.X
                    ? ResolveUnitInterval(manifest.Options.GetValueOrDefault("OUTER_PADDING"), "OUTER_PADDING")
                    : 0m);
        }
    }

    private static bool IsOn(string? value) => value is not null &&
        (value.Equals("ON", StringComparison.OrdinalIgnoreCase) || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase));

    private static StackMode ResolveNamedStackMode(VisualManifest manifest)
    {
        var value = manifest.Options.GetValueOrDefault("STACKED") ?? manifest.Styles?.GetValueOrDefault("STACKED");
        return value?.ToUpperInvariant() switch
        {
            "100PCT" => StackMode.Normalize,
            "ON" or "TRUE" => StackMode.Zero,
            _ => StackMode.None
        };
    }

    private static FieldChannel? MapChannel(VisualType type, string role) => role.ToUpperInvariant() switch
    {
        "X" or "START" => FieldChannel.X,
        "ERROR_LOW" when type == VisualType.Scatter => FieldChannel.ErrorLow,
        "ERROR_HIGH" when type == VisualType.Scatter => FieldChannel.ErrorHigh,
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

    public static IReadOnlyList<string> ValidRolesFor(VisualType type) => type switch
    {
        VisualType.Bar or VisualType.HorizontalBar or VisualType.Line
            => ["X", "Y", "Y2", "SERIES", "COLOR", "SIZE", "TOOLTIP"],
        VisualType.Scatter
            => ["X", "Y", "Y2", "ERROR_LOW", "ERROR_HIGH", "SERIES", "COLOR", "SIZE", "TOOLTIP"],
        VisualType.Bubble
            => ["X", "Y", "Y2", "SERIES", "COLOR", "SIZE", "LABEL", "TOOLTIP"],
        VisualType.Pie or VisualType.Donut
            => ["LABEL", "CATEGORY", "VALUE", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Funnel
            => ["NAME", "LABEL", "CATEGORY", "VALUE", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Gauge
            => ["VALUE", "LABEL", "MIN", "MAX", "GOAL", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.HeatMap
            => ["X", "Y", "VALUE", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Waterfall
            => ["NAME", "X", "VALUE", "Y", "TOTAL", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.BoxPlot
            => ["X", "LOW", "Q1", "MEDIAN", "Q3", "HIGH", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Candlestick
            => ["X", "OPEN", "HIGH", "LOW", "CLOSE", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Gantt
            => ["X", "START", "X2", "END", "Y", "LABEL", "PROGRESS", "MILESTONE", "DEPENDS_ON", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Trellis
            => ["X", "Y", "Y2", "FACET", "SERIES", "COLOR", "SIZE", "TOOLTIP"],
        VisualType.Radar
            => ["X", "Y", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Combo
            => ["X", "Y", "Y2", "SERIES", "COLOR", "TOOLTIP"],
        _ => ["X", "Y", "Y2", "SERIES", "COLOR", "SIZE", "TOOLTIP"]
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
            FieldChannel.Open or FieldChannel.Close or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or
            FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh or FieldChannel.Radius or FieldChannel.Size)
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
            FieldChannel.Q3 or FieldChannel.High or FieldChannel.Open or FieldChannel.Close or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or
            FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh => "y",
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
        var axis = AxisName(channel);
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
    private static string AxisName(FieldChannel channel) => channel switch
    {
        FieldChannel.X or FieldChannel.X2 => "x",
        FieldChannel.Y2 => "y2",
        _ => "y"
    };
    private static string? ParseAxisText(VisualManifest manifest, string axis, string option) =>
        manifest.Options.GetValueOrDefault($"axis:{axis}:{option}");
    private static bool? ParseAxisBool(VisualManifest manifest, string axis, string option)
    {
        var value = ParseAxisText(manifest, axis, option);
        if (value is null) return null;
        return value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
    private static int? ParseAxisInt(VisualManifest manifest, string axis, string option) =>
        int.TryParse(ParseAxisText(manifest, axis, option), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : null;
    private static decimal? ParseAxisDecimal(VisualManifest manifest, string axis, string option) =>
        decimal.TryParse(ParseAxisText(manifest, axis, option), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value : null;

    private static decimal ResolveBandSize(CreateVisualStatement statement)
    {
        var text = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("BAND_SIZE", StringComparison.OrdinalIgnoreCase))?.Value;
        if (text is null) return .75m;
        if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException(
                $"BAND_SIZE must be a number greater than zero and at most one, but got '{text}'.");
        if (value is <= 0m or > 1m)
            throw new InvalidDataException($"BAND_SIZE must be greater than zero and at most one, but got '{text}'.");
        return value;
    }

    private static decimal ResolveUnitInterval(string? text, string option)
    {
        if (text is null) return 0m;
        if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) || value is < 0m or > 1m)
            throw new InvalidDataException($"{option} must be a number between zero and one, but got '{text}'.");
        return value;
    }

    private static string Sanitize(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray());
}
