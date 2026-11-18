using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Core;

/// <summary>
/// The single source of semantic truth for <c>CUSTOM ... CHART (...)</c> authoring.
/// </summary>
/// <remarks>
/// Both the <c>AdvancedChartAuthoring</c> lint rule and the reporting lowerer run this validator, so an
/// editor diagnostic and a report preview failure can never disagree. Every diagnostic is anchored to
/// the offending AST node, not to the <c>CREATE VISUAL</c> header.
///
/// Two failure classes stay outside this validator because they are not expressible against the AST:
/// parameter resolution (an undeclared or secret-bearing <c>@variable</c>) and the contract-level
/// <c>ChartSpec.Validate()</c> backstop. Both are still surfaced as typed, positioned diagnostics by the
/// lowerer — the parameter case at the offending node, the backstop at the CHART clause.
/// </remarks>
public static class AdvancedChartSemanticValidator
{
    /// <summary>Diagnostic code shared by the lint rule, the lowerer, and report preview.</summary>
    public const string DiagnosticCode = "RPT-CHART";

    /// <summary>Diagnostic source shared by the lint rule, the lowerer, and report preview.</summary>
    public const string DiagnosticSource = "AdvancedChart";

    /// <summary>Validates the advanced chart declared by a CREATE VISUAL statement, if any.</summary>
    public static IReadOnlyList<Diagnostic> Validate(CreateVisualStatement visual) =>
        visual.AdvancedChart is null ? [] : Validate(visual.AdvancedChart, visual);

    /// <summary>Validates an advanced chart definition, anchoring diagnostics to its own nodes.</summary>
    /// <param name="chart">The parsed chart definition.</param>
    /// <param name="fallback">Node used when a chart node carries no source position.</param>
    public static IReadOnlyList<Diagnostic> Validate(AdvancedChartDefinition chart, AstNode fallback)
    {
        var results = new List<Diagnostic>();
        var chartNode = Anchor(chart, fallback);

        Duplicates(results, chart.Layers, layer => layer.Name, layer => Anchor(layer, chartNode), "layer");
        Duplicates(results, chart.Scales, scale => scale.Name, scale => Anchor(scale, chartNode), "scale");
        Duplicates(results, chart.Encodings, encoding => encoding.Channel.ToString(),
            encoding => Anchor(encoding, chartNode), "global encoding channel");

        ValidateScales(results, chart, chartNode);
        ValidateLayers(results, chart, chartNode);
        ValidateCoordinate(results, chart, chartNode);
        ValidateFacetAndResolution(results, chart, chartNode);
        if (fallback is CreateVisualStatement visual)
            ValidateVisualExtras(results, chart, visual, chartNode);

        return results;
    }

    private static readonly HashSet<string> NamedVisualOnlyCustomOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "AXIS_X", "AXIS_Y", "AXIS_Y2",
        "STACKED", "ORIENTATION", "SHOW_VALUES", "CUMULATIVE",
        "LINE_WIDTH", "INTERPOLATION", "LINE_DASH",
        "SYMBOL_STROKE_COLOR", "SYMBOL_STROKE_WIDTH", "ERROR_BAR_STYLE", "AREA_BASELINE",
        "SIZE_RANGE", "MIN_BUBBLE_SIZE", "MAX_BUBBLE_SIZE",
        "JITTER", "JITTER:WIDTH", "JITTER:HEIGHT", "JITTER:KEY", "JITTER:SEED",
        "JITTER_WIDTH", "JITTER_HEIGHT", "JITTER_KEY", "JITTER_SEED",
        "INNER_RADIUS", "START_ANGLE", "END_ANGLE",
        "PROJECTION", "MAP_NAME", "MAP_FILE", "FEATURE_KEY",
        "FUNNEL_SHAPE", "GAP", "BAR_GAP", "Y_MARK", "Y2_MARK",
        "VOLUME", "VOLUME_COLOR", "WHISKER_MODE", "SHOW_OUTLIERS", "SHOW_MEDIAN", "BOX_WIDTH",
        "BIN_COUNT", "RADAR_SHAPE", "RADAR_SCALE", "TODAY_LINE", "TODAY_COLOR", "TODAY_DATE",
        "WATERFALL_TOTAL_COLOR", "INCREASE_COLOR", "DECREASE_COLOR", "GRADIENT_COLORS",
        "GAUGE_STYLE", "TARGET_LABEL"
    };

    private static void ValidateVisualExtras(List<Diagnostic> results, AdvancedChartDefinition chart, CreateVisualStatement visual, AstNode chartNode)
    {
        if (visual.AxisOptions is { Count: > 0 })
        {
            var axes = string.Join(", ", visual.AxisOptions.Select(a => $"{a.Axis.ToUpperInvariant()}_AXIS").Distinct(StringComparer.OrdinalIgnoreCase));
            Add(results, chartNode, $"CUSTOM visuals configure axes inside CHART (SCALES (...)) and cannot use outer {axes}.");
        }
        foreach (var opt in visual.Options)
        {
            if (NamedVisualOnlyCustomOptions.Contains(opt.Key))
                Add(results, chartNode, $"Option '{opt.Key.ToUpperInvariant()}' is a named-visual option and cannot be used in outer OPTIONS on a CUSTOM visual; configure it inside CHART (...).");
        }
        if (visual.SegmentStyles.Count > 0 && !chart.Layers.Any(layer => layer.Mark == AdvancedChartMarkKind.Line))
            Add(results, chartNode, "SEGMENT_STYLE requires at least one LINE layer in CUSTOM CHART.");

        if (visual.Overlays.Count == 0)
            return;

        if (chart.Coordinate.Kind is not (AdvancedChartCoordinateKind.Cartesian or AdvancedChartCoordinateKind.TransposedCartesian))
        {
            Add(results, chartNode, "Analytical OVERLAYS are supported only on CARTESIAN and TRANSPOSED_CARTESIAN CUSTOM charts.");
            return;
        }

        var effectiveBindings = chart.Layers.SelectMany(layer => EffectiveEncodings(chart, layer)).ToList();
        var hasX = effectiveBindings.Any(binding => BaseScaleChannel(binding.Channel) == AdvancedChartChannel.X);
        var hasPrimaryY = effectiveBindings.Any(binding =>
            binding.DataKind == AdvancedChartDataKind.Quantitative &&
            binding.Channel is AdvancedChartChannel.Y or AdvancedChartChannel.Y2 or AdvancedChartChannel.Close);

        foreach (var overlay in visual.Overlays)
        {
            if (overlay.OverlayType == OverlayType.Forecast)
            {
                if (string.IsNullOrWhiteSpace(overlay.ForecastField))
                    Add(results, chartNode, "FORECAST overlay requires a forecast field name.");
                if (string.IsNullOrWhiteSpace(overlay.ConfidenceLowField) != string.IsNullOrWhiteSpace(overlay.ConfidenceHighField))
                    Add(results, chartNode, "FORECAST overlay requires both CONFIDENCE_LOW and CONFIDENCE_HIGH as a pair.");
                if (!hasX) Add(results, chartNode, "FORECAST overlay requires an X encoding.");
                if (!hasPrimaryY) Add(results, chartNode, "FORECAST overlay requires a quantitative Y encoding.");
            }
            else if (overlay.OverlayType == OverlayType.ReferenceLine)
            {
                if (!overlay.Parameter.HasValue || !double.IsFinite(overlay.Parameter.Value))
                    Add(results, chartNode, "REFERENCE_LINE requires a finite numeric VALUE.");
                if (!hasPrimaryY)
                    Add(results, chartNode, "REFERENCE_LINE overlay requires a quantitative Y encoding.");
            }
            else if (overlay.OverlayType == OverlayType.ReferenceBand)
            {
                if (!overlay.BandLow.HasValue || !overlay.BandHigh.HasValue ||
                    !double.IsFinite(overlay.BandLow.Value) || !double.IsFinite(overlay.BandHigh.Value) ||
                    overlay.BandLow.Value >= overlay.BandHigh.Value)
                    Add(results, chartNode, "REFERENCE_BAND requires finite numeric LOW < HIGH.");
                if (!hasPrimaryY)
                    Add(results, chartNode, "REFERENCE_BAND overlay requires a quantitative Y encoding.");
            }
            else if (overlay.OverlayType is OverlayType.RunningTotal or OverlayType.PercentOfTotal)
            {
                if (!hasPrimaryY)
                    Add(results, chartNode, $"{overlay.OverlayType} overlay requires a quantitative Y encoding.");
                if (string.IsNullOrWhiteSpace(overlay.TableCalculationField))
                    Add(results, chartNode, $"{overlay.OverlayType} overlay requires a pre-computed field name.");
            }
            else if (overlay.OverlayType != OverlayType.AnnotationPoint && !hasPrimaryY)
            {
                Add(results, chartNode, $"{overlay.OverlayType} overlay requires a quantitative Y encoding.");
            }
        }
    }

    /// <summary>Effective encodings for a layer: inherited global channels the layer does not override.</summary>
    public static IReadOnlyList<AdvancedChartEncoding> EffectiveEncodings(AdvancedChartDefinition chart, AdvancedChartLayer layer) =>
        layer.InheritEncodings
            ? chart.Encodings.Where(inherited => layer.Encodings.All(local => local.Channel != inherited.Channel))
                .Concat(layer.Encodings).ToList()
            : layer.Encodings.ToList();

    /// <summary>True when a scale declared for <paramref name="scale"/> may carry a <paramref name="binding"/> channel.</summary>
    public static bool CompatibleScaleChannel(AdvancedChartChannel scale, AdvancedChartChannel binding) => scale == binding ||
        scale == AdvancedChartChannel.X && binding is AdvancedChartChannel.X2 or AdvancedChartChannel.XStart or AdvancedChartChannel.XEnd ||
        scale == AdvancedChartChannel.Y && binding is AdvancedChartChannel.Y2 or AdvancedChartChannel.YStart or AdvancedChartChannel.YEnd or
            AdvancedChartChannel.Low or AdvancedChartChannel.Q1 or AdvancedChartChannel.Median or AdvancedChartChannel.Q3 or
            AdvancedChartChannel.High or AdvancedChartChannel.Open or AdvancedChartChannel.Close or
            AdvancedChartChannel.ErrorLow or AdvancedChartChannel.ErrorHigh or
            AdvancedChartChannel.ConfidenceLow or AdvancedChartChannel.ConfidenceHigh;

    /// <summary>The scale identifier the lowerer synthesizes for an un-declared binding.</summary>
    public static string InferredScaleId(AdvancedChartCoordinateKind coordinate, AdvancedChartAxisRole axis, AdvancedChartChannel channel) =>
        $"inferred-{coordinate.ToString().ToLowerInvariant()}-{(axis == AdvancedChartAxisRole.Secondary ? "secondary" : "primary")}-{BaseScaleChannel(channel).ToString().ToLowerInvariant()}";

    /// <summary>Resolves the scale ID for an encoding: author-declared scale or deterministic inferred fallback.</summary>
    public static string EffectiveScaleId(AdvancedChartCoordinateKind coordinate, AdvancedChartEncoding encoding) =>
        encoding.Scale ?? InferredScaleId(coordinate, encoding.Axis, encoding.Channel);

    /// <summary>Collapses interval and secondary channels onto the positional channel that owns the scale.</summary>
    public static AdvancedChartChannel BaseScaleChannel(AdvancedChartChannel channel) => channel switch
    {
        AdvancedChartChannel.X2 or AdvancedChartChannel.XStart or AdvancedChartChannel.XEnd => AdvancedChartChannel.X,
        AdvancedChartChannel.YStart or AdvancedChartChannel.YEnd or
        AdvancedChartChannel.Low or AdvancedChartChannel.Q1 or AdvancedChartChannel.Median or AdvancedChartChannel.Q3 or
        AdvancedChartChannel.High or AdvancedChartChannel.Open or AdvancedChartChannel.Close or
        AdvancedChartChannel.ErrorLow or AdvancedChartChannel.ErrorHigh or
        AdvancedChartChannel.ConfidenceLow or AdvancedChartChannel.ConfidenceHigh => AdvancedChartChannel.Y,
        _ => channel
    };

    private static void ValidateScales(List<Diagnostic> results, AdvancedChartDefinition chart, AstNode chartNode)
    {
        foreach (var scale in chart.Scales)
        {
            var node = Anchor(scale, chartNode);
            if (scale.Kind == AdvancedChartScaleKind.Logarithmic && scale.IncludeZero)
                Add(results, node, $"Logarithmic scale '{scale.Name}' cannot use INCLUDE_ZERO=ON.");
            if (scale.ExplicitOrder.Length > 0 && scale.Kind is not (AdvancedChartScaleKind.Band or AdvancedChartScaleKind.Point or AdvancedChartScaleKind.Ordinal))
                Add(results, node, $"Scale '{scale.Name}' may declare an explicit ORDER list only for categorical scales.");
            foreach (var item in scale.ExplicitOrder.Where(item => !IsConstant(item)))
                Add(results, node, $"Scale '{scale.Name}' ORDER values must be literals or parameters.");
            if (scale.Minimum is not null && !IsConstant(scale.Minimum))
                Add(results, node, $"Scale '{scale.Name}' MIN must be a literal or parameter.");
            if (scale.Maximum is not null && !IsConstant(scale.Maximum))
                Add(results, node, $"Scale '{scale.Name}' MAX must be a literal or parameter.");
            if (scale.OuterPadding is < 0m or > 1m)
                Add(results, node, $"Scale '{scale.Name}' OUTER_PADDING must be between zero and one.");
            if (scale.OuterPadding != 0m && scale.Kind != AdvancedChartScaleKind.Band)
                Add(results, node, $"Scale '{scale.Name}' OUTER_PADDING is valid only for BAND scales.");
            if (scale.TimeUnit is not null)
            {
                var tu = scale.TimeUnit.ToUpperInvariant();
                if (tu is not ("AUTO" or "DAY" or "WEEK" or "MONTH" or "QUARTER" or "YEAR"))
                    Add(results, node, $"Scale '{scale.Name}' TIME_UNIT accepts only AUTO, DAY, WEEK, MONTH, QUARTER, or YEAR; found '{scale.TimeUnit}'.");
            }
            if (scale.Title is not null && scale.Channel is not (AdvancedChartChannel.X or AdvancedChartChannel.Y or AdvancedChartChannel.Y2))
                Add(results, node, $"Scale '{scale.Name}' TITLE/LABEL is supported only on X, Y, and Y2 scales.");

            if (!scale.DiscreteColorMap.IsDefaultOrEmpty)
            {
                if (scale.Channel != AdvancedChartChannel.Color ||
                    scale.Kind is not (AdvancedChartScaleKind.Ordinal or AdvancedChartScaleKind.Band or AdvancedChartScaleKind.Point))
                {
                    Add(results, node, $"Scale '{scale.Name}' discrete RANGE mapping requires a NOMINAL/ORDINAL COLOR scale.");
                }
                else
                {
                    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in scale.DiscreteColorMap)
                    {
                        var entryNode = Anchor(entry, node);
                        if (!IsConstant(entry.Domain) || !IsConstant(entry.Color))
                        {
                            Add(results, entryNode, $"Scale '{scale.Name}' discrete RANGE entries must use literals or parameters.");
                            continue;
                        }
                        if (LiteralNumberText(entry.Domain) is { } keyText && !seenKeys.Add(keyText))
                            Add(results, entryNode, $"Scale '{scale.Name}' discrete RANGE has duplicate domain key '{keyText}'.");
                        if (LiteralText(entry.Color) is { } colorText && !IsPortableColor(colorText))
                            Add(results, entryNode, $"Scale '{scale.Name}' discrete RANGE accepts portable #RRGGBB colors only; found '{colorText}'.");
                        else if (ConstantKind(entry.Color) is { } ck && ck != LiteralKind.Text)
                            Add(results, entryNode, $"Scale '{scale.Name}' discrete RANGE requires #RRGGBB color strings.");
                    }
                }
            }

            if (!scale.RangeValues.IsDefaultOrEmpty)
            {
                if (scale.Channel == AdvancedChartChannel.Color)
                {
                    if (scale.Kind is not (AdvancedChartScaleKind.Ordinal or AdvancedChartScaleKind.Band or AdvancedChartScaleKind.Point))
                        Add(results, node, $"Scale '{scale.Name}' discrete color RANGE requires an ORDINAL, BAND, or POINT COLOR scale.");
                    else
                        foreach (var item in scale.RangeValues)
                        {
                            if (!IsConstant(item))
                                Add(results, node, $"Scale '{scale.Name}' RANGE colors must be literals or parameters.");
                            else if (LiteralText(item) is { } colorText && !IsPortableColor(colorText))
                                Add(results, node, $"Scale '{scale.Name}' RANGE accepts portable #RRGGBB colors only; found '{colorText}'.");
                            else if (ConstantKind(item) is { } ck && ck != LiteralKind.Text)
                                Add(results, node, $"Scale '{scale.Name}' discrete COLOR RANGE requires #RRGGBB color strings.");
                        }
                }
                else if (scale.Channel is AdvancedChartChannel.Size or AdvancedChartChannel.Opacity)
                {
                    if (scale.Kind is not (AdvancedChartScaleKind.Linear or AdvancedChartScaleKind.Logarithmic))
                        Add(results, node, $"Scale '{scale.Name}' numeric RANGE requires a LINEAR or LOGARITHMIC {scale.Channel} scale.");
                    else if (scale.RangeValues.Length != 2)
                        Add(results, node, $"Scale '{scale.Name}' {scale.Channel} RANGE requires exactly two numeric values (min, max).");
                    else
                    {
                        var minExpr = scale.RangeValues[0];
                        var maxExpr = scale.RangeValues[1];
                        if (!IsConstant(minExpr) || !IsConstant(maxExpr))
                            Add(results, node, $"Scale '{scale.Name}' RANGE bounds must be literals or parameters.");
                        else
                        {
                            var minOk = decimal.TryParse(LiteralNumberText(SignedNumericLiteral(minExpr) ?? minExpr), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var minVal);
                            var maxOk = decimal.TryParse(LiteralNumberText(SignedNumericLiteral(maxExpr) ?? maxExpr), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var maxVal);
                            if ((ConstantKind(SignedNumericLiteral(minExpr) ?? minExpr) is not (null or LiteralKind.Numeric)) ||
                                (ConstantKind(SignedNumericLiteral(maxExpr) ?? maxExpr) is not (null or LiteralKind.Numeric)))
                                Add(results, node, $"Scale '{scale.Name}' {scale.Channel} RANGE requires numeric bounds.");
                            else if (minOk && maxOk)
                            {
                                if (minExpr is UnaryExpression { Operator: TokenType.MINUS } || minExpr is BinaryExpression { Operator: TokenType.MINUS }) minVal = -minVal;
                                if (maxExpr is UnaryExpression { Operator: TokenType.MINUS } || maxExpr is BinaryExpression { Operator: TokenType.MINUS }) maxVal = -maxVal;
                                if (minVal < 0m || (scale.Channel == AdvancedChartChannel.Opacity && maxVal > 1m) || maxVal < minVal)
                                    Add(results, node, $"Scale '{scale.Name}' {scale.Channel} RANGE requires 0 <= min <= max{(scale.Channel == AdvancedChartChannel.Opacity ? " <= 1" : "")}.");
                            }
                        }
                    }
                }
                else
                {
                    Add(results, node, $"Scale '{scale.Name}' RANGE = (...) is supported only on COLOR, SIZE, or OPACITY scales.");
                }
            }

            if (scale.ColorRange is not { } range) continue;

            var rangeNode = Anchor(range, node);
            if (scale.Channel != AdvancedChartChannel.Color ||
                scale.Kind is not (AdvancedChartScaleKind.Linear or AdvancedChartScaleKind.Logarithmic))
                Add(results, rangeNode, $"Scale '{scale.Name}' RANGE requires a quantitative COLOR linear/logarithmic scale.");
            else if (!chart.Encodings.Concat(chart.Layers.SelectMany(layer => layer.Encodings)).Any(encoding =>
                encoding.Channel == AdvancedChartChannel.Color &&
                encoding.DataKind == AdvancedChartDataKind.Quantitative &&
                string.Equals(encoding.Scale, scale.Name, StringComparison.OrdinalIgnoreCase)))
                Add(results, rangeNode, $"Scale '{scale.Name}' RANGE requires a quantitative COLOR binding.");
            foreach (var (value, option) in new[]
                     {
                         (range.Low, "LOW"), (range.High, "HIGH"), (range.Mid, "MID"), (range.NullColor, "NULL_COLOR")
                     })
            {
                if (value is null) continue;
                if (!IsConstant(value))
                    Add(results, rangeNode, $"Scale '{scale.Name}' RANGE {option} must be a literal or parameter.");
                else if (LiteralText(value) is { } text && !IsPortableColor(text))
                    Add(results, rangeNode, $"Scale '{scale.Name}' RANGE accepts portable #RRGGBB colors only; found '{text}'.");
            }
            if (range.Midpoint is not null && !IsConstant(range.Midpoint))
                Add(results, rangeNode, $"Scale '{scale.Name}' RANGE MIDPOINT must be a literal or parameter.");
        }
    }

    private static void ValidateLayers(List<Diagnostic> results, AdvancedChartDefinition chart, AstNode chartNode)
    {
        var declared = new Dictionary<string, AdvancedChartScale>(StringComparer.OrdinalIgnoreCase);
        foreach (var scale in chart.Scales) declared[scale.Name] = scale;
        var inferred = new Dictionary<string, AdvancedChartScaleKind>(StringComparer.OrdinalIgnoreCase);

        foreach (var layer in chart.Layers)
        {
            var layerNode = Anchor(layer, chartNode);
            Duplicates(results, layer.Encodings, encoding => encoding.Channel.ToString(),
                encoding => Anchor(encoding, layerNode), $"encoding channel in layer '{layer.Name}'");
            Duplicates(results, layer.Styles, style => style.Name,
                style => Anchor(style, layerNode), $"style in layer '{layer.Name}'");

            foreach (var style in layer.Styles.Where(style => !IsConstant(style.Value)))
                Add(results, Anchor(style, layerNode), $"Layer '{layer.Name}' style '{style.Name}' must be a literal or parameter.");
            var errorStyle = layer.Styles.FirstOrDefault(s => s.Name.Equals("ERROR_BAR_STYLE", StringComparison.OrdinalIgnoreCase));
            if (errorStyle is not null)
            {
                var text = LiteralText(errorStyle.Value);
                if (text is not null && !text.Equals("CAPS", StringComparison.OrdinalIgnoreCase) && !text.Equals("NO_CAPS", StringComparison.OrdinalIgnoreCase))
                {
                    Add(results, Anchor(errorStyle, layerNode), $"Layer '{layer.Name}' ERROR_BAR_STYLE accepts only CAPS or NO_CAPS; found '{text}'.");
                }
            }

            var strokeColor = layer.Styles.FirstOrDefault(style =>
                style.Name.Equals("SYMBOL_STROKE_COLOR", StringComparison.OrdinalIgnoreCase));
            var strokeWidth = layer.Styles.FirstOrDefault(style =>
                style.Name.Equals("SYMBOL_STROKE_WIDTH", StringComparison.OrdinalIgnoreCase));
            if ((strokeColor is not null || strokeWidth is not null) && layer.Mark != AdvancedChartMarkKind.Point)
                Add(results, layerNode, $"Layer '{layer.Name}' may use SYMBOL_STROKE_COLOR and SYMBOL_STROKE_WIDTH only on POINT marks.");
            if (strokeColor is not null && ConstantKind(strokeColor.Value) is { } colorKind)
            {
                if (colorKind != LiteralKind.Text)
                    Add(results, Anchor(strokeColor, layerNode), $"Layer '{layer.Name}' SYMBOL_STROKE_COLOR must be a #RRGGBB text literal or parameter.");
                else if (LiteralText(strokeColor.Value) is { } color && !PointMarkerStroke.IsPortableColor(color))
                    Add(results, Anchor(strokeColor, layerNode), $"Layer '{layer.Name}' SYMBOL_STROKE_COLOR accepts portable #RRGGBB colors only; found '{color}'.");
            }
            if (strokeWidth is not null && ConstantKind(strokeWidth.Value) is { } widthKind)
            {
                if (widthKind != LiteralKind.Numeric)
                    Add(results, Anchor(strokeWidth, layerNode), $"Layer '{layer.Name}' SYMBOL_STROKE_WIDTH must be a non-negative number or parameter.");
                else if (!PointMarkerStroke.TryNormalizeWidth(LiteralNumberText(strokeWidth.Value), out _))
                    Add(results, Anchor(strokeWidth, layerNode), $"Layer '{layer.Name}' SYMBOL_STROKE_WIDTH must be non-negative.");
            }

            var lineWidth = layer.Styles.FirstOrDefault(style =>
                style.Name.Equals("LINE_WIDTH", StringComparison.OrdinalIgnoreCase));
            if (lineWidth is not null)
            {
                if (layer.Mark != AdvancedChartMarkKind.Line)
                    Add(results, layerNode, $"Layer '{layer.Name}' may use LINE_WIDTH only on LINE marks.");
                if (ConstantKind(lineWidth.Value) is { } lineWidthKind)
                {
                    if (lineWidthKind != LiteralKind.Numeric)
                        Add(results, Anchor(lineWidth, layerNode), $"Layer '{layer.Name}' LINE_WIDTH must be a number from {LineSeriesWidth.Minimum} through {LineSeriesWidth.Maximum}, or a parameter.");
                    else if (!LineSeriesWidth.TryNormalize(LiteralNumberText(lineWidth.Value), out _))
                        Add(results, Anchor(lineWidth, layerNode), $"Layer '{layer.Name}' LINE_WIDTH must be from {LineSeriesWidth.Minimum} through {LineSeriesWidth.Maximum} pixels.");
                }
            }

            var interpolation = layer.Styles.FirstOrDefault(style =>
                style.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase));
            if (interpolation is not null)
            {
                if (layer.Mark is not (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area))
                    Add(results, layerNode, $"Layer '{layer.Name}' may use INTERPOLATION only on LINE and AREA marks.");
                if (ConstantKind(interpolation.Value) is { } interpolationKind)
                {
                    var text = interpolationKind == LiteralKind.Text ? LiteralText(interpolation.Value) : null;
                    if (text is null || text.Trim().ToUpperInvariant() is not ("LINEAR" or "SMOOTH" or "STEP_BEFORE" or "STEP_AFTER"))
                        Add(results, Anchor(interpolation, layerNode), $"Layer '{layer.Name}' INTERPOLATION accepts only LINEAR, SMOOTH, STEP_BEFORE, or STEP_AFTER.");
                }
            }

            var layerLineDash = layer.Styles.FirstOrDefault(style =>
                style.Name.Equals("LINE_DASH", StringComparison.OrdinalIgnoreCase));
            if (layerLineDash is not null)
            {
                if (layer.Mark is not (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area))
                    Add(results, layerNode, $"Layer '{layer.Name}' may use LINE_DASH only on LINE and AREA marks.");
                if (ConstantKind(layerLineDash.Value) is { } lineDashKind)
                {
                    var text = lineDashKind == LiteralKind.Text ? LiteralText(layerLineDash.Value) : null;
                    if (text is null || text.Trim().ToUpperInvariant() is not ("SOLID" or "DASHED" or "DOTTED"))
                        Add(results, Anchor(layerLineDash, layerNode), $"Layer '{layer.Name}' LINE_DASH accepts only SOLID, DASHED, or DOTTED.");
                }
            }

            if (layer.ZIndex < 0)
                Add(results, layerNode, $"Layer '{layer.Name}' has a negative Z_INDEX.");
            if (layer.BandSize <= 0m || layer.BandSize > 1m)
                Add(results, layerNode, $"Layer '{layer.Name}' BAND_SIZE must be greater than zero and at most one.");
            if (layer.TickThickness <= 0m || layer.TickThickness > 1m)
                Add(results, layerNode, $"Layer '{layer.Name}' THICKNESS must be greater than zero and at most one em.");
            if (layer.AreaBaseline is not null)
            {
                if (layer.Mark != AdvancedChartMarkKind.Area)
                    Add(results, layerNode, $"Layer '{layer.Name}' may declare AREA_BASELINE only for AREA marks.");
                else if (!layer.AreaBaseline.Equals("ZERO", StringComparison.OrdinalIgnoreCase) &&
                         !decimal.TryParse(layer.AreaBaseline, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                    Add(results, layerNode, $"Layer '{layer.Name}' AREA_BASELINE must be ZERO or a decimal number; found '{layer.AreaBaseline}'.");
            }
            if (layer.HoverFocus is not null)
            {
                var hf = layer.HoverFocus.ToUpperInvariant();
                if (hf is not ("NONE" or "SELF" or "SERIES"))
                    Add(results, layerNode, $"Layer '{layer.Name}' HOVER_FOCUS accepts only NONE, SELF, or SERIES; found '{layer.HoverFocus}'.");
            }

            ValidatePosition(results, chart, layer, layerNode);

            var effective = EffectiveEncodings(chart, layer);
            if (effective.Count == 0)
                Add(results, layerNode, $"Layer '{layer.Name}' has no effective encodings.");

            foreach (var encoding in effective)
                ValidateEncoding(results, chart, layer, encoding, layerNode, declared, inferred);

            ValidateLayerShape(results, chart, layer, effective, layerNode);
            ValidateConditions(results, chart, layer, layerNode);
        }
    }

    private static void ValidateEncoding(
        List<Diagnostic> results,
        AdvancedChartDefinition chart,
        AdvancedChartLayer layer,
        AdvancedChartEncoding encoding,
        AstNode layerNode,
        IReadOnlyDictionary<string, AdvancedChartScale> declared,
        Dictionary<string, AdvancedChartScaleKind> inferred)
    {
        var node = Anchor(encoding, layerNode);
        var target = encoding.Scale is null ? null : declared.GetValueOrDefault(encoding.Scale);
        if (encoding.Scale is not null && target is null)
            Add(results, node, $"Layer '{layer.Name}' references undeclared scale '{encoding.Scale}'.");
        else if (target is not null)
        {
            if (!CompatibleScaleChannel(target.Channel, encoding.Channel))
                Add(results, node, $"Layer '{layer.Name}' binds {encoding.Channel} to scale '{encoding.Scale}', which is declared for a different channel.");
            else if (!CompatibleScaleKind(encoding.DataKind, target.Kind))
                Add(results, node, $"Layer '{layer.Name}' binds {encoding.DataKind} {encoding.Channel} to {target.Kind} scale '{encoding.Scale}'; the TYPE and scale kind are incompatible.");
        }

        if (encoding.Axis == AdvancedChartAxisRole.Secondary && encoding.Channel != AdvancedChartChannel.Y2)
            Add(results, node, $"Layer '{layer.Name}' may use AXIS=SECONDARY only on the Y2 channel.");

        if (encoding.Channel == AdvancedChartChannel.Opacity)
        {
            if (encoding.DataKind != AdvancedChartDataKind.Quantitative)
                Add(results, node, $"Layer '{layer.Name}' OPACITY encoding requires TYPE = QUANTITATIVE.");
            if (encoding.Source.Kind == AdvancedChartBindingSourceKind.Value && encoding.Source.Constant is { } opConst &&
                ConstantKind(SignedNumericLiteral(opConst) ?? opConst) == LiteralKind.Numeric &&
                decimal.TryParse(LiteralNumberText(SignedNumericLiteral(opConst) ?? opConst), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ov))
            {
                if (opConst is UnaryExpression { Operator: TokenType.MINUS } || opConst is BinaryExpression { Operator: TokenType.MINUS }) ov = -ov;
                if (ov < 0m || ov > 1m)
                    Add(results, node, $"Layer '{layer.Name}' OPACITY VALUE must be between 0 and 1; found {ov}.");
            }
        }

        if (encoding.Channel == AdvancedChartChannel.Shape)
        {
            if (layer.Mark != AdvancedChartMarkKind.Point)
                Add(results, node, $"Layer '{layer.Name}' may use SHAPE only on POINT marks.");
            if (encoding.Source.Kind != AdvancedChartBindingSourceKind.Field &&
                LiteralText(encoding.Source.Constant!) is { } shape && !PointShapeVocabulary.IsSupported(shape))
                Add(results, node, $"Layer '{layer.Name}' SHAPE accepts only {PointShapeVocabulary.DisplayList}; found '{shape}'.");
        }

        if (encoding.Source.Kind == AdvancedChartBindingSourceKind.Value)
        {
            if (encoding.Scale is not null)
                Add(results, node, $"Layer '{layer.Name}' uses VALUE on {encoding.Channel}; VALUE bypasses scales and cannot declare SCALE.");
            if (encoding.Axis != AdvancedChartAxisRole.None)
                Add(results, node, $"Layer '{layer.Name}' uses VALUE on {encoding.Channel}; VALUE cannot declare AXIS.");
            if (IsPositional(encoding.Channel))
                Add(results, node, $"Layer '{layer.Name}' cannot bind visual-range VALUE to positional channel {encoding.Channel}.");
        }

        if (encoding.Channel is AdvancedChartChannel.XOffset or AdvancedChartChannel.YOffset)
        {
            if (encoding.DataKind is not (AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal))
                Add(results, node, $"Layer '{layer.Name}' offset channel {encoding.Channel} requires NOMINAL or ORDINAL TYPE.");
            if (encoding.Source.Kind == AdvancedChartBindingSourceKind.Value)
                Add(results, node, $"Layer '{layer.Name}' offset channel {encoding.Channel} cannot use a visual VALUE source.");
        }

        if (encoding.Stack != AdvancedChartStackMode.None &&
            (encoding.DataKind != AdvancedChartDataKind.Quantitative ||
             encoding.Source.Kind == AdvancedChartBindingSourceKind.Value ||
             !(chart.Coordinate.Kind == AdvancedChartCoordinateKind.Polar && encoding.Channel == AdvancedChartChannel.Radius ||
               chart.Coordinate.Kind is AdvancedChartCoordinateKind.Cartesian or AdvancedChartCoordinateKind.TransposedCartesian && encoding.Channel is AdvancedChartChannel.Y or AdvancedChartChannel.Y2)))
            Add(results, node, $"Layer '{layer.Name}' STACK requires a quantitative Cartesian/transposed Y/Y2 or polar RADIUS binding.");

        if (encoding.Source.Kind != AdvancedChartBindingSourceKind.Field)
            ValidateConstantBinding(results, encoding, node, layer);

        if (encoding.Scale is not null || encoding.Source.Kind == AdvancedChartBindingSourceKind.Value)
            return;

        var kind = AdvancedChartScaleInference.Infer(encoding.Channel, encoding.DataKind, layer.Mark);
        if (kind is null)
        {
            if (encoding.Channel is AdvancedChartChannel.Longitude or AdvancedChartChannel.Latitude or AdvancedChartChannel.Region or AdvancedChartChannel.Route or
                AdvancedChartChannel.Text or AdvancedChartChannel.Tooltip or AdvancedChartChannel.Detail)
                return;
            Add(results, node, $"Layer '{layer.Name}' has no deterministic scale inference for {layer.Mark} {encoding.Channel} {encoding.DataKind}; declare a compatible scale or encoding.");
            return;
        }

        var id = InferredScaleId(chart.Coordinate.Kind, encoding.Axis, encoding.Channel);
        if (inferred.TryGetValue(id, out var existing) && existing != kind.Value)
            Add(results, node, $"Channel {encoding.Channel} requires incompatible inferred scales ({existing} and {kind.Value}); declare named scales explicitly.");
        else
            inferred[id] = kind.Value;
    }

    private static void ValidateConstantBinding(List<Diagnostic> results, AdvancedChartEncoding encoding, AstNode node, AdvancedChartLayer layer)
    {
        var source = encoding.Source.Kind.ToString().ToUpperInvariant();
        var constant = encoding.Source.Constant;
        if (constant is null || !IsConstant(constant))
        {
            Add(results, node, $"Layer '{layer.Name}' {source} on {encoding.Channel} requires a scalar literal or declared parameter.");
            return;
        }
        if (ConstantKind(constant) is not { } literal) return;
        if (literal == LiteralKind.Null)
        {
            if (encoding.Channel is not (AdvancedChartChannel.Color or AdvancedChartChannel.Text or AdvancedChartChannel.Tooltip or AdvancedChartChannel.Detail))
                Add(results, node, $"Channel {encoding.Channel} does not support a null constant binding.");
            return;
        }
        var compatible = encoding.DataKind switch
        {
            AdvancedChartDataKind.Quantitative => literal == LiteralKind.Numeric,
            AdvancedChartDataKind.Temporal => literal == LiteralKind.Temporal,
            _ => true
        };
        if (!compatible)
            Add(results, node, $"{source} value kind {literal} is incompatible with declared {encoding.DataKind} TYPE on {encoding.Channel}.");
    }

    private static void ValidatePosition(List<Diagnostic> results, AdvancedChartDefinition chart, AdvancedChartLayer layer, AstNode layerNode)
    {
        var position = layer.Position;
        if (position.Kind == AdvancedChartPositionKind.Identity) return;
        var node = Anchor(position, layerNode);
        if (position.Kind == AdvancedChartPositionKind.Jitter && string.IsNullOrWhiteSpace(position.KeyField))
            Add(results, node, $"Layer '{layer.Name}' JITTER requires a stable KEY field.");
        if (position.Kind == AdvancedChartPositionKind.Jitter && (position.X < 0m || position.Y < 0m || position.X > 1m || position.Y > 1m))
            Add(results, node, $"Layer '{layer.Name}' JITTER amplitudes must be between zero and one.");
        if (position.Kind == AdvancedChartPositionKind.Nudge && position.Unit == AdvancedChartPositionUnit.Data &&
            chart.Coordinate.Kind != AdvancedChartCoordinateKind.Cartesian &&
            chart.Coordinate is not { Kind: AdvancedChartCoordinateKind.TransposedCartesian, AspectRatio: not null } &&
            !(chart.Coordinate.Kind == AdvancedChartCoordinateKind.TransposedCartesian && chart.Layers.Any(candidate => candidate.Mark is (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area) && candidate.Conditions.Length > 0)))
            Add(results, node, $"Layer '{layer.Name}' data-domain NUDGE requires Cartesian coordinates or a supported transposed ASPECT_RATIO composition.");
    }

    private static void ValidateLayerShape(
        List<Diagnostic> results,
        AdvancedChartDefinition chart,
        AdvancedChartLayer layer,
        IReadOnlyList<AdvancedChartEncoding> effective,
        AstNode layerNode)
    {
        var stacked = effective.Where(encoding => encoding.Stack != AdvancedChartStackMode.None).ToList();
        if (stacked.Select(encoding => encoding.Stack).Distinct().Count() > 1)
            Add(results, layerNode, $"Layer '{layer.Name}' cannot declare multiple STACK modes.");
        if (stacked.Count > 0 && effective.Any(encoding => encoding.Channel is AdvancedChartChannel.XOffset or AdvancedChartChannel.YOffset))
            Add(results, layerNode, $"Layer '{layer.Name}' cannot combine STACK with an offset channel.");

        var channels = effective.Select(encoding => encoding.Channel).ToHashSet();
        switch (layer.Mark)
        {
            case AdvancedChartMarkKind.Rect:
                ValidateStatisticalRect(results, layer, effective, channels, layerNode);
                // A ranged rectangle owns its own extent on an axis, so the author supplies both
                // endpoints and nothing else that would also claim that axis.
                ValidateIntervalPair(results, layer, effective, AdvancedChartChannel.XStart, AdvancedChartChannel.XEnd, "X_START/X_END", layerNode);
                ValidateIntervalPair(results, layer, effective, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd, "Y_START/Y_END", layerNode);
                if (channels.Contains(AdvancedChartChannel.YStart) &&
                    (channels.Contains(AdvancedChartChannel.Y) || channels.Contains(AdvancedChartChannel.Y2)))
                    Add(results, layerNode, $"RECT layer '{layer.Name}' cannot combine Y or Y2 with Y_START/Y_END; the interval endpoints are the rectangle's extent.");
                if (channels.Contains(AdvancedChartChannel.XStart) &&
                    (channels.Contains(AdvancedChartChannel.X) || channels.Contains(AdvancedChartChannel.X2)))
                    Add(results, layerNode, $"RECT layer '{layer.Name}' cannot combine X or X2 with X_START/X_END; the interval endpoints are the rectangle's extent.");
                break;
            case AdvancedChartMarkKind.Area:
                var hasStart = channels.Contains(AdvancedChartChannel.YStart);
                var hasEnd = channels.Contains(AdvancedChartChannel.YEnd);
                if (hasStart != hasEnd)
                    Add(results, layerNode, $"AREA layer '{layer.Name}' requires both Y_START and Y_END for a floating ribbon.");
                else if (hasStart && channels.Contains(AdvancedChartChannel.Y))
                    Add(results, layerNode, $"AREA layer '{layer.Name}' cannot combine Y with Y_START/Y_END.");
                else if (hasStart)
                {
                    ValidateIntervalTypes(results, effective, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd, layerNode,
                        $"AREA layer '{layer.Name}' ribbon endpoints require matching quantitative or temporal types.");
                    var start = effective.First(encoding => encoding.Channel == AdvancedChartChannel.YStart);
                    var end = effective.First(encoding => encoding.Channel == AdvancedChartChannel.YEnd);
                    // COMPAT_BREAK: 0.20.0 — ribbon bounds must share their effective Y scale.
                    if (!EffectiveScaleId(chart.Coordinate.Kind, start).Equals(EffectiveScaleId(chart.Coordinate.Kind, end), StringComparison.OrdinalIgnoreCase))
                        Add(results, Anchor(end, layerNode), $"AREA layer '{layer.Name}' ribbon endpoints must use the same Y scale.");
                }
                break;
            case AdvancedChartMarkKind.Rule:
                ValidateIntervalPair(results, layer, effective, AdvancedChartChannel.XStart, AdvancedChartChannel.XEnd, "X_START/X_END", layerNode);
                ValidateIntervalPair(results, layer, effective, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd, "Y_START/Y_END", layerNode);
                if (channels.Contains(AdvancedChartChannel.X2) && !channels.Contains(AdvancedChartChannel.X))
                    Add(results, layerNode, $"RULE layer '{layer.Name}' requires X with X2.");
                if (channels.Contains(AdvancedChartChannel.Y2) && !channels.Contains(AdvancedChartChannel.Y))
                    Add(results, layerNode, $"RULE layer '{layer.Name}' requires Y with Y2.");
                break;
            case AdvancedChartMarkKind.Tick:
                var x = effective.FirstOrDefault(encoding => encoding.Channel == AdvancedChartChannel.X);
                var y = effective.FirstOrDefault(encoding => encoding.Channel == AdvancedChartChannel.Y);
                if (x?.DataKind is not (AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal) ||
                    y?.DataKind != AdvancedChartDataKind.Quantitative)
                    Add(results, layerNode, $"TICK layer '{layer.Name}' requires a nominal/ordinal X encoding and a quantitative Y encoding.");
                break;
            case AdvancedChartMarkKind.Arc when chart.Coordinate.Kind != AdvancedChartCoordinateKind.Polar:
                Add(results, layerNode, "ARC layers require POLAR coordinates.");
                break;
        }

        ValidateErrorBars(results, chart, layer, effective, channels, layerNode);
        ValidateConfidenceChannels(results, chart, layer, effective, channels, layerNode);

        if (chart.Coordinate.Kind == AdvancedChartCoordinateKind.Polar && layer.Mark != AdvancedChartMarkKind.Arc)
            Add(results, layerNode, "The native POLAR slice supports ARC layers only.");
        if (chart.Coordinate.Kind == AdvancedChartCoordinateKind.Geographic)
        {
            var hasPoint = channels.Contains(AdvancedChartChannel.Longitude) && channels.Contains(AdvancedChartChannel.Latitude);
            var hasRegion = channels.Contains(AdvancedChartChannel.Region);
            if (layer.Mark == AdvancedChartMarkKind.Rect && !hasRegion)
                Add(results, layerNode, $"Geographic RECT layer '{layer.Name}' requires REGION.");
            else if (layer.Mark is AdvancedChartMarkKind.Point or AdvancedChartMarkKind.Text && !hasPoint)
                Add(results, layerNode, $"Geographic {layer.Mark.ToString().ToUpperInvariant()} layer '{layer.Name}' requires LONGITUDE and LATITUDE.");
            else if (layer.Mark == AdvancedChartMarkKind.Line && (!hasPoint || !channels.Contains(AdvancedChartChannel.Route)))
                Add(results, layerNode, $"Geographic LINE layer '{layer.Name}' requires LONGITUDE, LATITUDE, and ROUTE.");
            else if (layer.Mark is not (AdvancedChartMarkKind.Rect or AdvancedChartMarkKind.Point or AdvancedChartMarkKind.Text or AdvancedChartMarkKind.Line))
                Add(results, layerNode, $"GEOGRAPHIC coordinates support RECT, POINT, TEXT, and LINE layers only; found {layer.Mark.ToString().ToUpperInvariant()}.");
        }
    }

    private static void ValidateErrorBars(
        List<Diagnostic> results,
        AdvancedChartDefinition chart,
        AdvancedChartLayer layer,
        IReadOnlyList<AdvancedChartEncoding> effective,
        HashSet<AdvancedChartChannel> channels,
        AstNode layerNode)
    {
        var hasLow = channels.Contains(AdvancedChartChannel.ErrorLow);
        var hasHigh = channels.Contains(AdvancedChartChannel.ErrorHigh);
        if (!hasLow && !hasHigh) return;

        var markUpper = layer.Mark.ToString().ToUpperInvariant();
        if (layer.Mark is not (AdvancedChartMarkKind.Point or AdvancedChartMarkKind.Rect))
        {
            Add(results, layerNode, $"{markUpper} layer '{layer.Name}' does not support error bar channels; only POINT and RECT marks support error bars.");
        }

        if (chart.Coordinate.Kind is not (AdvancedChartCoordinateKind.Cartesian or AdvancedChartCoordinateKind.TransposedCartesian))
        {
            Add(results, layerNode, $"Layer '{layer.Name}' error bar channels ERROR_LOW and ERROR_HIGH require CARTESIAN or TRANSPOSED_CARTESIAN coordinates.");
        }

        if (!hasLow || !hasHigh)
        {
            Add(results, layerNode, $"{markUpper} layer '{layer.Name}' requires both ERROR_LOW and ERROR_HIGH as a pair.");
        }
        else
        {
            var lowEnc = effective.First(e => e.Channel == AdvancedChartChannel.ErrorLow);
            var highEnc = effective.First(e => e.Channel == AdvancedChartChannel.ErrorHigh);
            if (lowEnc.DataKind != AdvancedChartDataKind.Quantitative)
                Add(results, Anchor(lowEnc, layerNode), $"{markUpper} layer '{layer.Name}' channel ERROR_LOW requires QUANTITATIVE TYPE.");
            if (highEnc.DataKind != AdvancedChartDataKind.Quantitative)
                Add(results, Anchor(highEnc, layerNode), $"{markUpper} layer '{layer.Name}' channel ERROR_HIGH requires QUANTITATIVE TYPE.");

            var yEnc = effective.FirstOrDefault(e => e.Channel == AdvancedChartChannel.Y);
            if (yEnc is null || yEnc.DataKind != AdvancedChartDataKind.Quantitative)
            {
                Add(results, layerNode, $"{markUpper} layer '{layer.Name}' with error bars requires a quantitative Y encoding.");
            }
            else
            {
                if (yEnc.Axis == AdvancedChartAxisRole.Secondary)
                    Add(results, Anchor(yEnc, layerNode), $"{markUpper} layer '{layer.Name}' Y encoding with error bars must use the primary axis.");
                if (lowEnc.Axis == AdvancedChartAxisRole.Secondary)
                    Add(results, Anchor(lowEnc, layerNode), $"{markUpper} layer '{layer.Name}' ERROR_LOW encoding must use the primary axis.");
                if (highEnc.Axis == AdvancedChartAxisRole.Secondary)
                    Add(results, Anchor(highEnc, layerNode), $"{markUpper} layer '{layer.Name}' ERROR_HIGH encoding must use the primary axis.");

                var yScale = EffectiveScaleId(chart.Coordinate.Kind, yEnc);
                var lowScale = EffectiveScaleId(chart.Coordinate.Kind, lowEnc);
                var highScore = EffectiveScaleId(chart.Coordinate.Kind, highEnc);

                if (!string.Equals(lowScale, yScale, StringComparison.OrdinalIgnoreCase))
                    Add(results, Anchor(lowEnc, layerNode), $"{markUpper} layer '{layer.Name}' ERROR_LOW must resolve to the same scale as Y ('{yScale}'); found '{lowScale}'.");
                if (!string.Equals(highScore, yScale, StringComparison.OrdinalIgnoreCase))
                    Add(results, Anchor(highEnc, layerNode), $"{markUpper} layer '{layer.Name}' ERROR_HIGH must resolve to the same scale as Y ('{yScale}'); found '{highScore}'.");
            }
        }

        if (layer.Mark == AdvancedChartMarkKind.Rect)
        {
            if (channels.Contains(AdvancedChartChannel.YStart) || channels.Contains(AdvancedChartChannel.XStart))
                Add(results, layerNode, $"RECT layer '{layer.Name}' cannot combine error bars with ranged rectangle channels (Y_START/Y_END, X_START/X_END).");
            if (channels.Contains(AdvancedChartChannel.Low) || channels.Contains(AdvancedChartChannel.Q1) ||
                channels.Contains(AdvancedChartChannel.Median) || channels.Contains(AdvancedChartChannel.Q3) ||
                channels.Contains(AdvancedChartChannel.High) || channels.Contains(AdvancedChartChannel.Open) ||
                channels.Contains(AdvancedChartChannel.Close))
                Add(results, layerNode, $"RECT layer '{layer.Name}' cannot combine error bars with box-plot or candlestick channels.");
        }
    }

    private static void ValidateConfidenceChannels(
        List<Diagnostic> results,
        AdvancedChartDefinition chart,
        AdvancedChartLayer layer,
        IReadOnlyList<AdvancedChartEncoding> effective,
        HashSet<AdvancedChartChannel> channels,
        AstNode layerNode)
    {
        var hasLow = channels.Contains(AdvancedChartChannel.ConfidenceLow);
        var hasHigh = channels.Contains(AdvancedChartChannel.ConfidenceHigh);
        if (!hasLow && !hasHigh) return;

        var markUpper = layer.Mark.ToString().ToUpperInvariant();
        if (layer.Mark != AdvancedChartMarkKind.Area)
        {
            Add(results, layerNode, $"{markUpper} layer '{layer.Name}' does not support confidence channels; only AREA marks support confidence channels.");
        }

        if (chart.Coordinate.Kind is not (AdvancedChartCoordinateKind.Cartesian or AdvancedChartCoordinateKind.TransposedCartesian))
        {
            Add(results, layerNode, $"Layer '{layer.Name}' confidence channels CONFIDENCE_LOW and CONFIDENCE_HIGH require CARTESIAN or TRANSPOSED_CARTESIAN coordinates.");
        }

        if (!hasLow || !hasHigh)
        {
            Add(results, layerNode, $"{markUpper} layer '{layer.Name}' requires both CONFIDENCE_LOW and CONFIDENCE_HIGH as a pair.");
        }
        else
        {
            var lowEnc = effective.First(e => e.Channel == AdvancedChartChannel.ConfidenceLow);
            var highEnc = effective.First(e => e.Channel == AdvancedChartChannel.ConfidenceHigh);
            if (lowEnc.DataKind != AdvancedChartDataKind.Quantitative)
                Add(results, Anchor(lowEnc, layerNode), $"{markUpper} layer '{layer.Name}' channel CONFIDENCE_LOW requires QUANTITATIVE TYPE.");
            if (highEnc.DataKind != AdvancedChartDataKind.Quantitative)
                Add(results, Anchor(highEnc, layerNode), $"{markUpper} layer '{layer.Name}' channel CONFIDENCE_HIGH requires QUANTITATIVE TYPE.");

            if (lowEnc.Axis == AdvancedChartAxisRole.Secondary)
                Add(results, Anchor(lowEnc, layerNode), $"{markUpper} layer '{layer.Name}' CONFIDENCE_LOW encoding must use the primary axis.");
            if (highEnc.Axis == AdvancedChartAxisRole.Secondary)
                Add(results, Anchor(highEnc, layerNode), $"{markUpper} layer '{layer.Name}' CONFIDENCE_HIGH encoding must use the primary axis.");

            var lowScale = EffectiveScaleId(chart.Coordinate.Kind, lowEnc);
            var highScore = EffectiveScaleId(chart.Coordinate.Kind, highEnc);
            if (!string.Equals(lowScale, highScore, StringComparison.OrdinalIgnoreCase))
                Add(results, Anchor(highEnc, layerNode), $"{markUpper} layer '{layer.Name}' CONFIDENCE_LOW and CONFIDENCE_HIGH must resolve to the same scale ID; found '{lowScale}' and '{highScore}'.");
        }

        if (!channels.Contains(AdvancedChartChannel.X))
        {
            Add(results, layerNode, $"{markUpper} layer '{layer.Name}' with confidence channels requires an X encoding.");
        }

        if (channels.Contains(AdvancedChartChannel.Y) || channels.Contains(AdvancedChartChannel.Y2) ||
            channels.Contains(AdvancedChartChannel.YStart) || channels.Contains(AdvancedChartChannel.YEnd))
        {
            Add(results, layerNode, $"AREA layer '{layer.Name}' cannot combine CONFIDENCE_LOW/CONFIDENCE_HIGH with Y, Y2, Y_START, or Y_END; confidence endpoints own the band's extent.");
        }
    }

    private static void ValidateStatisticalRect(
        List<Diagnostic> results,
        AdvancedChartLayer layer,
        IReadOnlyList<AdvancedChartEncoding> effective,
        HashSet<AdvancedChartChannel> channels,
        AstNode layerNode)
    {
        var boxChannels = new[]
        {
            AdvancedChartChannel.Low, AdvancedChartChannel.Q1, AdvancedChartChannel.Median,
            AdvancedChartChannel.Q3, AdvancedChartChannel.High
        };
        var candleChannels = new[]
        {
            AdvancedChartChannel.Open, AdvancedChartChannel.Close,
            AdvancedChartChannel.Low, AdvancedChartChannel.High
        };
        var hasQuartile = channels.Contains(AdvancedChartChannel.Q1) ||
            channels.Contains(AdvancedChartChannel.Median) || channels.Contains(AdvancedChartChannel.Q3);
        var hasFinancial = channels.Contains(AdvancedChartChannel.Open) || channels.Contains(AdvancedChartChannel.Close);
        if (!hasQuartile && !hasFinancial) return;

        if (hasQuartile && hasFinancial)
        {
            Add(results, layerNode, $"RECT layer '{layer.Name}' cannot mix box-plot and candlestick channels.");
            return;
        }

        var required = hasQuartile ? boxChannels : candleChannels;
        var shape = hasQuartile ? "box plot" : "candlestick";
        var missing = required.Where(channel => !channels.Contains(channel)).ToArray();
        if (missing.Length > 0)
            Add(results, layerNode, $"RECT layer '{layer.Name}' {shape} requires {string.Join(", ", required.Select(Upper))}; missing {string.Join(", ", missing.Select(Upper))}.");
        if (!channels.Contains(AdvancedChartChannel.X))
            Add(results, layerNode, $"RECT layer '{layer.Name}' {shape} requires an X category encoding.");
        foreach (var encoding in effective.Where(encoding => required.Contains(encoding.Channel)))
            if (encoding.DataKind != AdvancedChartDataKind.Quantitative)
                Add(results, Anchor(encoding, layerNode), $"RECT layer '{layer.Name}' {shape} channel {Upper(encoding.Channel)} requires QUANTITATIVE TYPE.");
    }

    private static string Upper(AdvancedChartChannel channel) => channel switch
    {
        AdvancedChartChannel.XStart => "X_START",
        AdvancedChartChannel.XEnd => "X_END",
        AdvancedChartChannel.XOffset => "X_OFFSET",
        AdvancedChartChannel.YStart => "Y_START",
        AdvancedChartChannel.YEnd => "Y_END",
        AdvancedChartChannel.YOffset => "Y_OFFSET",
        AdvancedChartChannel.ErrorLow => "ERROR_LOW",
        AdvancedChartChannel.ErrorHigh => "ERROR_HIGH",
        _ => channel.ToString().ToUpperInvariant()
    };

    private static void ValidateIntervalPair(
        List<Diagnostic> results,
        AdvancedChartLayer layer,
        IReadOnlyList<AdvancedChartEncoding> effective,
        AdvancedChartChannel start,
        AdvancedChartChannel end,
        string name,
        AstNode layerNode)
    {
        var mark = layer.Mark.ToString().ToUpperInvariant();
        var first = effective.FirstOrDefault(encoding => encoding.Channel == start);
        var second = effective.FirstOrDefault(encoding => encoding.Channel == end);
        if ((first is null) != (second is null))
        {
            Add(results, layerNode, $"{mark} layer '{layer.Name}' requires both endpoints in {name}.");
            return;
        }
        if (first is null) return;
        ValidateIntervalTypes(results, effective, start, end, layerNode,
            $"{mark} layer '{layer.Name}' interval {name} requires matching quantitative or temporal endpoint types.");
    }

    private static void ValidateIntervalTypes(
        List<Diagnostic> results,
        IReadOnlyList<AdvancedChartEncoding> effective,
        AdvancedChartChannel start,
        AdvancedChartChannel end,
        AstNode layerNode,
        string message)
    {
        var first = effective.First(encoding => encoding.Channel == start);
        var second = effective.First(encoding => encoding.Channel == end);
        if (first.DataKind != second.DataKind ||
            first.DataKind is not (AdvancedChartDataKind.Quantitative or AdvancedChartDataKind.Temporal))
            Add(results, layerNode, message);
    }

    private static void ValidateConditions(List<Diagnostic> results, AdvancedChartDefinition chart, AdvancedChartLayer layer, AstNode layerNode)
    {
        var bindings = EffectiveEncodings(chart, layer);
        var positions = bindings.Where(binding => binding.Channel != AdvancedChartChannel.Color).ToArray();
        var composition = chart.Layers.Any(candidate => candidate.Mark is (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area) && candidate.Conditions.Length > 0) &&
            (chart.Coordinate.Kind == AdvancedChartCoordinateKind.TransposedCartesian || chart.Layers.Length > 1 || chart.Facet is not null || chart.Layers.Any(candidate => candidate.Mark is (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area) && candidate.Conditions.Length > 0 && (candidate.Styles.Any(style => style.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase) && LiteralText(style.Value)?.Trim().ToUpperInvariant() is "SMOOTH" or "STEP_BEFORE" or "STEP_AFTER") || candidate.Conditions.Any(condition => condition.Channel is AdvancedChartConditionChannel.Size or AdvancedChartConditionChannel.Shape or AdvancedChartConditionChannel.Text) || string.Equals(candidate.NullHandling, "ZERO", StringComparison.OrdinalIgnoreCase) || EffectiveEncodings(chart, candidate).Any(binding => binding.Channel == AdvancedChartChannel.Color))));
        var ribbon = layer.Mark == AdvancedChartMarkKind.Area && positions.Select(binding => binding.Channel).ToHashSet()
            .SetEquals([AdvancedChartChannel.X, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd]);
        var sharedAxes = true;
        if (composition)
        {
            foreach (var axis in new[] { AdvancedChartChannel.X, AdvancedChartChannel.Y })
            {
                var positional = chart.Layers.SelectMany(candidate => EffectiveEncodings(chart, candidate)).Where(binding => BaseScaleChannel(binding.Channel) == axis).ToArray();
                var ids = positional.Select(binding => EffectiveScaleId(chart.Coordinate.Kind, binding)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var scale = ids.Length == 1 ? chart.Scales.FirstOrDefault(scale => scale.Name.Equals(ids[0], StringComparison.OrdinalIgnoreCase)) : null;
                sharedAxes &= ids.Length == 1 && positional.All(binding => binding.DataKind == AdvancedChartDataKind.Quantitative) &&
                    (scale is null || scale.Channel == axis && scale.Kind == AdvancedChartScaleKind.Linear);
            }
            sharedAxes &= !chart.Layers.SelectMany(candidate => EffectiveEncodings(chart, candidate)).Any(binding => binding.Stack != AdvancedChartStackMode.None || binding.Channel == AdvancedChartChannel.Y2 || binding.Axis == AdvancedChartAxisRole.Secondary);
        }
        var connectedLine = layer.Mark is (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area) &&
            (layer.Mark != AdvancedChartMarkKind.Area || (ribbon ? layer.AreaBaseline is null : string.Equals(layer.AreaBaseline, "ZERO", StringComparison.OrdinalIgnoreCase))) &&
            chart.Coordinate.Kind is (AdvancedChartCoordinateKind.Cartesian or AdvancedChartCoordinateKind.TransposedCartesian) && sharedAxes &&
            layer.Position.Kind == AdvancedChartPositionKind.Identity &&
            (string.Equals(layer.NullHandling, "GAP", StringComparison.OrdinalIgnoreCase) || string.Equals(layer.NullHandling, "CONNECT", StringComparison.OrdinalIgnoreCase) || string.Equals(layer.NullHandling, "ZERO", StringComparison.OrdinalIgnoreCase)) &&
            layer.Styles.Any(style => style.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase) &&
                LiteralText(style.Value)?.Trim().ToUpperInvariant() is "LINEAR" or "SMOOTH" or "STEP_BEFORE" or "STEP_AFTER") &&
            (ribbon || positions.Select(binding => binding.Channel).ToHashSet().SetEquals([AdvancedChartChannel.X, AdvancedChartChannel.Y])) &&
            positions.All(binding => binding.DataKind == AdvancedChartDataKind.Quantitative && binding.Stack == AdvancedChartStackMode.None &&
                binding.Source.Kind is AdvancedChartBindingSourceKind.Field or AdvancedChartBindingSourceKind.Datum) &&
            bindings.Where(binding => binding.Channel == AdvancedChartChannel.Color).All(binding => binding.DataKind is AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal && binding.Source.Kind == AdvancedChartBindingSourceKind.Field) &&
            (composition || chart.Scales.All(scale => scale.Kind == AdvancedChartScaleKind.Linear));
        if (connectedLine && !ribbon && string.Equals(layer.NullHandling, "ZERO", StringComparison.OrdinalIgnoreCase))
        {
            var yBinding = bindings.First(binding => binding.Channel == AdvancedChartChannel.Y);
            var yScale = chart.Scales.FirstOrDefault(scale => scale.Name.Equals(EffectiveScaleId(chart.Coordinate.Kind, yBinding), StringComparison.OrdinalIgnoreCase));
            if (yScale is not null &&
                (yScale.Minimum is { } minimum && decimal.TryParse(LiteralNumberText(minimum), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var min) && min > 0m ||
                 yScale.Maximum is { } maximum && decimal.TryParse(LiteralNumberText(maximum), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var max) && max < 0m))
                Add(results, Anchor(yScale, layerNode), "Connected ZERO scalar Y requires a linear Y domain containing zero.");
        }
        var isNonQuantitativeX = positions.Any(binding =>
            BaseScaleChannel(binding.Channel) == AdvancedChartChannel.X &&
            binding.DataKind is AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal or AdvancedChartDataKind.Temporal);
        var hasInterpolationStyle = layer.Styles.Any(style => style.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase));
        var hasConnectedNullHandling = string.Equals(layer.NullHandling, "CONNECT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer.NullHandling, "ZERO", StringComparison.OrdinalIgnoreCase);
        var ordinaryConditionedLineOrArea = layer.Mark is (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area) &&
            !layer.Conditions.IsDefaultOrEmpty &&
            chart.Coordinate.Kind == AdvancedChartCoordinateKind.Cartesian &&
            !hasConnectedNullHandling &&
            (!hasInterpolationStyle && isNonQuantitativeX ||
             !hasInterpolationStyle && positions.All(b => BaseScaleChannel(b.Channel) is AdvancedChartChannel.X or AdvancedChartChannel.Y) ||
             isNonQuantitativeX && (!hasInterpolationStyle || string.Equals(LiteralText(layer.Styles.First(s => s.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase)).Value)?.Trim(), "LINEAR", StringComparison.OrdinalIgnoreCase))) &&
            bindings.Any(b => BaseScaleChannel(b.Channel) == AdvancedChartChannel.Y && b.DataKind == AdvancedChartDataKind.Quantitative);
        foreach (var condition in layer.Conditions)
        {
            var node = Anchor(condition, layerNode);
            if (layer.Mark is AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area &&
                (!ordinaryConditionedLineOrArea && (!connectedLine || !Enum.IsDefined(condition.Channel))))
                Add(results, node, $"Layer '{layer.Name}' cannot use these CONDITIONS on connected {layer.Mark.ToString().ToUpperInvariant()} marks. Connected CONDITIONS require Cartesian or transposed Cartesian LINE or AREA, quantitative unstacked X/Y, shared linear primary scales, optional categorical COLOR series, IDENTITY, NULL_HANDLING = GAP, CONNECT or ZERO, INTERPOLATION = LINEAR, SMOOTH, STEP_BEFORE or STEP_AFTER, COLOR/OPACITY connections and SIZE/SHAPE/TEXT row decorations; AREA requires AREA_BASELINE = ZERO or X/Y_START/Y_END with no baseline.");
            if (!IsSupportedPredicate(condition.Predicate))
                Add(results, node, $"Layer '{layer.Name}' condition predicate supports only fields, parameters, literals, comparisons, AND/OR/NOT, and IS NULL.");
            bool IsResultConstant(Expression value) => IsConstant(value) || connectedLine && condition.Channel == AdvancedChartConditionChannel.Size &&
                SignedNumericLiteral(value) is { } literal && ConstantKind(literal) == LiteralKind.Numeric;
            if (!IsResultConstant(condition.WhenTrue))
                Add(results, node, $"Layer '{layer.Name}' condition THEN value must be a literal or parameter.");
            if (condition.WhenFalse is not null && !IsResultConstant(condition.WhenFalse))
                Add(results, node, $"Layer '{layer.Name}' condition ELSE value must be a literal or parameter.");
            if (condition.Channel == AdvancedChartConditionChannel.Shape)
            {
                if (layer.Mark != AdvancedChartMarkKind.Point && !(connectedLine && !ordinaryConditionedLineOrArea))
                    Add(results, node, $"Layer '{layer.Name}' may condition SHAPE only on POINT marks.");
                ValidateConditionShape(condition.WhenTrue, "THEN");
                if (condition.WhenFalse is not null) ValidateConditionShape(condition.WhenFalse, "ELSE");
            }
            if (condition.Channel == AdvancedChartConditionChannel.LineDash)
            {
                if (layer.Mark is not (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area))
                    Add(results, node, $"Layer '{layer.Name}' may condition LINE_DASH only on LINE and AREA marks.");
                ValidateConditionLineDash(condition.WhenTrue, "THEN");
                if (condition.WhenFalse is not null) ValidateConditionLineDash(condition.WhenFalse, "ELSE");
            }

            if (connectedLine)
            {
                ValidateDecorationValue(condition.WhenTrue);
                if (condition.WhenFalse is not null) ValidateDecorationValue(condition.WhenFalse);
            }

            void ValidateDecorationValue(Expression value)
            {
                var kind = ConstantKind(SignedNumericLiteral(value) ?? value);
                if (condition.Channel == AdvancedChartConditionChannel.Size && kind is not (null or LiteralKind.Numeric or LiteralKind.Null))
                    Add(results, node, "Connected CONDITIONS SIZE requires a numeric or null literal or parameter.");
                if (condition.Channel == AdvancedChartConditionChannel.Shape && kind is not (null or LiteralKind.Text or LiteralKind.Null))
                    Add(results, node, "Connected CONDITIONS SHAPE requires a portable shape name or null.");
            }

            void ValidateConditionShape(Expression value, string branch)
            {
                if (LiteralText(value) is { } shape && !PointShapeVocabulary.IsSupported(shape))
                    Add(results, node, connectedLine ? $"Connected CONDITIONS {branch} SHAPE accepts only {PointShapeVocabulary.DisplayList}; found '{shape}'." : $"Layer '{layer.Name}' condition {branch} SHAPE accepts only {PointShapeVocabulary.DisplayList}; found '{shape}'.");
            }

            void ValidateConditionLineDash(Expression value, string branch)
            {
                var kind = ConstantKind(value);
                if (kind is LiteralKind.Numeric or LiteralKind.Boolean)
                    Add(results, node, $"Layer '{layer.Name}' condition {branch} LINE_DASH requires SOLID, DASHED, DOTTED, or null.");
                else if (LiteralText(value) is { } dash && dash.Trim().ToUpperInvariant() is not ("SOLID" or "DASHED" or "DOTTED"))
                    Add(results, node, $"Layer '{layer.Name}' condition {branch} LINE_DASH accepts only SOLID, DASHED, or DOTTED; found '{dash}'.");
            }
        }
    }

    private static void ValidateCoordinate(List<Diagnostic> results, AdvancedChartDefinition chart, AstNode chartNode)
    {
        var coordinate = chart.Coordinate;
        var node = Anchor(coordinate, chartNode);
        if (coordinate.Kind == AdvancedChartCoordinateKind.Polar)
        {
            var encodings = chart.Encodings.Concat(chart.Layers.SelectMany(layer => layer.Encodings)).ToList();
            if (!encodings.Any(encoding => encoding.Channel == AdvancedChartChannel.Theta) ||
                !encodings.Any(encoding => encoding.Channel == AdvancedChartChannel.Radius))
                Add(results, node, "POLAR charts require THETA and RADIUS encodings.");
            if (encodings.Any(encoding => encoding.Stack != AdvancedChartStackMode.None))
            {
                if (chart.Facet is not null || chart.Layers.Any(layer => layer.Position.Kind != AdvancedChartPositionKind.Identity || layer.Conditions.Length > 0))
                    Add(results, node, "Radial stacking requires IDENTITY, no CONDITIONS, and no FACET.");
                if ((coordinate.EndAngle ?? (coordinate.StartAngle ?? 0m) + 360m) - (coordinate.StartAngle ?? 0m) is <= 0m or > 360m)
                    Add(results, node, "Radial stacking requires an angular sweep greater than zero and at most 360 degrees.");
                if (chart.Scales.Any(scale => scale.Channel == AdvancedChartChannel.Radius && (scale.Kind != AdvancedChartScaleKind.Linear || scale.Minimum is not null || scale.Maximum is not null || scale.Reverse)))
                    Add(results, node, "Radial stacking owns the zero-based linear RADIUS domain; custom bounds or reversal are not supported.");
                foreach (var layer in chart.Layers)
                {
                    var bindings = EffectiveEncodings(chart, layer);
                    if (bindings.FirstOrDefault(binding => binding.Channel == AdvancedChartChannel.Theta)?.DataKind is not (AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal) ||
                        bindings.FirstOrDefault(binding => binding.Channel == AdvancedChartChannel.Radius)?.Stack is not (AdvancedChartStackMode.Zero or AdvancedChartStackMode.Normalize) ||
                        bindings.Any(binding => binding.Channel is not (AdvancedChartChannel.Theta or AdvancedChartChannel.Radius or AdvancedChartChannel.Color)) ||
                        bindings.Any(binding => binding.Source.Kind == AdvancedChartBindingSourceKind.Value || binding.Channel == AdvancedChartChannel.Color && binding.DataKind is not (AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal)))
                        Add(results, Anchor(layer, chartNode), "Radial stacking requires categorical THETA and stacked quantitative RADIUS, with optional nominal COLOR only.");
                }
                if (encodings.Where(encoding => encoding.Stack != AdvancedChartStackMode.None).Select(encoding => encoding.Stack).Distinct().Count() != 1)
                    Add(results, node, "Radial stacking requires one shared STACK mode.");
            }
            if (coordinate.InnerRadius is < 0m or >= 1m)
                Add(results, node, "Polar INNER_RADIUS must be at least zero and less than one.");
        }
        if (coordinate.Kind == AdvancedChartCoordinateKind.Geographic)
        {
            if (coordinate.MapName is null == (coordinate.MapFile is null))
                Add(results, node, "GEOGRAPHIC coordinates require exactly one of MAP_NAME or MAP_FILE.");
            if (coordinate.Projection is null)
                Add(results, node, "GEOGRAPHIC coordinates require PROJECTION.");
            if (string.IsNullOrWhiteSpace(coordinate.FeatureKey))
                Add(results, node, "GEOGRAPHIC FEATURE_KEY cannot be empty.");
            if (chart.Facet is not null)
                Add(results, node, "GEOGRAPHIC coordinates do not support FACET.");
        }
        else if (coordinate.Projection is not null || coordinate.MapName is not null || coordinate.MapFile is not null || coordinate.FeatureKey is not null)
            Add(results, node, "PROJECTION, MAP_NAME, MAP_FILE, and FEATURE_KEY require GEOGRAPHIC coordinates.");
        var transposedConnected = coordinate.Kind == AdvancedChartCoordinateKind.TransposedCartesian && chart.Layers.Any(layer => layer.Mark is (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area) && layer.Conditions.Length > 0);
        var transposedCompositionLabel = transposedConnected ? "TRANSPOSED_CARTESIAN connected composition" : "TRANSPOSED_CARTESIAN ASPECT_RATIO";
        if (coordinate.AspectRatio is null && !transposedConnected) return;
        if (coordinate.AspectRatio <= 0m)
            Add(results, node, "Cartesian ASPECT_RATIO must be greater than zero.");
        if (coordinate.Kind is not (AdvancedChartCoordinateKind.Cartesian or AdvancedChartCoordinateKind.TransposedCartesian))
            Add(results, node, "ASPECT_RATIO requires CARTESIAN or TRANSPOSED_CARTESIAN coordinates.");
        else if (coordinate.Kind == AdvancedChartCoordinateKind.TransposedCartesian && chart.Layers.Any(layer => layer.Mark == AdvancedChartMarkKind.Area || layer.Mark == AdvancedChartMarkKind.Line && (layer.Conditions.Length > 0 || layer.Styles.Any(style => style.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase) && LiteralText(style.Value)?.Trim().ToUpperInvariant() is ("SMOOTH" or "STEP_BEFORE" or "STEP_AFTER")))))
        {
            foreach (var axis in new[] { AdvancedChartChannel.X, AdvancedChartChannel.Y })
            {
                var encodings = chart.Layers.SelectMany(layer => EffectiveEncodings(chart, layer)).Where(encoding => BaseScaleChannel(encoding.Channel) == axis).ToArray();
                var ids = encodings.Select(encoding => EffectiveScaleId(coordinate.Kind, encoding)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var scale = ids.Length == 1 ? chart.Scales.FirstOrDefault(scale => scale.Name.Equals(ids[0], StringComparison.OrdinalIgnoreCase)) : null;
                if (ids.Length != 1 || encodings.Any(encoding => encoding.DataKind != AdvancedChartDataKind.Quantitative) ||
                    scale is not null && (scale.Channel != axis || scale.Kind is not (AdvancedChartScaleKind.Linear or AdvancedChartScaleKind.Logarithmic)))
                    Add(results, node, $"{transposedCompositionLabel} AREA compositions require one shared quantitative primary scale per X/Y axis.");
            }
        }
        else if (!ContinuousPositionalScale(chart, AdvancedChartChannel.X) || !ContinuousPositionalScale(chart, AdvancedChartChannel.Y))
            Add(results, node, "ASPECT_RATIO requires continuous quantitative primary X and Y scales.");
        if (coordinate.Kind == AdvancedChartCoordinateKind.TransposedCartesian && chart.Layers.Any(layer =>
            layer.Mark is not (AdvancedChartMarkKind.Point or AdvancedChartMarkKind.Text or AdvancedChartMarkKind.Rule or AdvancedChartMarkKind.Rect or AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area) ||
            layer.Position is not ({ Kind: AdvancedChartPositionKind.Identity } or { Kind: AdvancedChartPositionKind.Jitter } or
            { Kind: AdvancedChartPositionKind.Nudge, Unit: AdvancedChartPositionUnit.Em or AdvancedChartPositionUnit.Band or AdvancedChartPositionUnit.Data }) ||
            EffectiveEncodings(chart, layer).Any(encoding => encoding.Stack != AdvancedChartStackMode.None ||
                encoding.Channel == AdvancedChartChannel.Y2)))
            Add(results, node, $"{transposedCompositionLabel} supports POINT, TEXT, supported LINE/AREA/RECT and RULE layers without stacking or secondary axes.");
        if (coordinate.Kind == AdvancedChartCoordinateKind.TransposedCartesian)
            foreach (var layer in chart.Layers.Where(layer => layer.Mark is AdvancedChartMarkKind.Rule or AdvancedChartMarkKind.Rect or AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area))
            {
                var encodings = EffectiveEncodings(chart, layer);
                var channels = encodings.Select(encoding => encoding.Channel).ToHashSet();
                if (layer.Mark is (AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area) && layer.Conditions.Length > 0) continue;
                if (layer.Mark == AdvancedChartMarkKind.Area)
                {
                    var ribbon = channels.SetEquals([AdvancedChartChannel.X, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd]) ||
                        coordinate.AspectRatio is not null && channels.SetEquals([AdvancedChartChannel.X, AdvancedChartChannel.ConfidenceLow, AdvancedChartChannel.ConfidenceHigh]);
                    var interpolation = layer.Styles.FirstOrDefault(style => style.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase));
                    var yEncoding = encodings.FirstOrDefault(encoding => encoding.Channel == AdvancedChartChannel.Y);
                    var yScale = yEncoding is null ? null : chart.Scales.FirstOrDefault(scale => scale.Name.Equals(EffectiveScaleId(coordinate.Kind, yEncoding), StringComparison.OrdinalIgnoreCase));
                    if ((!ribbon && !channels.SetEquals([AdvancedChartChannel.X, AdvancedChartChannel.Y])) ||
                        (ribbon ? layer.AreaBaseline is not null : !string.Equals(layer.AreaBaseline, "ZERO", StringComparison.OrdinalIgnoreCase) || yScale is { Kind: not AdvancedChartScaleKind.Linear }) ||
                        !(layer.Position.Kind == AdvancedChartPositionKind.Identity || coordinate.AspectRatio is not null && layer.Position is ({ Kind: AdvancedChartPositionKind.Jitter } or { Kind: AdvancedChartPositionKind.Nudge, Unit: AdvancedChartPositionUnit.Em or AdvancedChartPositionUnit.Band or AdvancedChartPositionUnit.Data })) || layer.Conditions.Length > 0 ||
                        !string.Equals(layer.NullHandling, "GAP", StringComparison.OrdinalIgnoreCase) ||
                        !(string.Equals(interpolation is null ? null : LiteralText(interpolation.Value)?.Trim(), "LINEAR", StringComparison.OrdinalIgnoreCase) ||
                            coordinate.AspectRatio is not null && interpolation is not null && LiteralText(interpolation.Value)?.Trim().ToUpperInvariant() is ("SMOOTH" or "STEP_BEFORE" or "STEP_AFTER")) ||
                        encodings.Any(encoding => encoding.DataKind != AdvancedChartDataKind.Quantitative || encoding.Source.Kind is not (AdvancedChartBindingSourceKind.Field or AdvancedChartBindingSourceKind.Datum)))
                        Add(results, Anchor(layer, chartNode), $"{transposedCompositionLabel} AREA requires quantitative field/DATUM X/Y with AREA_BASELINE = ZERO and a linear Y scale, or X/Y_START/Y_END with no baseline (also X/CONFIDENCE_LOW/CONFIDENCE_HIGH with ASPECT_RATIO); IDENTITY (or JITTER/NUDGE UNIT EM/BAND/DATA with ASPECT_RATIO), GAP, LINEAR interpolation (or SMOOTH/STEP_BEFORE/STEP_AFTER with ASPECT_RATIO) and no CONDITIONS.");
                    continue;
                }
                if (layer.Mark == AdvancedChartMarkKind.Line)
                {
                    var interpolation = layer.Styles.FirstOrDefault(style => style.Name.Equals("INTERPOLATION", StringComparison.OrdinalIgnoreCase));
                    if (!channels.SetEquals([AdvancedChartChannel.X, AdvancedChartChannel.Y]) ||
                        !(layer.Position.Kind == AdvancedChartPositionKind.Identity || coordinate.AspectRatio is not null && layer.Position is ({ Kind: AdvancedChartPositionKind.Jitter } or { Kind: AdvancedChartPositionKind.Nudge, Unit: AdvancedChartPositionUnit.Em or AdvancedChartPositionUnit.Band or AdvancedChartPositionUnit.Data })) || layer.Conditions.Length > 0 ||
                        !string.Equals(layer.NullHandling, "GAP", StringComparison.OrdinalIgnoreCase) ||
                        interpolation is null || !(string.Equals(LiteralText(interpolation.Value)?.Trim(), "LINEAR", StringComparison.OrdinalIgnoreCase) ||
                            coordinate.AspectRatio is not null && LiteralText(interpolation.Value)?.Trim().ToUpperInvariant() is ("SMOOTH" or "STEP_BEFORE" or "STEP_AFTER")) ||
                        encodings.Any(encoding => encoding.DataKind != AdvancedChartDataKind.Quantitative || encoding.Source.Kind is not (AdvancedChartBindingSourceKind.Field or AdvancedChartBindingSourceKind.Datum)))
                        Add(results, Anchor(layer, chartNode), $"{transposedCompositionLabel} LINE requires exactly quantitative field/DATUM X/Y bindings, IDENTITY (or JITTER/NUDGE UNIT EM/BAND/DATA with ASPECT_RATIO), NULL_HANDLING = GAP, STYLE INTERPOLATION = LINEAR (or SMOOTH/STEP_BEFORE/STEP_AFTER with ASPECT_RATIO), and no CONDITIONS.");
                    continue;
                }
                if (layer.Mark == AdvancedChartMarkKind.Rect)
                {
                    if (!channels.SetEquals([AdvancedChartChannel.XStart, AdvancedChartChannel.XEnd, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd]) ||
                        layer.Position is not ({ Kind: AdvancedChartPositionKind.Identity } or { Kind: AdvancedChartPositionKind.Jitter } or { Kind: AdvancedChartPositionKind.Nudge, Unit: AdvancedChartPositionUnit.Em or AdvancedChartPositionUnit.Band or AdvancedChartPositionUnit.Data }) || layer.Conditions.Length > 0 ||
                        encodings.Any(encoding => encoding.DataKind != AdvancedChartDataKind.Quantitative ||
                            encoding.Source.Kind is not (AdvancedChartBindingSourceKind.Field or AdvancedChartBindingSourceKind.Datum)))
                        Add(results, Anchor(layer, chartNode), $"{transposedCompositionLabel} RECT requires exactly four quantitative field/DATUM X_START/X_END/Y_START/Y_END bindings, IDENTITY, JITTER or NUDGE UNIT EM/BAND/DATA, and no CONDITIONS.");
                    continue;
                }
                var ranged = channels.SetEquals([AdvancedChartChannel.X, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd]) ||
                    channels.SetEquals([AdvancedChartChannel.Y, AdvancedChartChannel.XStart, AdvancedChartChannel.XEnd]) ||
                    channels.SetEquals([AdvancedChartChannel.XStart, AdvancedChartChannel.XEnd, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd]);
                if (ranged)
                {
                    if (layer.Position is not ({ Kind: AdvancedChartPositionKind.Identity } or { Kind: AdvancedChartPositionKind.Jitter } or
                        { Kind: AdvancedChartPositionKind.Nudge, Unit: AdvancedChartPositionUnit.Em or AdvancedChartPositionUnit.Band or AdvancedChartPositionUnit.Data }) || layer.Conditions.Length > 0 ||
                        encodings.Any(encoding => encoding.DataKind != AdvancedChartDataKind.Quantitative ||
                            encoding.Source.Kind is not (AdvancedChartBindingSourceKind.Field or AdvancedChartBindingSourceKind.Datum)))
                        Add(results, Anchor(layer, chartNode), $"{transposedCompositionLabel} RULE segments require quantitative field/DATUM bindings, IDENTITY, JITTER or NUDGE UNIT EM/BAND/DATA, and no CONDITIONS.");
                    continue;
                }
                if (layer.Position is not ({ Kind: AdvancedChartPositionKind.Identity } or { Kind: AdvancedChartPositionKind.Jitter } or
                    { Kind: AdvancedChartPositionKind.Nudge, Unit: AdvancedChartPositionUnit.Em or AdvancedChartPositionUnit.Band or AdvancedChartPositionUnit.Data }) || layer.Conditions.Length > 0 ||
                    encodings.Count != 1 || encodings[0].Channel is not (AdvancedChartChannel.X or AdvancedChartChannel.Y) ||
                    encodings[0].Source.Kind is not (AdvancedChartBindingSourceKind.Datum or AdvancedChartBindingSourceKind.Field) || encodings[0].DataKind != AdvancedChartDataKind.Quantitative ||
                    layer.Position.Kind is (AdvancedChartPositionKind.Nudge or AdvancedChartPositionKind.Jitter) &&
                    (encodings[0].Channel == AdvancedChartChannel.X ? layer.Position.Y != 0m : layer.Position.X != 0m))
                    Add(results, Anchor(layer, chartNode), $"{transposedCompositionLabel} RULE requires one quantitative field or DATUM X or Y binding, IDENTITY, JITTER or NUDGE UNIT EM/BAND/DATA along the bound axis only, and no CONDITIONS or other encodings.");
            }
    }

    private static void ValidateFacetAndResolution(List<Diagnostic> results, AdvancedChartDefinition chart, AstNode chartNode)
    {
        if (chart.Facet is { } facet)
        {
            var node = Anchor(facet, chartNode);
            if (facet.RowField is null && facet.ColumnField is null && facet.WrapField is null)
                Add(results, node, "FACET must declare ROW, COLUMN, or WRAP.");
            if (facet.WrapField is not null && (facet.RowField is not null || facet.ColumnField is not null))
                Add(results, node, "FACET WRAP is mutually exclusive with ROW and COLUMN.");
            if (facet.Columns is not null && facet.WrapField is null)
                Add(results, node, "FACET COLUMNS requires WRAP.");
            if (facet.Columns is < 1 or > 12)
                Add(results, node, "FACET COLUMNS must be between 1 and 12.");
            if (facet.RowField is not null && facet.RowField.Equals(facet.ColumnField, StringComparison.OrdinalIgnoreCase))
                Add(results, node, "FACET ROW and COLUMN fields must be different.");
            return;
        }
        if (chart.Resolution.X == AdvancedChartResolutionMode.Independent ||
            chart.Resolution.Y == AdvancedChartResolutionMode.Independent ||
            chart.Resolution.Color == AdvancedChartResolutionMode.Independent)
            Add(results, Anchor(chart.Resolution, chartNode), "Independent scale resolution requires FACET.");
    }

    private static bool ContinuousPositionalScale(AdvancedChartDefinition chart, AdvancedChartChannel channel)
    {
        var declared = chart.Scales.FirstOrDefault(scale => scale.Channel == channel);
        if (declared is not null)
            return declared.Kind is AdvancedChartScaleKind.Linear or AdvancedChartScaleKind.Logarithmic;
        foreach (var layer in chart.Layers)
            foreach (var encoding in EffectiveEncodings(chart, layer))
            {
                if (encoding.Scale is not null || encoding.Source.Kind == AdvancedChartBindingSourceKind.Value) continue;
                if (BaseScaleChannel(encoding.Channel) != channel) continue;
                if (AdvancedChartScaleInference.Infer(encoding.Channel, encoding.DataKind, layer.Mark) is { } kind)
                    return kind is AdvancedChartScaleKind.Linear or AdvancedChartScaleKind.Logarithmic;
            }
        return false;
    }

    private static bool CompatibleScaleKind(AdvancedChartDataKind dataKind, AdvancedChartScaleKind scaleKind) => dataKind switch
    {
        AdvancedChartDataKind.Quantitative => scaleKind is AdvancedChartScaleKind.Linear or AdvancedChartScaleKind.Logarithmic or AdvancedChartScaleKind.Identity,
        AdvancedChartDataKind.Temporal => scaleKind == AdvancedChartScaleKind.Time,
        AdvancedChartDataKind.Nominal or AdvancedChartDataKind.Ordinal => scaleKind is AdvancedChartScaleKind.Band or AdvancedChartScaleKind.Point or AdvancedChartScaleKind.Ordinal or AdvancedChartScaleKind.Identity,
        _ => false
    };

    private static bool IsPositional(AdvancedChartChannel channel) => channel is
        AdvancedChartChannel.X or AdvancedChartChannel.X2 or AdvancedChartChannel.XStart or AdvancedChartChannel.XEnd or
        AdvancedChartChannel.Y or AdvancedChartChannel.Y2 or AdvancedChartChannel.YStart or AdvancedChartChannel.YEnd or
        AdvancedChartChannel.Low or AdvancedChartChannel.Q1 or AdvancedChartChannel.Median or AdvancedChartChannel.Q3 or
        AdvancedChartChannel.High or AdvancedChartChannel.Open or AdvancedChartChannel.Close or
        AdvancedChartChannel.ErrorLow or AdvancedChartChannel.ErrorHigh or
        AdvancedChartChannel.ConfidenceLow or AdvancedChartChannel.ConfidenceHigh;

    private static bool IsConstant(Expression expression) => expression is LiteralExpression or VariableExpression;

    // The expression parser represents a negative literal as 0m minus the unsigned literal.
    private static LiteralExpression? SignedNumericLiteral(Expression expression) => expression switch
    {
        BinaryExpression { Operator: TokenType.MINUS, Left: LiteralExpression { Value: 0m }, Right: LiteralExpression literal } => literal,
        UnaryExpression { Operator: TokenType.MINUS or TokenType.PLUS, Expression: LiteralExpression literal } => literal,
        _ => null
    };

    private static bool IsSupportedPredicate(Expression expression) => expression switch
    {
        BinaryExpression binary when binary.Operator is TokenType.AND or TokenType.OR =>
            IsSupportedPredicate(binary.Left) && IsSupportedPredicate(binary.Right),
        BinaryExpression binary when binary.Operator is TokenType.EQUALS or TokenType.NOT_EQUALS or
            TokenType.LESS_THAN or TokenType.LESS_EQUALS or TokenType.GREATER_THAN or TokenType.GREATER_EQUALS =>
            IsSupportedOperand(binary.Left) && IsSupportedOperand(binary.Right),
        UnaryExpression unary when unary.Operator == TokenType.NOT => IsSupportedPredicate(unary.Expression),
        IsNullExpression isNull => IsSupportedOperand(isNull.Expression),
        IdentifierExpression or VariableExpression or LiteralExpression => true,
        _ => false
    };

    private static bool IsSupportedOperand(Expression expression) =>
        expression is IdentifierExpression or VariableExpression or LiteralExpression;

    private enum LiteralKind { Numeric, Text, Boolean, Temporal, Null }

    private static LiteralKind? ConstantKind(Expression expression) => expression is not LiteralExpression literal
        ? null
        : literal.Value switch
        {
            null or DBNull => LiteralKind.Null,
            bool => LiteralKind.Boolean,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => LiteralKind.Numeric,
            DateOnly or TimeOnly or DateTime or DateTimeOffset => LiteralKind.Temporal,
            _ => LiteralKind.Text
        };

    private static string? LiteralText(Expression expression) =>
        expression is LiteralExpression { Value: string text } ? text : null;

    private static string? LiteralNumberText(Expression expression) =>
        expression is LiteralExpression { Value: not null } literal
            ? Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static bool IsPortableColor(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    private static void Duplicates<T>(List<Diagnostic> results, IEnumerable<T> items, Func<T, string> key, Func<T, AstNode> node, string kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            if (!seen.Add(key(item)))
                Add(results, node(item), $"Duplicate {kind} '{key(item)}'.");
    }

    private static AstNode Anchor(AstNode node, AstNode fallback) => node.Line > 0 ? node : fallback;

    private static void Add(List<Diagnostic> results, AstNode node, string message) => results.Add(new Diagnostic
    {
        Message = message,
        Line = node.Line,
        Column = node.Column,
        Severity = DiagnosticSeverity.Error,
        Code = DiagnosticCode,
        Source = DiagnosticSource
    });
}
