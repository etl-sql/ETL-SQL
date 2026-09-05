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
        VisualType.Pie, VisualType.Donut, VisualType.Combo,
        VisualType.Map
    ];

    public static bool Supports(VisualType type) => Supported.Contains(type);

    public ChartSpec Lower(CreateVisualStatement statement, VisualManifest manifest)
    {
        if (!Supports(statement.VisualType))
            throw new NotSupportedException($"Visual type {statement.VisualType} is not in the representative GoG slice.");

        var symbolShapeOption = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("SYMBOL_SHAPE", StringComparison.OrdinalIgnoreCase))?.Value;
        if (symbolShapeOption is not null)
        {
            if (statement.VisualType is not (VisualType.Line or VisualType.Scatter))
                throw new InvalidOperationException($"SYMBOL_SHAPE is supported only on LINE and SCATTER visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            if (!PointShapeVocabulary.IsSupported(symbolShapeOption))
                throw new InvalidOperationException($"Invalid SYMBOL_SHAPE '{symbolShapeOption}'. Valid values are {PointShapeVocabulary.DisplayList}.");
        }
        ValidatePointStrokeOptions(statement);
        ValidateLineWidthOption(statement);
        ValidateLegendOptions(statement);
        ValidatePieDonutOptions(statement);
        ValidateScatterBubbleOptions(statement);
        ValidateHeatmapOptions(statement);
        ValidateMapOptions(statement);
        ValidateWaterfallOptions(statement);
        ValidateGanttOptions(statement);
        ValidateCandlestickOptions(statement);
        ValidateRadarOptions(statement);
        ValidateFunnelOptions(statement);
        ValidateBoxPlotOptions(statement);
        ValidateComboOptions(statement);
        ValidateSymbolSizeOption(statement);
        ValidateTrellisOptions(statement);
        foreach (var opt in statement.Options) manifest.Options.TryAdd(opt.Key, opt.Value);
        if (statement.VisualType == VisualType.Funnel)
        {
            var shape = statement.Options.FirstOrDefault(o => o.Key.Equals("FUNNEL_SHAPE", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("SHAPE", StringComparison.OrdinalIgnoreCase))?.Value;
            var defaultSort = shape?.Equals("PYRAMID", StringComparison.OrdinalIgnoreCase) == true ? "VALUE_ASC" : "VALUE_DESC";
            if (!statement.Options.Any(o => o.Key.Equals("SORT", StringComparison.OrdinalIgnoreCase)) &&
                !manifest.Options.ContainsKey("SORT"))
            {
                manifest.Options["SORT"] = defaultSort;
            }
        }
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
                if (upper is not ("CAPS" or "CAP" or "NO_CAPS" or "NO_CAP"))
                    throw new InvalidOperationException($"Invalid ERROR_BAR_STYLE '{errorBarStyleOption}'. Valid values are CAPS or NO_CAPS.");
            }
        }

        var seriesLabelsOption = statement.Options.FirstOrDefault(o => o.Key.Equals("SERIES_LABELS", StringComparison.OrdinalIgnoreCase))?.Value;
        var hasSeriesLabelsPrefix = statement.Options.Any(o => o.Key.StartsWith("SERIES_LABELS:", StringComparison.OrdinalIgnoreCase));
        if (seriesLabelsOption is not null || hasSeriesLabelsPrefix)
        {
            if (seriesLabelsOption is null)
                throw new InvalidOperationException("SERIES_LABELS nested options require the SERIES_LABELS toggle.");

            if (statement.VisualType is not (VisualType.Line or VisualType.Combo))
                throw new InvalidOperationException($"SERIES_LABELS is supported only on LINE and COMBO visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");

            if (seriesLabelsOption is not null && seriesLabelsOption.ToUpperInvariant() is not ("ON" or "OFF"))
                throw new InvalidOperationException($"Invalid SERIES_LABELS value '{seriesLabelsOption}'. Valid values are ON or OFF.");

            var positionOption = statement.Options.FirstOrDefault(o => o.Key.Equals("SERIES_LABELS:POSITION", StringComparison.OrdinalIgnoreCase))?.Value;
            if (positionOption is not null)
            {
                var upperPos = positionOption.ToUpperInvariant();
                if (upperPos is not ("START" or "END"))
                    throw new InvalidOperationException($"Invalid SERIES_LABELS POSITION '{positionOption}'. Valid values are START or END.");
            }
        }

        if (statement.SegmentStyles.Count > 0)
        {
            if (statement.VisualType is not (VisualType.Line or VisualType.Combo))
                throw new InvalidOperationException($"SEGMENT_STYLE is supported only on LINE and COMBO visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");

            foreach (var rule in statement.SegmentStyles)
            {
                if (string.IsNullOrWhiteSpace(rule.LineDash) && string.IsNullOrWhiteSpace(rule.Color))
                    throw new InvalidOperationException("SEGMENT_STYLE requires at least LINE_DASH or COLOR.");
                if (!string.IsNullOrWhiteSpace(rule.LineDash) && rule.LineDash.ToUpperInvariant() is not ("SOLID" or "DASHED" or "DOTTED"))
                    throw new InvalidOperationException($"Invalid LINE_DASH value '{rule.LineDash}'. Valid values are SOLID, DASHED, or DOTTED.");
            }
        }

        var leaderLineOption = statement.Options.FirstOrDefault(o => o.Key.Equals("DATA_LABELS:LEADER_LINE", StringComparison.OrdinalIgnoreCase))?.Value;
        var hasLeaderLinePrefix = statement.Options.Any(o => o.Key.StartsWith("DATA_LABELS:LEADER_LINE:", StringComparison.OrdinalIgnoreCase));
        if (leaderLineOption is not null || hasLeaderLinePrefix)
        {
            if (leaderLineOption is null)
                throw new InvalidOperationException("LEADER_LINE nested options require the LEADER_LINE toggle.");

            if (!statement.Options.Any(o => o.Key.Equals("DATA_LABELS", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("LEADER_LINE requires the DATA_LABELS toggle.");

            if (statement.VisualType is not (VisualType.Pie or VisualType.Donut or VisualType.Scatter))
                throw new InvalidOperationException($"LEADER_LINE is supported only on PIE, DONUT, and SCATTER visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");

            if (leaderLineOption is not null && leaderLineOption.ToUpperInvariant() is not ("ON" or "OFF"))
                throw new InvalidOperationException($"Invalid LEADER_LINE value '{leaderLineOption}'. Valid values are ON or OFF.");

            var styleOption = statement.Options.FirstOrDefault(o => o.Key.Equals("DATA_LABELS:LEADER_LINE:STYLE", StringComparison.OrdinalIgnoreCase))?.Value;
            if (styleOption is not null)
            {
                var upperStyle = styleOption.ToUpperInvariant();
                if (upperStyle is not ("SOLID" or "DASHED"))
                    throw new InvalidOperationException($"Invalid LEADER_LINE STYLE '{styleOption}'. Valid values are SOLID or DASHED.");
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
                if (statement.VisualType is not (VisualType.Bar or VisualType.HorizontalBar or VisualType.Line or VisualType.Combo or VisualType.Scatter or VisualType.Bubble or VisualType.Candlestick))
                    throw new InvalidOperationException($"REFERENCE_LINE overlay is supported only on Cartesian charts with a primary value axis (BAR, HBAR, LINE, COMBO, SCATTER, BUBBLE, CANDLESTICK); found {statement.VisualType.ToString().ToUpperInvariant()}.");

                if (!overlay.Parameter.HasValue || !double.IsFinite(overlay.Parameter.Value))
                    throw new InvalidOperationException("REFERENCE_LINE requires a finite numeric VALUE.");

                if (!HasPrimaryValueMapping(statement))
                    throw new InvalidOperationException("REFERENCE_LINE overlay requires a primary quantitative value mapping.");
            }
            if (overlay.OverlayType == OverlayType.ReferenceBand)
            {
                if (statement.VisualType is not (VisualType.Bar or VisualType.HorizontalBar or VisualType.Line or VisualType.Combo or VisualType.Scatter or VisualType.Bubble or VisualType.Candlestick))
                    throw new InvalidOperationException($"REFERENCE_BAND overlay is supported only on Cartesian charts with a primary value axis (BAR, HBAR, LINE, COMBO, SCATTER, BUBBLE, CANDLESTICK); found {statement.VisualType.ToString().ToUpperInvariant()}.");
                if (!overlay.BandLow.HasValue || !overlay.BandHigh.HasValue ||
                    !double.IsFinite(overlay.BandLow.Value) || !double.IsFinite(overlay.BandHigh.Value))
                    throw new InvalidOperationException("REFERENCE_BAND requires finite numeric LOW and HIGH values.");
                if (overlay.BandLow.Value >= overlay.BandHigh.Value)
                    throw new InvalidOperationException("REFERENCE_BAND requires LOW to be less than HIGH.");
                if (!HasPrimaryValueMapping(statement))
                    throw new InvalidOperationException("REFERENCE_BAND overlay requires a primary quantitative value mapping.");
            }
            if (overlay.OverlayType is OverlayType.RunningTotal or OverlayType.PercentOfTotal)
            {
                if (statement.VisualType is not (VisualType.Line or VisualType.Bar or VisualType.HorizontalBar))
                    throw new InvalidOperationException($"{OverlayName(overlay.OverlayType)} overlay is supported only on LINE, BAR, and HORIZONTALBAR visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
                if (!HasPrimaryValueMapping(statement))
                    throw new InvalidOperationException($"{OverlayName(overlay.OverlayType)} overlay requires a primary quantitative Y mapping.");
                if (string.IsNullOrWhiteSpace(overlay.TableCalculationField))
                    throw new InvalidOperationException($"{OverlayName(overlay.OverlayType)} overlay requires a pre-computed field name.");
                if (!manifest.Columns.Any(column => column.Equals(overlay.TableCalculationField, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"{OverlayName(overlay.OverlayType)} overlay field '{overlay.TableCalculationField}' was not found in the visual source.");
            }
        }

        var bindings = BuildBindings(statement, manifest).ToImmutableArray();
        var layers = BuildLayers(statement, bindings, manifest).ToImmutableArray();
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
            coordinate: statement.VisualType == VisualType.Map
                ? new CoordinateSpec(CoordinateKind.Geographic)
                {
                    Geography = new GeographicCoordinateSpec(
                        (statement.Options.FirstOrDefault(o => o.Key.Equals("PROJECTION", StringComparison.OrdinalIgnoreCase))?.Value?.ToUpperInvariant() == "MERCATOR"
                            ? GeographicProjectionKind.Mercator
                            : GeographicProjectionKind.Equirectangular),
                        statement.Options.FirstOrDefault(o => o.Key.Equals("MAP_FILE", StringComparison.OrdinalIgnoreCase)) is { } mf && !string.IsNullOrWhiteSpace(mf.Value)
                            ? GeographicMapSourceKind.File
                            : GeographicMapSourceKind.BuiltIn,
                        statement.Options.FirstOrDefault(o => o.Key.Equals("MAP_FILE", StringComparison.OrdinalIgnoreCase))?.Value
                            ?? statement.Options.FirstOrDefault(o => o.Key.Equals("MAP_NAME", StringComparison.OrdinalIgnoreCase))?.Value
                            ?? "WORLD",
                        statement.Options.FirstOrDefault(o => o.Key.Equals("FEATURE_KEY", StringComparison.OrdinalIgnoreCase))?.Value
                            ?? "name")
                }
                : new CoordinateSpec(IsPolar(statement.VisualType)
                    ? CoordinateKind.Polar
                    : statement.VisualType is VisualType.HorizontalBar or VisualType.Gantt ||
                      (statement.VisualType is VisualType.Waterfall or VisualType.BoxPlot or VisualType.Funnel && IsHorizontal(statement, manifest))
                        ? CoordinateKind.TransposedCartesian
                        : CoordinateKind.Cartesian,
                    StartAngle: ResolveStartAngle(statement, manifest),
                    InnerRadius: statement.VisualType == VisualType.Donut ? ResolveInnerRadius(statement, manifest) : null),
            scales: scales,
            formatting: ChartStyleTokens.Formatting(context, manifest,
                statement.Mappings.Select(mapping => new FieldFormat(mapping.Column, mapping.Format)).ToImmutableArray()),
            nullHandling: new NullHandlingSpec(
                ResolveNullPolicy(statement),
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
            if (statement.VisualType == VisualType.Map && mapping.Role.Equals("VALUE", StringComparison.OrdinalIgnoreCase) &&
                statement.Options.Any(o => o.Key.Equals("MODE", StringComparison.OrdinalIgnoreCase) && o.Value.Equals("POINTS", StringComparison.OrdinalIgnoreCase)))
            {
                channel = FieldChannel.Size;
            }
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
        ImmutableArray<FieldBinding> bindings,
        VisualManifest manifest)
    {
        var bandSize = ResolveBandSize(statement);
        var symbolShapeOption = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("SYMBOL_SHAPE", StringComparison.OrdinalIgnoreCase))?.Value;
        var symbolStrokeColor = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("SYMBOL_STROKE_COLOR", StringComparison.OrdinalIgnoreCase))?.Value;
        var symbolStrokeWidth = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("SYMBOL_STROKE_WIDTH", StringComparison.OrdinalIgnoreCase))?.Value;
        var lineWidth = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("LINE_WIDTH", StringComparison.OrdinalIgnoreCase))?.Value;
        var areaBaseline = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("AREA_BASELINE", StringComparison.OrdinalIgnoreCase))?.Value;
        var hoverFocus = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("HOVER_FOCUS", StringComparison.OrdinalIgnoreCase))?.Value;
        var barMinHeight = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("BAR_MIN_HEIGHT", StringComparison.OrdinalIgnoreCase))?.Value;
        var anim = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("ANIMATION", StringComparison.OrdinalIgnoreCase))?.Value;
        var animDur = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("ANIMATION_DURATION", StringComparison.OrdinalIgnoreCase))?.Value;
        var animEasing = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("ANIMATION_EASING", StringComparison.OrdinalIgnoreCase))?.Value;
        var updateAnim = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("UPDATE_ANIMATION", StringComparison.OrdinalIgnoreCase))?.Value;
        var symbolSize = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("SYMBOL_SIZE", StringComparison.OrdinalIgnoreCase))?.Value;
        var syncAxes = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("SYNC_AXES", StringComparison.OrdinalIgnoreCase))?.Value;
        var yMarkOpt = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("Y_MARK", StringComparison.OrdinalIgnoreCase))?.Value?.ToUpperInvariant();
        var y2MarkOpt = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("Y2_MARK", StringComparison.OrdinalIgnoreCase))?.Value?.ToUpperInvariant();

        ImmutableArray<StyleToken> AppendCommonStyles(ImmutableArray<StyleToken> st)
        {
            if (areaBaseline is not null) st = st.Add(new StyleToken("areaBaseline", areaBaseline));
            if (hoverFocus is not null) st = st.Add(new StyleToken("hoverFocus", hoverFocus.ToUpperInvariant()));
            if (barMinHeight is not null) st = st.Add(new StyleToken("barMinHeight", barMinHeight));
            if (anim is not null) st = st.Add(new StyleToken("animation", anim.ToUpperInvariant()));
            if (animDur is not null) st = st.Add(new StyleToken("animationDuration", animDur));
            if (animEasing is not null) st = st.Add(new StyleToken("animationEasing", animEasing.ToUpperInvariant()));
            if (updateAnim is not null) st = st.Add(new StyleToken("updateAnimation", updateAnim.ToUpperInvariant()));
            if (symbolSize is not null) st = st.Add(new StyleToken("SYMBOL_SIZE", symbolSize));
            if (syncAxes is not null && IsOn(syncAxes)) st = st.Add(new StyleToken("SYNC_AXES", "ON"));
            return st;
        }
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
                var markOverride = secondary ? y2MarkOpt : yMarkOpt;
                var mark = markOverride switch
                {
                    "BAR" => MarkKind.Rect,
                    "LINE" => MarkKind.Line,
                    "AREA" => MarkKind.Area,
                    _ => series.SeriesType.Equals("bar", StringComparison.OrdinalIgnoreCase)
                        ? MarkKind.Rect
                        : series.SeriesType.Equals("area", StringComparison.OrdinalIgnoreCase)
                            ? MarkKind.Area
                            : MarkKind.Line
                };
                var style = ImmutableArray.Create(new StyleToken("series", series.Column));
                if (mark == MarkKind.Line && lineWidth is not null && LineSeriesWidth.TryNormalize(lineWidth, out var normalizedLineWidth))
                    style = style.Add(new StyleToken("LINE_WIDTH", normalizedLineWidth));
                style = AppendCommonStyles(style);
                yield return new MarkLayerSpec(
                    $"series-{index:D2}-{Sanitize(series.Column)}",
                    mark,
                    index,
                    x.Add(y with { Channel = channel, Axis = secondary ? AxisRole.Secondary : AxisRole.Primary }),
                    style,
                    series.Column)
                {
                    BandSize = mark == MarkKind.Rect ? bandSize : .75m
                };
            }
        }
        else if (statement.VisualType == VisualType.Combo && bindings.Any(b => b.Channel == FieldChannel.Y2))
        {
            var x = bindings.Where(binding => binding.Channel == FieldChannel.X).ToImmutableArray();
            var yBinding = bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Y);
            var y2Binding = bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.Y2);

            if (yBinding is not null)
            {
                var mark = yMarkOpt switch { "LINE" => MarkKind.Line, "AREA" => MarkKind.Area, _ => MarkKind.Rect };
                var style = ImmutableArray.Create(new StyleToken("series", yBinding.Field ?? "Primary"));
                if (mark == MarkKind.Line && lineWidth is not null && LineSeriesWidth.TryNormalize(lineWidth, out var normalizedLineWidth))
                    style = style.Add(new StyleToken("LINE_WIDTH", normalizedLineWidth));
                style = AppendCommonStyles(style);
                yield return new MarkLayerSpec(
                    "combo-primary",
                    mark,
                    0,
                    x.Add(yBinding with { Axis = AxisRole.Primary }),
                    style,
                    yBinding.Field)
                {
                    BandSize = mark == MarkKind.Rect ? bandSize : .75m
                };
            }

            if (y2Binding is not null)
            {
                var mark = y2MarkOpt switch { "BAR" => MarkKind.Rect, "AREA" => MarkKind.Area, _ => MarkKind.Line };
                var style = ImmutableArray.Create(new StyleToken("series", y2Binding.Field ?? "Secondary"));
                if (mark == MarkKind.Line && lineWidth is not null && LineSeriesWidth.TryNormalize(lineWidth, out var normalizedLineWidth))
                    style = style.Add(new StyleToken("LINE_WIDTH", normalizedLineWidth));
                style = AppendCommonStyles(style);
                yield return new MarkLayerSpec(
                    "combo-secondary",
                    mark,
                    1,
                    x.Add(y2Binding with { Axis = AxisRole.Secondary }),
                    style,
                    y2Binding.Field)
                {
                    BandSize = mark == MarkKind.Rect ? bandSize : .75m
                };
            }
        }
        else
        {
            var style = statement.VisualType switch
            {
                VisualType.HeatMap => ResolveHeatMapLayerStyle(statement, manifest),
                VisualType.Funnel => ResolveFunnelLayerStyle(statement, manifest),
                VisualType.Gauge => ImmutableArray.Create(new StyleToken("layout", "gauge")),
                VisualType.BoxPlot => ResolveBoxPlotLayerStyle(statement, manifest),
                VisualType.Waterfall => ResolveWaterfallLayerStyle(statement, manifest),
                VisualType.Candlestick => ResolveCandlestickLayerStyle(statement, manifest),
                VisualType.Gantt => ResolveGanttLayerStyle(statement, manifest),
                VisualType.Radar => ResolveRadarLayerStyle(statement, manifest),
                VisualType.Scatter when bindings.Any(b => b.Channel == FieldChannel.ErrorLow) && bindings.Any(b => b.Channel == FieldChannel.ErrorHigh) =>
                    ImmutableArray.Create(new StyleToken("errorBarStyle",
                        NormalizeErrorBarStyle(statement.Options.FirstOrDefault(o => o.Key.Equals("ERROR_BAR_STYLE", StringComparison.OrdinalIgnoreCase))?.Value))),
                VisualType.Scatter =>
                    statement.Options.FirstOrDefault(o => o.Key.Equals("ERROR_BAR_STYLE", StringComparison.OrdinalIgnoreCase)) is { } styleOpt
                        ? ImmutableArray.Create(new StyleToken("errorBarStyle", NormalizeErrorBarStyle(styleOpt.Value)))
                        : ImmutableArray<StyleToken>.Empty,
                VisualType.Map => ResolveMapLayerStyle(statement, manifest),
                _ => ImmutableArray<StyleToken>.Empty
            };
            if (symbolShapeOption is not null)
                style = style.Add(new StyleToken("symbolShape", PointShapeVocabulary.NormalizeOrDefault(symbolShapeOption)));
            if (symbolStrokeColor is not null)
                style = style.Add(new StyleToken("SYMBOL_STROKE_COLOR", symbolStrokeColor));
            if (symbolStrokeWidth is not null && PointMarkerStroke.TryNormalizeWidth(symbolStrokeWidth, out var normalizedWidth))
                style = style.Add(new StyleToken("SYMBOL_STROKE_WIDTH", normalizedWidth));
            if (lineWidth is not null && LineSeriesWidth.TryNormalize(lineWidth, out var normalizedLineWidth))
                style = style.Add(new StyleToken("LINE_WIDTH", normalizedLineWidth));
            style = AppendCommonStyles(style);
            var mark = statement.VisualType switch
            {
                VisualType.Bar or VisualType.HorizontalBar => MarkKind.Rect,
                VisualType.Line => IsAreaLine(statement) ? MarkKind.Area : MarkKind.Line,
                VisualType.Scatter or VisualType.Bubble => MarkKind.Point,
                VisualType.HeatMap or VisualType.Funnel => MarkKind.Rect,
                VisualType.BoxPlot or VisualType.Waterfall or VisualType.Candlestick => MarkKind.Rect,
                VisualType.Trellis => TrellisMark(statement),
                VisualType.Gantt => MarkKind.Rect,
                VisualType.Radar => MarkKind.Line,
                VisualType.Gauge => MarkKind.Arc,
                VisualType.Pie or VisualType.Donut => MarkKind.Arc,
                VisualType.Combo => MarkKind.Line,
                VisualType.Map => (statement.Options.FirstOrDefault(o => o.Key.Equals("MODE", StringComparison.OrdinalIgnoreCase))?.Value?.ToUpperInvariant() == "POINTS") ? MarkKind.Point : MarkKind.Rect,
                _ => throw new InvalidOperationException()
            };
            var position = ResolvePositionAdjustment(statement, manifest, bindings);
            if (ResolveBubbleMinMax(statement, manifest, out var bMin, out var bMax))
            {
                style = style.Add(new StyleToken("MIN_BUBBLE_SIZE", bMin.ToString(CultureInfo.InvariantCulture)));
                style = style.Add(new StyleToken("MAX_BUBBLE_SIZE", bMax.ToString(CultureInfo.InvariantCulture)));
            }

            if (statement.VisualType == VisualType.Candlestick)
            {
                var volumeBinding = bindings.FirstOrDefault(b => b.Channel == FieldChannel.Y2);
                var candleBindings = bindings.Where(b => b.Channel != FieldChannel.Y2).ToImmutableArray();
                var xBinding = bindings.FirstOrDefault(b => b.Channel == FieldChannel.X);

                if (volumeBinding is not null && xBinding is not null)
                {
                    var volColor = statement.Options.FirstOrDefault(o => o.Key.Equals("VOLUME_COLOR", StringComparison.OrdinalIgnoreCase))?.Value
                        ?? manifest.Options.GetValueOrDefault("VOLUME_COLOR")
                        ?? "#94a3b8";

                    yield return new MarkLayerSpec(
                        "volume",
                        MarkKind.Rect,
                        0,
                        [xBinding, volumeBinding],
                        [new StyleToken("series", "Volume"), new StyleToken("color", volColor), new StyleToken("volumeLayer", "true")],
                        "Volume")
                    {
                        BandSize = bandSize * 0.55m
                    };
                }

                yield return new MarkLayerSpec(
                    "primary",
                    mark,
                    1,
                    candleBindings,
                    style,
                    statement.Name)
                {
                    BandSize = bandSize,
                    Position = position
                };
            }
            else
            {
                yield return new MarkLayerSpec(
                    "primary",
                    mark,
                    0,
                    bindings,
                    style,
                    statement.Name)
                {
                    BandSize = mark == MarkKind.Rect ? bandSize : .75m,
                    Position = position
                };
            }
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

            if (overlay.OverlayType == OverlayType.ReferenceBand)
            {
                var bandTokens = new List<StyleToken>
                {
                    new("overlayType", "ReferenceBand"),
                    new("low", overlay.BandLow!.Value.ToString(CultureInfo.InvariantCulture)),
                    new("high", overlay.BandHigh!.Value.ToString(CultureInfo.InvariantCulture)),
                    new("color", overlay.Color ?? "#94a3b8")
                };
                if (!string.IsNullOrWhiteSpace(overlay.Label)) bandTokens.Add(new("label", overlay.Label!));
                yield return new MarkLayerSpec(
                    $"band-{index:D2}-referenceband",
                    MarkKind.Rect,
                    90 + index,
                    [],
                    bandTokens.ToImmutableArray(),
                    string.IsNullOrWhiteSpace(overlay.Label) ? null : overlay.Label);
                continue;
            }

            if (overlay.OverlayType is OverlayType.RunningTotal or OverlayType.PercentOfTotal)
            {
                var xBinding = bindings.FirstOrDefault(binding => binding.Channel == FieldChannel.X);
                var overlayName = OverlayName(overlay.OverlayType);
                var overlayLabel = overlay.Label ?? overlayName.Replace('_', ' ');
                var tableCalculationBindings = new List<FieldBinding>();
                if (xBinding is not null) tableCalculationBindings.Add(xBinding);
                tableCalculationBindings.Add(new FieldBinding(FieldChannel.Y, overlay.TableCalculationField,
                    DataSemanticKind.Quantitative, ScaleId: "y"));
                yield return new MarkLayerSpec(
                    $"table-calc-{index:D2}-{overlayName.ToLowerInvariant().Replace('_', '-')}",
                    MarkKind.Line,
                    100 + index,
                    tableCalculationBindings.ToImmutableArray(),
                    [
                        new StyleToken("overlayType", overlay.OverlayType.ToString()),
                        new StyleToken("lineStyle", overlay.LineStyle.ToString().ToLowerInvariant()),
                        new StyleToken("color", overlay.Color ?? "#888888"),
                        new StyleToken("label", overlayLabel)
                    ],
                    overlayLabel);
                continue;
            }

            if (overlay.OverlayType == OverlayType.AnnotationPoint)
            {
                var annotTokens = new List<StyleToken>
                {
                    new("overlayType", "AnnotationPoint"),
                    new("series", overlay.SeriesName ?? ""),
                    new("annotationType", overlay.AnnotationPointType ?? "MAX"),
                    new("coordX", overlay.CoordX?.ToString(CultureInfo.InvariantCulture) ?? ""),
                    new("coordY", overlay.CoordY?.ToString(CultureInfo.InvariantCulture) ?? ""),
                    new("coordXString", overlay.CoordXString ?? ""),
                    new("symbol", overlay.Symbol ?? "pin"),
                    new("color", overlay.Color ?? "#2563eb"),
                    new("label", overlay.Label ?? "")
                };
                yield return new MarkLayerSpec(
                    $"annotation-point-{index:D2}",
                    MarkKind.Point,
                    150 + index,
                    [],
                    annotTokens.ToImmutableArray(),
                    overlay.Label);
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
                    FieldChannel.High or FieldChannel.Mean or FieldChannel.Open or FieldChannel.Close or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or
                    FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh
                ? FieldChannel.Y
                : binding.Channel;
            var axis = AxisName(scaleChannel);
            var axisScale = ParseAxisText(statement, manifest, axis, "scale")?.ToUpperInvariant();
            if (axisScale is not null && axisScale is not ("LINEAR" or "LOG" or "LOGARITHMIC"))
            {
                throw new InvalidOperationException($"Invalid axis SCALE '{axisScale}'. Valid values are LINEAR, LOG, and LOGARITHMIC.");
            }
            var isLog = axisScale is "LOG" or "LOGARITHMIC";
            var explicitIncludeZero = ParseAxisBool(statement, manifest, axis, "include_zero");
            if (isLog && explicitIncludeZero == true)
            {
                throw new InvalidOperationException($"Logarithmic scale for axis '{axis.ToUpperInvariant()}' cannot use INCLUDE_ZERO = ON.");
            }
            var kind = binding.SemanticKind switch
            {
                DataSemanticKind.Temporal => ScaleKind.Time,
                DataSemanticKind.Quantitative when isLog => ScaleKind.Logarithmic,
                DataSemanticKind.Quantitative => ScaleKind.Linear,
                DataSemanticKind.Ordinal => ScaleKind.Point,
                _ => binding.Channel is FieldChannel.Color ? ScaleKind.Ordinal : ScaleKind.Band
            };
            var includeZero = explicitIncludeZero ??
                (!isLog && (scaleChannel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.Radius or FieldChannel.Size)
                    && statement.VisualType is not VisualType.Scatter and not VisualType.Bubble);
            var isMapPoints = statement.VisualType == VisualType.Map &&
                statement.Options.Any(o => o.Key.Equals("MODE", StringComparison.OrdinalIgnoreCase) && o.Value.Equals("POINTS", StringComparison.OrdinalIgnoreCase));
            var colorRange = statement.VisualType == VisualType.HeatMap && scaleChannel == FieldChannel.Size
                ? ResolveHeatMapColorRange(statement, manifest)
                : statement.VisualType == VisualType.Map && scaleChannel == FieldChannel.Color && !isMapPoints && binding.SemanticKind == DataSemanticKind.Quantitative
                    ? ResolveMapColorRange(statement, manifest)
                    : null;
            yield return new ScaleSpec(group.Key, scaleChannel, kind,
                IncludeZero: includeZero,
                CategoryOrder: [],
                DomainMinimum: GaugeBound(statement, manifest, binding.Channel, "MIN")
                    ?? ParseDomain(manifest, binding.Channel, "min") ?? ParseStandardDomain(manifest, "MIN"),
                DomainMaximum: GaugeBound(statement, manifest, binding.Channel, "MAX")
                    ?? ParseDomain(manifest, binding.Channel, "max") ?? ParseStandardDomain(manifest, "MAX"),
                Reverse: ParseAxisBool(statement, manifest, axis, "reverse") ?? false,
                MajorTickCount: ParseAxisInt(statement, manifest, axis, "major_tick_count"),
                TickInterval: ParseAxisDecimal(statement, manifest, axis, "tick_interval"),
                MinorTicks: ParseAxisBool(statement, manifest, axis, "minor_ticks") ?? false,
                LabelRotation: ParseAxisText(statement, manifest, axis, "label_rotation"),
                LabelSkip: ParseAxisInt(statement, manifest, axis, "label_skip"),
                OuterPadding: kind == ScaleKind.Band && scaleChannel == FieldChannel.X
                    ? ResolveUnitInterval(manifest.Options.GetValueOrDefault("OUTER_PADDING"), "OUTER_PADDING")
                    : 0m,
                TickFormat: ParseAxisText(statement, manifest, axis, "tick_format"),
                TimeUnit: ParseAxisText(statement, manifest, axis, "time_unit"))
            {
                ColorRange = colorRange
            };
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

    private static void ValidatePointStrokeOptions(CreateVisualStatement statement)
    {
        var color = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("SYMBOL_STROKE_COLOR", StringComparison.OrdinalIgnoreCase))?.Value;
        var width = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("SYMBOL_STROKE_WIDTH", StringComparison.OrdinalIgnoreCase))?.Value;
        if (color is null && width is null) return;

        if (statement.VisualType is not (VisualType.Line or VisualType.Scatter))
            throw new InvalidOperationException($"SYMBOL_STROKE_COLOR and SYMBOL_STROKE_WIDTH are supported only on LINE and SCATTER visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
        if (color is not null && !PointMarkerStroke.IsPortableColor(color))
            throw new InvalidOperationException($"Invalid SYMBOL_STROKE_COLOR '{color}'. Use a portable #RRGGBB color.");
        if (width is not null && !PointMarkerStroke.TryNormalizeWidth(width, out _))
            throw new InvalidOperationException($"Invalid SYMBOL_STROKE_WIDTH '{width}'. Use a non-negative number.");
    }

    private static void ValidateLineWidthOption(CreateVisualStatement statement)
    {
        var width = statement.Options.FirstOrDefault(option =>
            option.Key.Equals("LINE_WIDTH", StringComparison.OrdinalIgnoreCase))?.Value;
        if (width is null) return;

        if (statement.VisualType is not (VisualType.Line or VisualType.Combo))
            throw new InvalidOperationException($"LINE_WIDTH is supported only on LINE and COMBO visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
        if (!LineSeriesWidth.TryNormalize(width, out _))
            throw new InvalidOperationException($"Invalid LINE_WIDTH '{width}'. Use a pixel width from {LineSeriesWidth.Minimum} through {LineSeriesWidth.Maximum}.");
    }

    private static void ValidatePieDonutOptions(CreateVisualStatement statement)
    {
        var pieOptions = statement.Options.Where(option =>
            option.Key.Equals("SORT", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("MIN_SLICE_PCT", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("OTHER_LABEL", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("EXPLODE", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("EXPLODE_ALL", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("EXPLODE_DISTANCE", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("SLICE_BORDER_COLOR", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("SLICE_BORDER_WIDTH", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("START_ANGLE", StringComparison.OrdinalIgnoreCase)).ToList();

        if (pieOptions.Count == 0) return;

        var isPieOrDonut = statement.VisualType is VisualType.Pie or VisualType.Donut;

        foreach (var opt in pieOptions)
        {
            var key = opt.Key.ToUpperInvariant();
            var val = opt.Value;

            if (!isPieOrDonut && key is not "SORT")
                throw new InvalidOperationException($"{key} is supported only on PIE and DONUT visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");

            switch (key)
            {
                case "SORT":
                    if (isPieOrDonut)
                    {
                        var sortUpper = val.ToUpperInvariant();
                        if (sortUpper is not ("SOURCE" or "VALUE_DESC" or "VALUE_ASC" or "ALPHA" or "VALUE" or "ASC" or "DESC"))
                            throw new InvalidOperationException($"Invalid SORT '{val}'. Valid values are SOURCE, VALUE_DESC, VALUE_ASC, or ALPHA.");
                    }
                    break;

                case "MIN_SLICE_PCT":
                    var trimmedPct = val.Trim().TrimEnd('%');
                    if (!decimal.TryParse(trimmedPct, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct) || pct <= 0m)
                        throw new InvalidOperationException($"Invalid MIN_SLICE_PCT '{val}'. Must be a positive number.");
                    if (pct > 100m)
                        throw new InvalidOperationException($"Invalid MIN_SLICE_PCT '{val}'. Must be at most 100.");
                    break;

                case "OTHER_LABEL":
                    if (string.IsNullOrWhiteSpace(val))
                        throw new InvalidOperationException("OTHER_LABEL cannot be empty.");
                    break;

                case "EXPLODE":
                    if (string.IsNullOrWhiteSpace(val))
                        throw new InvalidOperationException("EXPLODE slice name cannot be empty.");
                    break;

                case "EXPLODE_ALL":
                    var explodeAllUpper = val.ToUpperInvariant();
                    if (explodeAllUpper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                    {
                        if (!decimal.TryParse(val, NumberStyles.Number, CultureInfo.InvariantCulture, out var dist) || dist < 0m)
                            throw new InvalidOperationException($"Invalid EXPLODE_ALL '{val}'. Must be ON/OFF or a non-negative number.");
                    }
                    break;

                case "EXPLODE_DISTANCE":
                    if (!decimal.TryParse(val, NumberStyles.Number, CultureInfo.InvariantCulture, out var ed) || ed < 0m)
                        throw new InvalidOperationException($"Invalid EXPLODE_DISTANCE '{val}'. Must be a non-negative number.");
                    break;

                case "SLICE_BORDER_WIDTH":
                    var trimmedBw = val.Trim().TrimEnd('p', 'x', 'P', 'X');
                    if (!decimal.TryParse(trimmedBw, NumberStyles.Number, CultureInfo.InvariantCulture, out var bw) || bw < 0m)
                        throw new InvalidOperationException($"Invalid SLICE_BORDER_WIDTH '{val}'. Must be a non-negative number.");
                    break;

                case "SLICE_BORDER_COLOR":
                    if (string.IsNullOrWhiteSpace(val))
                        throw new InvalidOperationException("SLICE_BORDER_COLOR cannot be empty.");
                    break;

                case "START_ANGLE":
                    var trimmedAngle = val.Trim().TrimEnd('°', 'd', 'e', 'g');
                    if (!decimal.TryParse(trimmedAngle, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                        throw new InvalidOperationException($"Invalid START_ANGLE '{val}'. Must be a number.");
                    break;
            }
        }
    }

    private static void ValidateLegendOptions(CreateVisualStatement statement)
    {
        var legendOptions = statement.Options.Where(option =>
            option.Key.Equals("LEGEND", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_POSITION", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_ANCHOR", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_ORIENTATION", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_REVERSE", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_COLUMNS", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_TITLE", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_FONT_SIZE", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_FONT_COLOR", StringComparison.OrdinalIgnoreCase) ||
            option.Key.Equals("LEGEND_FONT_WEIGHT", StringComparison.OrdinalIgnoreCase)).ToList();

        if (legendOptions.Count == 0) return;

        if (statement.VisualType == VisualType.Gauge)
            throw new InvalidOperationException("LEGEND options are not supported on GAUGE visuals.");

        var hasAnchor = statement.Options.Any(option => option.Key.Equals("LEGEND_ANCHOR", StringComparison.OrdinalIgnoreCase));
        var posValue = statement.Options.FirstOrDefault(option => option.Key.Equals("LEGEND_POSITION", StringComparison.OrdinalIgnoreCase))?.Value
            ?? statement.Options.FirstOrDefault(option => option.Key.Equals("LEGEND", StringComparison.OrdinalIgnoreCase))?.Value;
        if (hasAnchor && !string.Equals(posValue, "INSIDE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("LEGEND_ANCHOR requires LEGEND_POSITION = INSIDE.");

        foreach (var opt in legendOptions)
        {
            var key = opt.Key.ToUpperInvariant();
            var val = opt.Value;

            switch (key)
            {
                case "LEGEND":
                    if (val.ToUpperInvariant() is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0" or "TOP" or "BOTTOM" or "LEFT" or "RIGHT" or "INSIDE"))
                        throw new InvalidOperationException($"Invalid LEGEND value '{val}'.");
                    break;

                case "LEGEND_POSITION":
                    if (val.ToUpperInvariant() is not ("TOP" or "BOTTOM" or "LEFT" or "RIGHT" or "INSIDE"))
                        throw new InvalidOperationException($"Invalid LEGEND_POSITION '{val}'. Valid values are TOP, BOTTOM, LEFT, RIGHT, or INSIDE.");
                    break;

                case "LEGEND_ANCHOR":
                    if (val.ToUpperInvariant() is not ("TOP_LEFT" or "TOP_RIGHT" or "BOTTOM_LEFT" or "BOTTOM_RIGHT"))
                        throw new InvalidOperationException($"Invalid LEGEND_ANCHOR '{val}'. Valid values are TOP_LEFT, TOP_RIGHT, BOTTOM_LEFT, or BOTTOM_RIGHT.");
                    break;

                case "LEGEND_ORIENTATION":
                    if (val.ToUpperInvariant() is not ("HORIZONTAL" or "VERTICAL"))
                        throw new InvalidOperationException($"Invalid LEGEND_ORIENTATION '{val}'. Valid values are HORIZONTAL or VERTICAL.");
                    break;

                case "LEGEND_REVERSE":
                    if (val.ToUpperInvariant() is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                        throw new InvalidOperationException($"Invalid LEGEND_REVERSE value '{val}'. Valid values are ON or OFF.");
                    break;

                case "LEGEND_COLUMNS":
                    if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cols) || cols <= 0)
                        throw new InvalidOperationException($"Invalid LEGEND_COLUMNS '{val}'. Must be a positive integer.");
                    break;

                case "LEGEND_FONT_SIZE":
                    var trimmedSize = val.Trim().TrimEnd('p', 'x', 'P', 'X', 't', 'T');
                    if (!decimal.TryParse(trimmedSize, NumberStyles.Number, CultureInfo.InvariantCulture, out var size) || size <= 0)
                        throw new InvalidOperationException($"Invalid LEGEND_FONT_SIZE '{val}'. Must be a positive number.");
                    break;

                case "LEGEND_FONT_WEIGHT":
                    var weightUpper = val.ToUpperInvariant();
                    if (weightUpper is not ("NORMAL" or "BOLD" or "BOLDER" or "LIGHTER" or "100" or "200" or "300" or "400" or "500" or "600" or "700" or "800" or "900"))
                        throw new InvalidOperationException($"Invalid LEGEND_FONT_WEIGHT '{val}'.");
                    break;
            }
        }
    }

    private static bool HasPrimaryValueMapping(CreateVisualStatement statement) =>
        statement.Mappings.Any(mapping =>
            mapping.Role.Equals("Y", StringComparison.OrdinalIgnoreCase) ||
            mapping.Role.Equals("VALUE", StringComparison.OrdinalIgnoreCase) ||
            (statement.VisualType == VisualType.Candlestick && mapping.Role.Equals("CLOSE", StringComparison.OrdinalIgnoreCase))) ||
        statement.TypedSeries.Count > 0;

    private static string OverlayName(OverlayType type) => type switch
    {
        OverlayType.RunningTotal => "RUNNING_TOTAL",
        OverlayType.PercentOfTotal => "PERCENT_OF_TOTAL",
        _ => type.ToString().ToUpperInvariant()
    };

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
        "GROUP" when type == VisualType.Gantt => FieldChannel.Row,
        "NAME" or "LABEL" or "CATEGORY" when type == VisualType.Funnel => FieldChannel.X,
        "VALUE" when type == VisualType.Funnel => FieldChannel.Y,
        "VALUE" when type == VisualType.Gauge => FieldChannel.Radius,
        "LABEL" when type == VisualType.Gauge => FieldChannel.Text,
        "MIN" when type == VisualType.Gauge => FieldChannel.Y,
        "MAX" when type == VisualType.Gauge => FieldChannel.Y2,
        "GOAL" when type == VisualType.Gauge => FieldChannel.Detail,
        "GOAL2" when type == VisualType.Gauge => FieldChannel.High,
        "GOAL_LABEL" when type == VisualType.Gauge => FieldChannel.Low,
        "GOAL2_LABEL" when type == VisualType.Gauge => FieldChannel.ConfidenceLow,
        "REGION" when type == VisualType.Map => FieldChannel.Region,
        "LON" or "LONGITUDE" when type == VisualType.Map => FieldChannel.Longitude,
        "LAT" or "LATITUDE" when type == VisualType.Map => FieldChannel.Latitude,
        "VALUE" when type == VisualType.Map => FieldChannel.Color,
        "COLOR" when type == VisualType.Map => FieldChannel.Color,
        "SIZE" when type == VisualType.Map => FieldChannel.Size,
        "LABEL" when type == VisualType.Map => FieldChannel.Text,
        "TOOLTIP" when type == VisualType.Map => FieldChannel.Tooltip,
        "FACET" when type == VisualType.Trellis => FieldChannel.Column,
        "VALUE" when type == VisualType.HeatMap => FieldChannel.Size,
        "NAME" when type == VisualType.Waterfall => FieldChannel.X,
        "VALUE" when type == VisualType.Waterfall => FieldChannel.Y,
        "TOTAL" when type == VisualType.Waterfall => FieldChannel.Detail,
        "SUBTOTAL" when type == VisualType.Waterfall => FieldChannel.Low,
        "LOW" when type == VisualType.BoxPlot => FieldChannel.Low,
        "Q1" when type == VisualType.BoxPlot => FieldChannel.Q1,
        "MEDIAN" when type == VisualType.BoxPlot => FieldChannel.Median,
        "Q3" when type == VisualType.BoxPlot => FieldChannel.Q3,
        "HIGH" when type == VisualType.BoxPlot => FieldChannel.High,
        "MEAN" when type == VisualType.BoxPlot => FieldChannel.Mean,
        "OPEN" when type == VisualType.Candlestick => FieldChannel.Open,
        "HIGH" when type == VisualType.Candlestick => FieldChannel.High,
        "LOW" when type == VisualType.Candlestick => FieldChannel.Low,
        "CLOSE" when type == VisualType.Candlestick => FieldChannel.Close,
        "VOLUME" when type == VisualType.Candlestick => FieldChannel.Y2,
        "DIMENSION" or "METRIC" or "DETAIL" when type == VisualType.Radar => FieldChannel.Detail,
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
            => ["VALUE", "LABEL", "MIN", "MAX", "GOAL", "GOAL2", "GOAL_LABEL", "GOAL2_LABEL", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Map
            => ["REGION", "VALUE", "LON", "LONGITUDE", "LAT", "LATITUDE", "LABEL", "COLOR", "SIZE", "TOOLTIP"],
        VisualType.HeatMap
            => ["X", "Y", "VALUE", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Waterfall
            => ["NAME", "X", "VALUE", "Y", "TOTAL", "SUBTOTAL", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.BoxPlot
            => ["X", "Y", "LOW", "Q1", "MEDIAN", "Q3", "HIGH", "MEAN", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Candlestick
            => ["X", "OPEN", "HIGH", "LOW", "CLOSE", "VOLUME", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Gantt
            => ["X", "START", "X2", "END", "Y", "LABEL", "GROUP", "PROGRESS", "MILESTONE", "DEPENDS_ON", "SERIES", "COLOR", "TOOLTIP"],
        VisualType.Trellis
            => ["X", "Y", "Y2", "FACET", "SERIES", "COLOR", "SIZE", "TOOLTIP"],
        VisualType.Radar
            => ["X", "Y", "SERIES", "COLOR", "DIMENSION", "METRIC", "DETAIL", "TOOLTIP"],
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
        if (channel is FieldChannel.Longitude or FieldChannel.Latitude ||
            (type == VisualType.Map && channel == FieldChannel.Color && mapping.Role.Equals("VALUE", StringComparison.OrdinalIgnoreCase)))
            return DataSemanticKind.Quantitative;
        if (channel is FieldChannel.Y or FieldChannel.Y2 or FieldChannel.YStart or FieldChannel.YEnd or
            FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or FieldChannel.High or FieldChannel.Mean or
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
            FieldChannel.Q3 or FieldChannel.High or FieldChannel.Mean or FieldChannel.Open or FieldChannel.Close or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or
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

        var sharedAxis = statement.Options.FirstOrDefault(option => option.Key.Equals("SHARED_AXIS", StringComparison.OrdinalIgnoreCase))?.Value;
        var sharedY = statement.Options.FirstOrDefault(option => option.Key.Equals("SHARED_Y", StringComparison.OrdinalIgnoreCase))?.Value;
        var sharedX = statement.Options.FirstOrDefault(option => option.Key.Equals("SHARED_X", StringComparison.OrdinalIgnoreCase))?.Value;
        var sharedColor = statement.Options.FirstOrDefault(option => option.Key.Equals("SHARED_COLOR", StringComparison.OrdinalIgnoreCase))?.Value;

        var isScatter = TrellisChartTypeFromOptions(statement.Options) == "SCATTER";

        var yShared = sharedY is not null ? IsOn(sharedY) : (sharedAxis is null || IsOn(sharedAxis));
        var xShared = sharedX is not null ? IsOn(sharedX) : !isScatter;
        var colorShared = sharedColor is null || IsOn(sharedColor);

        return new FacetSpec(null, facet.Field, new ScaleResolutionSpec(
            X: xShared ? ScaleResolutionMode.Shared : ScaleResolutionMode.Independent,
            Y: yShared ? ScaleResolutionMode.Shared : ScaleResolutionMode.Independent,
            Color: colorShared ? ScaleResolutionMode.Shared : ScaleResolutionMode.Independent));
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
    private static decimal? ResolveStartAngle(CreateVisualStatement statement, VisualManifest manifest)
    {
        var text = (statement.Options.FirstOrDefault(o => o.Key.Equals("START_ANGLE", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("START_ANGLE"))?.Trim().TrimEnd('°', 'd', 'e', 'g');
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static decimal ResolveInnerRadius(CreateVisualStatement statement, VisualManifest manifest)
    {
        var text = (statement.Options.FirstOrDefault(o => o.Key.Equals("INNER_RADIUS", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("INNER_RADIUS"))?.Trim().TrimEnd('%');
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
    private static string? ParseAxisText(CreateVisualStatement statement, VisualManifest manifest, string axis, string option)
    {
        var axisObj = statement.AxisOptions.FirstOrDefault(a => a.Axis.Equals(axis, StringComparison.OrdinalIgnoreCase));
        var optVal = axisObj?.Options.FirstOrDefault(o => o.Key.Equals(option, StringComparison.OrdinalIgnoreCase))?.Value;
        return optVal ?? manifest.Options.GetValueOrDefault($"axis:{axis.ToLowerInvariant()}:{option.ToLowerInvariant()}");
    }
    private static bool? ParseAxisBool(CreateVisualStatement statement, VisualManifest manifest, string axis, string option)
    {
        var value = ParseAxisText(statement, manifest, axis, option);
        if (value is null) return null;
        return value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
    private static int? ParseAxisInt(CreateVisualStatement statement, VisualManifest manifest, string axis, string option) =>
        int.TryParse(ParseAxisText(statement, manifest, axis, option), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value : null;
    private static decimal? ParseAxisDecimal(CreateVisualStatement statement, VisualManifest manifest, string axis, string option) =>
        decimal.TryParse(ParseAxisText(statement, manifest, axis, option), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value : null;

    public static bool TryParseSizeRange(string? text, out decimal min, out decimal max)
    {
        min = 0m;
        max = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim().Trim('(', ')', '[', ']');
        var parts = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        return decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out min) &&
            decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out max);
    }

    private static bool ResolveBubbleMinMax(CreateVisualStatement statement, VisualManifest manifest, out decimal min, out decimal max)
    {
        min = 5m;
        max = 65m;
        var hasRange = false;
        var sizeRange = statement.Options.FirstOrDefault(o => o.Key.Equals("SIZE_RANGE", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("SIZE_RANGE");
        if (!string.IsNullOrWhiteSpace(sizeRange) && TryParseSizeRange(sizeRange, out var rMin, out var rMax))
        {
            min = rMin;
            max = rMax;
            hasRange = true;
        }

        var minBubble = statement.Options.FirstOrDefault(o => o.Key.Equals("MIN_BUBBLE_SIZE", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("MIN_BUBBLE_SIZE");
        if (!string.IsNullOrWhiteSpace(minBubble) && decimal.TryParse(minBubble, NumberStyles.Number, CultureInfo.InvariantCulture, out var mb))
        {
            min = mb;
            hasRange = true;
        }

        var maxBubble = statement.Options.FirstOrDefault(o => o.Key.Equals("MAX_BUBBLE_SIZE", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("MAX_BUBBLE_SIZE");
        if (!string.IsNullOrWhiteSpace(maxBubble) && decimal.TryParse(maxBubble, NumberStyles.Number, CultureInfo.InvariantCulture, out var xb))
        {
            max = xb;
            hasRange = true;
        }

        return hasRange || statement.VisualType == VisualType.Bubble;
    }

    private static PositionAdjustmentSpec? ResolvePositionAdjustment(
        CreateVisualStatement statement,
        VisualManifest manifest,
        ImmutableArray<FieldBinding> bindings)
    {
        if (statement.VisualType != VisualType.Scatter) return null;

        var jitterOpt = statement.Options.FirstOrDefault(o => o.Key.Equals("JITTER", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("JITTER");

        if (string.IsNullOrWhiteSpace(jitterOpt) ||
            jitterOpt.Equals("OFF", StringComparison.OrdinalIgnoreCase) ||
            jitterOpt.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ||
            jitterOpt == "0")
        {
            return null;
        }

        decimal x = 0.15m;
        var widthStr = statement.Options.FirstOrDefault(o => o.Key.Equals("JITTER:WIDTH", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER_WIDTH", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER:X", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("JITTER:WIDTH")
            ?? manifest.Options.GetValueOrDefault("JITTER_WIDTH")
            ?? manifest.Options.GetValueOrDefault("JITTER:X");
        if (!string.IsNullOrWhiteSpace(widthStr) && decimal.TryParse(widthStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedW))
        {
            x = parsedW;
        }

        decimal y = 0.15m;
        var heightStr = statement.Options.FirstOrDefault(o => o.Key.Equals("JITTER:HEIGHT", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER_HEIGHT", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER:Y", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("JITTER:HEIGHT")
            ?? manifest.Options.GetValueOrDefault("JITTER_HEIGHT")
            ?? manifest.Options.GetValueOrDefault("JITTER:Y");
        if (!string.IsNullOrWhiteSpace(heightStr) && decimal.TryParse(heightStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedH))
        {
            y = parsedH;
        }

        var keyField = statement.Options.FirstOrDefault(o => o.Key.Equals("JITTER:KEY", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER_KEY", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("KEY", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("JITTER:KEY")
            ?? manifest.Options.GetValueOrDefault("JITTER_KEY")
            ?? statement.Mappings.FirstOrDefault(m => m.Role.Equals("KEY", StringComparison.OrdinalIgnoreCase) || m.Role.Equals("ID", StringComparison.OrdinalIgnoreCase))?.Column
            ?? "__row_id";

        int seed = 0;
        var seedStr = statement.Options.FirstOrDefault(o => o.Key.Equals("JITTER:SEED", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER_SEED", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("JITTER:SEED")
            ?? manifest.Options.GetValueOrDefault("JITTER_SEED");
        if (!string.IsNullOrWhiteSpace(seedStr) && int.TryParse(seedStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeed))
        {
            seed = parsedSeed;
        }

        return new PositionAdjustmentSpec(PositionAdjustmentKind.Jitter, x, y, keyField, seed);
    }

    private static void ValidateScatterBubbleOptions(CreateVisualStatement statement)
    {
        var jitter = statement.Options.FirstOrDefault(o => o.Key.Equals("JITTER", StringComparison.OrdinalIgnoreCase) || o.Key.StartsWith("JITTER:", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER_WIDTH", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER_HEIGHT", StringComparison.OrdinalIgnoreCase));
        if (jitter is not null && statement.VisualType != VisualType.Scatter)
        {
            throw new InvalidOperationException($"JITTER is supported only on SCATTER visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
        }

        var widthStr = statement.Options.FirstOrDefault(o => o.Key.Equals("JITTER:WIDTH", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER_WIDTH", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER:X", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(widthStr))
        {
            if (!decimal.TryParse(widthStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var w) || w < 0m || w > 1m)
                throw new InvalidOperationException($"Invalid JITTER width '{widthStr}'. Must be between 0 and 1.");
        }

        var heightStr = statement.Options.FirstOrDefault(o => o.Key.Equals("JITTER:HEIGHT", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER_HEIGHT", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("JITTER:Y", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(heightStr))
        {
            if (!decimal.TryParse(heightStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var h) || h < 0m || h > 1m)
                throw new InvalidOperationException($"Invalid JITTER height '{heightStr}'. Must be between 0 and 1.");
        }

        var sizeRange = statement.Options.FirstOrDefault(o => o.Key.Equals("SIZE_RANGE", StringComparison.OrdinalIgnoreCase))?.Value;
        var minBubble = statement.Options.FirstOrDefault(o => o.Key.Equals("MIN_BUBBLE_SIZE", StringComparison.OrdinalIgnoreCase))?.Value;
        var maxBubble = statement.Options.FirstOrDefault(o => o.Key.Equals("MAX_BUBBLE_SIZE", StringComparison.OrdinalIgnoreCase))?.Value;

        if (sizeRange is not null || minBubble is not null || maxBubble is not null)
        {
            if (statement.VisualType is not (VisualType.Bubble or VisualType.Scatter))
                throw new InvalidOperationException($"SIZE_RANGE, MIN_BUBBLE_SIZE, and MAX_BUBBLE_SIZE are supported only on BUBBLE and SCATTER visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");

            decimal min = 5m, max = 65m;
            if (sizeRange is not null)
            {
                if (!TryParseSizeRange(sizeRange, out min, out max))
                    throw new InvalidOperationException($"Invalid SIZE_RANGE '{sizeRange}'. Expected format (min_px, max_px) with positive numbers.");
                if (min < 0m || max < 0m)
                    throw new InvalidOperationException($"Invalid SIZE_RANGE '{sizeRange}'. Bubble sizes must be non-negative.");
                if (min > max)
                    throw new InvalidOperationException($"Invalid SIZE_RANGE '{sizeRange}'. Minimum bubble size ({min}) cannot exceed maximum bubble size ({max}).");
            }

            if (minBubble is not null)
            {
                if (!decimal.TryParse(minBubble, NumberStyles.Number, CultureInfo.InvariantCulture, out var mb) || mb < 0m)
                    throw new InvalidOperationException($"Invalid MIN_BUBBLE_SIZE '{minBubble}'. Must be a non-negative number.");
                min = mb;
            }

            if (maxBubble is not null)
            {
                if (!decimal.TryParse(maxBubble, NumberStyles.Number, CultureInfo.InvariantCulture, out var mb) || mb < 0m)
                    throw new InvalidOperationException($"Invalid MAX_BUBBLE_SIZE '{maxBubble}'. Must be a non-negative number.");
                max = mb;
            }

            if (min > max)
                throw new InvalidOperationException($"Minimum bubble size ({min}) cannot exceed maximum bubble size ({max}).");
        }
    }

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

    private static ImmutableArray<StyleToken> ResolveHeatMapLayerStyle(CreateVisualStatement statement, VisualManifest manifest)
    {
        var builder = ImmutableArray.CreateBuilder<StyleToken>();
        builder.Add(new StyleToken("layout", "heatmap"));
        builder.Add(new StyleToken("preserveRows", "true"));

        void Forward(string key)
        {
            var val = statement.Options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value
                ?? manifest.Options.GetValueOrDefault(key);
            if (!string.IsNullOrWhiteSpace(val)) builder.Add(new StyleToken(key, val));
        }

        Forward("CELL_BORDER");
        Forward("CELL_BORDER_COLOR");
        Forward("CELL_BORDER_WIDTH");
        Forward("NULL_COLOR");
        Forward("MIDPOINT");
        Forward("COLOR_MID");
        Forward("COLOR_LOW");
        Forward("COLOR_HIGH");
        Forward("X_SORT");
        Forward("Y_SORT");

        return builder.ToImmutable();
    }

    private static void ValidateHeatmapOptions(CreateVisualStatement statement)
    {
        var isHeatmap = statement.VisualType == VisualType.HeatMap;
        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "MIDPOINT" or "COLOR_MID" or "CELL_BORDER" or "CELL_BORDER_COLOR" or "CELL_BORDER_WIDTH")
            {
                if (!isHeatmap)
                    throw new InvalidOperationException($"{key} option is supported only on HEATMAP visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            }
            else if (key is "COLOR_LOW" or "COLOR_HIGH" or "NULL_COLOR")
            {
                if (!isHeatmap && statement.VisualType != VisualType.Map)
                    throw new InvalidOperationException($"{key} option is supported only on HEATMAP and MAP visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            }

            if (isHeatmap)
            {
                if (key == "MIDPOINT")
                {
                    if (!decimal.TryParse(opt.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                        throw new InvalidOperationException($"Invalid MIDPOINT '{opt.Value}'. Must be a number.");
                }
                else if (key == "CELL_BORDER")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE"))
                        throw new InvalidOperationException($"Invalid CELL_BORDER '{opt.Value}'. Expected ON or OFF.");
                }
                else if (key == "CELL_BORDER_WIDTH")
                {
                    if (!decimal.TryParse(opt.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var width) || width < 0m)
                        throw new InvalidOperationException($"Invalid CELL_BORDER_WIDTH '{opt.Value}'. Must be a non-negative number.");
                }
                else if (key is "X_SORT" or "Y_SORT")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("SOURCE" or "ALPHA" or "VALUE_DESC" or "VALUE_ASC" or "VALUE"))
                        throw new InvalidOperationException($"Invalid {key} '{opt.Value}'. Expected SOURCE, ALPHA, VALUE_DESC, or VALUE_ASC.");
                }
            }
        }
    }

    private void ValidateMapOptions(CreateVisualStatement statement)
    {
        if (statement.VisualType != VisualType.Map)
        {
            if (statement.Options.Any(o => o.Key.Equals("BASE_MAP", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"BASE_MAP option is supported only on MAP visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            return;
        }
        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key == "COLOR_SCALE")
            {
                var upper = opt.Value?.ToUpperInvariant();
                if (upper is not ("LINEAR" or "QUANTILE" or "QUANTIZE" or "THRESHOLD"))
                    throw new InvalidOperationException($"Invalid COLOR_SCALE '{opt.Value}'. Valid values are LINEAR, QUANTILE, QUANTIZE, or THRESHOLD.");
            }
            else if (key == "ZOOM")
            {
                if (!decimal.TryParse(opt.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var z) || z <= 0m)
                    throw new InvalidOperationException($"Invalid ZOOM '{opt.Value}'. Must be a positive number.");
            }
            else if (key == "MODE")
            {
                var upper = opt.Value?.ToUpperInvariant();
                if (upper is not ("CHOROPLETH" or "POINTS"))
                    throw new InvalidOperationException($"Invalid MODE '{opt.Value}'. Valid values are CHOROPLETH or POINTS.");
            }
            else if (key == "BASE_MAP")
            {
                var val = opt.Value?.Trim('\'', '"') ?? string.Empty;
                var testUrl = val.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0").Replace("{s}", "a");
                if (!Uri.TryCreate(testUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException($"Invalid BASE_MAP '{opt.Value}'. Must be a valid HTTP or HTTPS URL template.");
                if (!val.Contains("{z}") || !val.Contains("{x}") || !val.Contains("{y}"))
                    throw new InvalidOperationException($"Invalid BASE_MAP URL template '{opt.Value}'. Must contain '{{z}}', '{{x}}', and '{{y}}' placeholders.");
                context?.SecurityService?.ValidateHost(uri.Host);
            }
        }
    }

    private static ImmutableArray<StyleToken> ResolveMapLayerStyle(CreateVisualStatement statement, VisualManifest manifest)
    {
        var builder = ImmutableArray.CreateBuilder<StyleToken>();
        void Forward(string key)
        {
            var val = statement.Options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value
                ?? manifest.Options.GetValueOrDefault(key);
            if (!string.IsNullOrWhiteSpace(val)) builder.Add(new StyleToken(key, val));
        }
        Forward("COLOR_SCALE");
        Forward("COLOR_LOW");
        Forward("COLOR_HIGH");
        Forward("NULL_COLOR");
        Forward("ZOOM");
        Forward("CENTER");
        Forward("MODE");
        Forward("BASE_MAP");
        return builder.ToImmutable();
    }

    private static ColorRangeSpec? ResolveMapColorRange(CreateVisualStatement statement, VisualManifest manifest)
    {
        var lowColor = statement.Options.FirstOrDefault(o => o.Key.Equals("COLOR_LOW", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("COLOR_LOW")
            ?? "#dbeafe";
        var highColor = statement.Options.FirstOrDefault(o => o.Key.Equals("COLOR_HIGH", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("COLOR_HIGH")
            ?? "#1d4ed8";
        var nullColor = statement.Options.FirstOrDefault(o => o.Key.Equals("NULL_COLOR", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("NULL_COLOR")
            ?? "#e5e7eb";
        return new ColorRangeSpec(ColorRangeKind.Gradient, lowColor, highColor, null, null, nullColor);
    }

    private static ColorRangeSpec? ResolveHeatMapColorRange(CreateVisualStatement statement, VisualManifest manifest)
    {
        string? lowColor = null;
        string? midColor = null;
        string? highColor = null;

        var colorsOpt = statement.Options.FirstOrDefault(o => o.Key.Equals("COLORS", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("COLORS");
        if (!string.IsNullOrWhiteSpace(colorsOpt))
        {
            var parts = colorsOpt.Trim('(', ')', '[', ']').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                lowColor = parts[0].Trim('\'', '"');
                highColor = parts[1].Trim('\'', '"');
            }
            else if (parts.Length >= 3)
            {
                lowColor = parts[0].Trim('\'', '"');
                midColor = parts[1].Trim('\'', '"');
                highColor = parts[2].Trim('\'', '"');
            }
        }

        var cl = statement.Options.FirstOrDefault(o => o.Key.Equals("COLOR_LOW", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("color:low", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("color:min", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("COLOR_LOW")
            ?? manifest.Options.GetValueOrDefault("color:low")
            ?? manifest.Options.GetValueOrDefault("color:min");
        if (!string.IsNullOrWhiteSpace(cl)) lowColor = cl;

        var cm = statement.Options.FirstOrDefault(o => o.Key.Equals("COLOR_MID", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("color:mid", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("COLOR_MID")
            ?? manifest.Options.GetValueOrDefault("color:mid");
        if (!string.IsNullOrWhiteSpace(cm)) midColor = cm;

        var ch = statement.Options.FirstOrDefault(o => o.Key.Equals("COLOR_HIGH", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("color:high", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("color:max", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("COLOR_HIGH")
            ?? manifest.Options.GetValueOrDefault("color:high")
            ?? manifest.Options.GetValueOrDefault("color:max");
        if (!string.IsNullOrWhiteSpace(ch)) highColor = ch;

        lowColor ??= "#dbeafe";
        highColor ??= "#1d4ed8";

        decimal? midpoint = null;
        var midpointStr = statement.Options.FirstOrDefault(o => o.Key.Equals("MIDPOINT", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("MIDPOINT");
        if (!string.IsNullOrWhiteSpace(midpointStr) && decimal.TryParse(midpointStr, NumberStyles.Number, CultureInfo.InvariantCulture, out var mp))
        {
            midpoint = mp;
        }

        var nullColor = statement.Options.FirstOrDefault(o => o.Key.Equals("NULL_COLOR", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("NULL_COLOR")
            ?? "#f1f5f9";

        var isDiverging = midColor is not null || midpoint is not null;
        if (isDiverging)
        {
            midColor ??= "#ffffff";
            midpoint ??= 0m;
            return new ColorRangeSpec(ColorRangeKind.Diverging, lowColor, highColor, midColor, midpoint, nullColor);
        }

        return new ColorRangeSpec(ColorRangeKind.Gradient, lowColor, highColor, null, null, nullColor);
    }

    private static void ValidateWaterfallOptions(CreateVisualStatement statement)
    {
        var isWaterfall = statement.VisualType == VisualType.Waterfall;
        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "CONNECTOR_LINES" or "CONNECTOR_LINE_COLOR" or "CONNECTOR_LINE_WIDTH" or "COLOR_SUBTOTAL" or "COLOR_TOTAL")
            {
                if (!isWaterfall)
                    throw new InvalidOperationException($"{key} option is supported only on WATERFALL visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            }

            if (isWaterfall)
            {
                if (key == "CONNECTOR_LINES")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE"))
                        throw new InvalidOperationException($"Invalid CONNECTOR_LINES '{opt.Value}'. Expected ON or OFF.");
                }
                else if (key == "CONNECTOR_LINE_WIDTH")
                {
                    if (!decimal.TryParse(opt.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var width) || width < 0m)
                        throw new InvalidOperationException($"Invalid CONNECTOR_LINE_WIDTH '{opt.Value}'. Must be a non-negative number.");
                }
            }

            if (key == "ORIENTATION")
            {
                if (statement.VisualType is not (VisualType.Waterfall or VisualType.Funnel or VisualType.BoxPlot or VisualType.Bar or VisualType.HorizontalBar))
                    throw new InvalidOperationException($"ORIENTATION option is not supported on {statement.VisualType.ToString().ToUpperInvariant()} visuals.");
                var upper = opt.Value.ToUpperInvariant();
                if (upper is not ("HORIZONTAL" or "VERTICAL"))
                    throw new InvalidOperationException($"Invalid ORIENTATION '{opt.Value}'. Valid values are HORIZONTAL or VERTICAL.");
            }
        }
    }

    private static ImmutableArray<StyleToken> ResolveWaterfallLayerStyle(CreateVisualStatement statement, VisualManifest manifest)
    {
        var tokens = ImmutableArray.CreateBuilder<StyleToken>();
        tokens.Add(new StyleToken("layout", "waterfall"));
        tokens.Add(new StyleToken("preserveRows", "true"));

        if (IsHorizontal(statement, manifest))
            tokens.Add(new StyleToken("orientation", "horizontal"));

        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "CONNECTOR_LINES" or "CONNECTOR_LINE_COLOR" or "CONNECTOR_LINE_WIDTH" or
                "COLOR_TOTAL" or "COLOR_SUBTOTAL" or "COLOR_UP" or "COLOR_DOWN" or "COLOR_INCREASE" or "COLOR_DECREASE")
            {
                tokens.Add(new StyleToken(opt.Key.ToLowerInvariant(), opt.Value));
            }
        }

        return tokens.ToImmutable();
    }

    private static bool IsHorizontal(CreateVisualStatement statement, VisualManifest manifest)
    {
        var opt = statement.Options.FirstOrDefault(o => o.Key.Equals("ORIENTATION", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("ORIENTATION");
        return string.Equals(opt, "HORIZONTAL", StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<StyleToken> ResolveGanttLayerStyle(CreateVisualStatement statement, VisualManifest manifest)
    {
        var tokens = ImmutableArray.CreateBuilder<StyleToken>();
        tokens.Add(new StyleToken("layout", "gantt"));
        tokens.Add(new StyleToken("preserveRows", "true"));

        void Forward(string key)
        {
            var val = statement.Options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value
                ?? manifest.Options.GetValueOrDefault(key);
            if (!string.IsNullOrWhiteSpace(val)) tokens.Add(new StyleToken(key, val));
        }

        Forward("TODAY_LINE");
        Forward("TODAY_COLOR");
        Forward("TODAY_DATE");
        Forward("LABEL_POSITION");

        return tokens.ToImmutable();
    }

    private static void ValidateGanttOptions(CreateVisualStatement statement)
    {
        var isGantt = statement.VisualType == VisualType.Gantt;
        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "TODAY_LINE" or "TODAY_COLOR" or "TODAY_DATE")
            {
                if (!isGantt)
                    throw new InvalidOperationException($"{key} option is supported only on GANTT visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            }

            if (isGantt)
            {
                if (key == "TODAY_LINE")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                        throw new InvalidOperationException($"Invalid TODAY_LINE '{opt.Value}'. Valid values are ON or OFF.");
                }
                else if (key == "LABEL_POSITION")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("LEFT" or "INSIDE" or "RIGHT" or "NONE"))
                        throw new InvalidOperationException($"Invalid LABEL_POSITION '{opt.Value}'. Valid values are LEFT, INSIDE, RIGHT, or NONE.");
                }
            }
        }
    }

    private static ImmutableArray<StyleToken> ResolveCandlestickLayerStyle(
        CreateVisualStatement statement,
        VisualManifest manifest)
    {
        var tokens = ImmutableArray.CreateBuilder<StyleToken>();
        tokens.Add(new StyleToken("layout", "candlestick"));
        tokens.Add(new StyleToken("preserveRows", "true"));

        void Forward(string key)
        {
            var val = statement.Options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value
                ?? manifest.Options.GetValueOrDefault(key);
            if (!string.IsNullOrWhiteSpace(val)) tokens.Add(new StyleToken(key, val));
        }

        Forward("COLOR_UP");
        Forward("COLOR_DOWN");
        Forward("WICK_COLOR");
        Forward("WICK_COLOR_UP");
        Forward("WICK_COLOR_DOWN");
        Forward("VOLUME_COLOR");

        return tokens.ToImmutable();
    }

    private static void ValidateCandlestickOptions(CreateVisualStatement statement)
    {
        var isCandlestick = statement.VisualType == VisualType.Candlestick;
        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "WICK_COLOR" or "WICK_COLOR_UP" or "WICK_COLOR_DOWN" or "VOLUME_COLOR")
            {
                if (!isCandlestick)
                    throw new InvalidOperationException($"{key} option is supported only on CANDLESTICK visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            }

            if (isCandlestick)
            {
                if (key is "COLOR_UP" or "COLOR_DOWN" or "WICK_COLOR" or "WICK_COLOR_UP" or "WICK_COLOR_DOWN" or "VOLUME_COLOR")
                {
                    if (string.IsNullOrWhiteSpace(opt.Value))
                        throw new InvalidOperationException($"Candlestick option '{key}' cannot be empty.");
                }
            }
        }
    }

    private static ImmutableArray<StyleToken> ResolveRadarLayerStyle(
        CreateVisualStatement statement,
        VisualManifest manifest)
    {
        var tokens = ImmutableArray.CreateBuilder<StyleToken>();
        tokens.Add(new StyleToken("layout", "radar"));
        tokens.Add(new StyleToken("preserveRows", "true"));

        void Forward(string key)
        {
            var val = statement.Options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value
                ?? manifest.Options.GetValueOrDefault(key);
            if (!string.IsNullOrWhiteSpace(val)) tokens.Add(new StyleToken(key, val));
        }

        Forward("INDEPENDENT_AXES");
        Forward("FILL_OPACITY");
        Forward("SHAPE");
        Forward("FILL");

        return tokens.ToImmutable();
    }

    private static void ValidateRadarOptions(CreateVisualStatement statement)
    {
        var isRadar = statement.VisualType == VisualType.Radar;
        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "INDEPENDENT_AXES" or "FILL_OPACITY")
            {
                if (!isRadar)
                    throw new InvalidOperationException($"{key} option is supported only on RADAR visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            }

            if (isRadar)
            {
                if (key == "INDEPENDENT_AXES")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                        throw new InvalidOperationException($"Invalid INDEPENDENT_AXES '{opt.Value}'. Valid values are ON or OFF.");
                }
                else if (key == "FILL_OPACITY")
                {
                    if (!double.TryParse(opt.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) || opacity < 0.0 || opacity > 1.0)
                        throw new InvalidOperationException($"Invalid FILL_OPACITY '{opt.Value}'. Must be a number between 0.0 and 1.0.");
                }
                else if (key == "SHAPE")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("POLYGON" or "CIRCLE"))
                        throw new InvalidOperationException($"Invalid SHAPE '{opt.Value}'. Valid values are POLYGON or CIRCLE.");
                }
                else if (key == "FILL")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                        throw new InvalidOperationException($"Invalid FILL '{opt.Value}'. Valid values are ON or OFF.");
                }
            }
        }
    }

    private static ImmutableArray<StyleToken> ResolveFunnelLayerStyle(
        CreateVisualStatement statement,
        VisualManifest manifest)
    {
        var tokens = ImmutableArray.CreateBuilder<StyleToken>();
        tokens.Add(new StyleToken("layout", "funnel"));

        void Forward(string key, string? defaultVal = null)
        {
            var val = statement.Options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value
                ?? manifest.Options.GetValueOrDefault(key)
                ?? defaultVal;
            if (!string.IsNullOrWhiteSpace(val)) tokens.Add(new StyleToken(key, val));
        }

        var shape = statement.Options.FirstOrDefault(o => o.Key.Equals("FUNNEL_SHAPE", StringComparison.OrdinalIgnoreCase) || o.Key.Equals("SHAPE", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("FUNNEL_SHAPE")
            ?? manifest.Options.GetValueOrDefault("SHAPE")
            ?? "FUNNEL";
        tokens.Add(new StyleToken("FUNNEL_SHAPE", shape.ToUpperInvariant()));

        var defaultSort = shape.Equals("PYRAMID", StringComparison.OrdinalIgnoreCase) ? "VALUE_ASC" : "VALUE_DESC";
        var sort = statement.Options.FirstOrDefault(o => o.Key.Equals("SORT", StringComparison.OrdinalIgnoreCase))?.Value
            ?? manifest.Options.GetValueOrDefault("SORT")
            ?? defaultSort;
        tokens.Add(new StyleToken("SORT", sort.ToUpperInvariant()));

        Forward("SHOW_PERCENT");
        Forward("PERCENT_MODE", "STEP");

        return tokens.ToImmutable();
    }

    private static void ValidateFunnelOptions(CreateVisualStatement statement)
    {
        var isFunnel = statement.VisualType == VisualType.Funnel;
        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "PERCENT_MODE" or "FUNNEL_SHAPE")
            {
                if (!isFunnel)
                    throw new InvalidOperationException($"{key} option is supported only on FUNNEL visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            }

            if (isFunnel)
            {
                if (key == "SORT")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("SOURCE" or "VALUE_DESC" or "VALUE_ASC" or "VALUE"))
                        throw new InvalidOperationException($"Invalid SORT '{opt.Value}'. Valid values are SOURCE, VALUE_DESC, or VALUE_ASC.");
                }
                else if (key == "SHOW_PERCENT")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                        throw new InvalidOperationException($"Invalid SHOW_PERCENT '{opt.Value}'. Valid values are ON or OFF.");
                }
                else if (key == "PERCENT_MODE")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("STEP" or "TOTAL"))
                        throw new InvalidOperationException($"Invalid PERCENT_MODE '{opt.Value}'. Valid values are STEP or TOTAL.");
                }
                else if (key is "FUNNEL_SHAPE" or "SHAPE")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("FUNNEL" or "PYRAMID"))
                        throw new InvalidOperationException($"Invalid {key} '{opt.Value}'. Valid values are FUNNEL or PYRAMID.");
                }
            }
        }
    }

    private static ImmutableArray<StyleToken> ResolveBoxPlotLayerStyle(
        CreateVisualStatement statement,
        VisualManifest manifest)
    {
        var tokens = ImmutableArray.CreateBuilder<StyleToken>();
        tokens.Add(new StyleToken("layout", "boxplot"));
        tokens.Add(new StyleToken("preserveRows", "true"));

        void Forward(string key, string? defaultVal = null)
        {
            var val = statement.Options.FirstOrDefault(o => o.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value
                ?? manifest.Options.GetValueOrDefault(key)
                ?? defaultVal;
            if (!string.IsNullOrWhiteSpace(val)) tokens.Add(new StyleToken(key, val));
        }

        Forward("NOTCHED", "OFF");
        Forward("SHOW_MEAN", "OFF");
        Forward("SHOW_VIOLIN", "OFF");
        Forward("VIOLIN", "OFF");
        Forward("BOX_STYLE", "BOX");
        Forward("ORIENTATION", "VERTICAL");
        Forward("MEAN_COLOR");
        Forward("VIOLIN_COLOR");

        return tokens.ToImmutable();
    }

    private static void ValidateBoxPlotOptions(CreateVisualStatement statement)
    {
        var isBoxPlot = statement.VisualType == VisualType.BoxPlot;
        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "NOTCHED" or "SHOW_MEAN" or "SHOW_VIOLIN" or "VIOLIN" or "BOX_STYLE" or "MEAN_COLOR" or "VIOLIN_COLOR")
            {
                if (!isBoxPlot)
                    throw new InvalidOperationException($"{key} option is supported only on BOXPLOT visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
            }

            if (isBoxPlot)
            {
                if (key == "NOTCHED")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                        throw new InvalidOperationException($"Invalid NOTCHED '{opt.Value}'. Valid values are ON or OFF.");
                }
                else if (key == "SHOW_MEAN")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                        throw new InvalidOperationException($"Invalid SHOW_MEAN '{opt.Value}'. Valid values are ON or OFF.");
                }
                else if (key is "SHOW_VIOLIN" or "VIOLIN")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE" or "1" or "0"))
                        throw new InvalidOperationException($"Invalid {key} '{opt.Value}'. Valid values are ON or OFF.");
                }
                else if (key == "BOX_STYLE")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("BOX" or "VIOLIN" or "BOTH"))
                        throw new InvalidOperationException($"Invalid BOX_STYLE '{opt.Value}'. Valid values are BOX, VIOLIN, or BOTH.");
                }
                else if (key == "ORIENTATION")
                {
                    var upper = opt.Value.ToUpperInvariant();
                    if (upper is not ("VERTICAL" or "HORIZONTAL"))
                        throw new InvalidOperationException($"Invalid ORIENTATION '{opt.Value}'. Valid values are VERTICAL or HORIZONTAL.");
                }
            }
        }
    }

    private static void ValidateComboOptions(CreateVisualStatement statement)
    {
        var hasComboOpts = statement.Options.Any(o =>
            o.Key.Equals("SYNC_AXES", StringComparison.OrdinalIgnoreCase) ||
            o.Key.Equals("Y_MARK", StringComparison.OrdinalIgnoreCase) ||
            o.Key.Equals("Y2_MARK", StringComparison.OrdinalIgnoreCase));

        if (!hasComboOpts) return;

        if (statement.VisualType != VisualType.Combo)
        {
            var firstOpt = statement.Options.First(o =>
                o.Key.Equals("SYNC_AXES", StringComparison.OrdinalIgnoreCase) ||
                o.Key.Equals("Y_MARK", StringComparison.OrdinalIgnoreCase) ||
                o.Key.Equals("Y2_MARK", StringComparison.OrdinalIgnoreCase));
            throw new InvalidOperationException($"{firstOpt.Key.ToUpperInvariant()} option is supported only on COMBO visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
        }

        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key == "SYNC_AXES")
            {
                var upper = opt.Value.ToUpperInvariant();
                if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE"))
                    throw new InvalidOperationException($"Invalid SYNC_AXES '{opt.Value}'. Valid values are ON or OFF.");
            }
            else if (key is "Y_MARK" or "Y2_MARK")
            {
                var upper = opt.Value.ToUpperInvariant();
                if (upper is not ("BAR" or "LINE" or "AREA"))
                    throw new InvalidOperationException($"Invalid {key} '{opt.Value}'. Valid values are BAR, LINE, or AREA.");
            }
        }
    }

    private static void ValidateSymbolSizeOption(CreateVisualStatement statement)
    {
        var opt = statement.Options.FirstOrDefault(o => o.Key.Equals("SYMBOL_SIZE", StringComparison.OrdinalIgnoreCase));
        if (opt is null) return;

        if (statement.VisualType is not (VisualType.Line or VisualType.Scatter or VisualType.Bubble or VisualType.Combo))
            throw new InvalidOperationException($"SYMBOL_SIZE option is supported only on LINE, SCATTER, BUBBLE, and COMBO visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");

        if (!decimal.TryParse(opt.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var size) || size <= 0m)
            throw new InvalidOperationException($"Invalid SYMBOL_SIZE '{opt.Value}'. Must be a positive number.");
    }

    private static void ValidateTrellisOptions(CreateVisualStatement statement)
    {
        var hasTrellisOpts = statement.Options.Any(o =>
            o.Key.Equals("SHARED_X", StringComparison.OrdinalIgnoreCase) ||
            o.Key.Equals("SHARED_Y", StringComparison.OrdinalIgnoreCase) ||
            o.Key.Equals("SHARED_COLOR", StringComparison.OrdinalIgnoreCase) ||
            o.Key.Equals("SHARED_AXIS", StringComparison.OrdinalIgnoreCase));

        if (!hasTrellisOpts) return;

        if (statement.VisualType != VisualType.Trellis)
        {
            var firstOpt = statement.Options.First(o =>
                o.Key.Equals("SHARED_X", StringComparison.OrdinalIgnoreCase) ||
                o.Key.Equals("SHARED_Y", StringComparison.OrdinalIgnoreCase) ||
                o.Key.Equals("SHARED_COLOR", StringComparison.OrdinalIgnoreCase) ||
                o.Key.Equals("SHARED_AXIS", StringComparison.OrdinalIgnoreCase));
            throw new InvalidOperationException($"{firstOpt.Key.ToUpperInvariant()} option is supported only on TRELLIS visuals; found {statement.VisualType.ToString().ToUpperInvariant()}.");
        }

        foreach (var opt in statement.Options)
        {
            var key = opt.Key.ToUpperInvariant();
            if (key is "SHARED_X" or "SHARED_Y" or "SHARED_COLOR" or "SHARED_AXIS")
            {
                var upper = opt.Value.ToUpperInvariant();
                if (upper is not ("ON" or "OFF" or "TRUE" or "FALSE"))
                    throw new InvalidOperationException($"Invalid {key} '{opt.Value}'. Valid values are ON or OFF.");
            }
        }
    }

    private static NullValuePolicy ResolveNullPolicy(CreateVisualStatement statement)
    {
        var nullOpt = statement.Options.FirstOrDefault(o => o.Key.Equals("NULL_HANDLING", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(nullOpt))
        {
            return nullOpt.ToUpperInvariant() switch
            {
                "CONNECT" => NullValuePolicy.Skip,
                "ZERO" => NullValuePolicy.Zero,
                _ => NullValuePolicy.Gap
            };
        }
        return statement.VisualType == VisualType.Line || statement.VisualType == VisualType.Combo ||
               statement.VisualType == VisualType.Trellis && TrellisMark(statement) == MarkKind.Line
            ? NullValuePolicy.Gap
            : NullValuePolicy.Skip;
    }

    private static bool IsAreaLine(CreateVisualStatement statement) =>
        statement.Options.Any(o => o.Key.Equals("AREA", StringComparison.OrdinalIgnoreCase) &&
            (o.Value.Equals("ON", StringComparison.OrdinalIgnoreCase) || o.Value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || o.Value == "1"));

    private static string NormalizeErrorBarStyle(string? value)
    {
        var upper = value?.ToUpperInvariant();
        return upper is "NO_CAP" or "NO_CAPS" ? "NO_CAPS" : "CAPS";
    }

    private static string Sanitize(string value) => new(value.Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray());
}
