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

        return results;
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

            if (layer.ZIndex < 0)
                Add(results, layerNode, $"Layer '{layer.Name}' has a negative Z_INDEX.");
            if (layer.BandSize <= 0m || layer.BandSize > 1m)
                Add(results, layerNode, $"Layer '{layer.Name}' BAND_SIZE must be greater than zero and at most one.");
            if (layer.TickThickness <= 0m || layer.TickThickness > 1m)
                Add(results, layerNode, $"Layer '{layer.Name}' THICKNESS must be greater than zero and at most one em.");

            ValidatePosition(results, chart, layer, layerNode);

            var effective = EffectiveEncodings(chart, layer);
            if (effective.Count == 0)
                Add(results, layerNode, $"Layer '{layer.Name}' has no effective encodings.");

            foreach (var encoding in effective)
                ValidateEncoding(results, chart, layer, encoding, layerNode, declared, inferred);

            ValidateLayerShape(results, chart, layer, effective, layerNode);
            ValidateConditions(results, layer, layerNode);
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
             encoding.Channel is not (AdvancedChartChannel.Y or AdvancedChartChannel.Y2) ||
             chart.Coordinate.Kind == AdvancedChartCoordinateKind.Polar))
            Add(results, node, $"Layer '{layer.Name}' STACK requires a quantitative Cartesian/transposed Y or Y2 binding; polar/radial stacking is not yet portable.");

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
            chart.Coordinate.Kind != AdvancedChartCoordinateKind.Cartesian)
            Add(results, node, $"Layer '{layer.Name}' data-domain NUDGE requires Cartesian coordinates.");
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
                    ValidateIntervalTypes(results, effective, AdvancedChartChannel.YStart, AdvancedChartChannel.YEnd, layerNode,
                        $"AREA layer '{layer.Name}' ribbon endpoints require matching quantitative or temporal types.");
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

    private static void ValidateConditions(List<Diagnostic> results, AdvancedChartLayer layer, AstNode layerNode)
    {
        foreach (var condition in layer.Conditions)
        {
            var node = Anchor(condition, layerNode);
            if (layer.Mark is AdvancedChartMarkKind.Line or AdvancedChartMarkKind.Area)
                Add(results, node, $"Layer '{layer.Name}' cannot use row-level CONDITIONS on connected {layer.Mark.ToString().ToUpperInvariant()} marks; stage separate series or layers in ETL-SQL.");
            if (!IsSupportedPredicate(condition.Predicate))
                Add(results, node, $"Layer '{layer.Name}' condition predicate supports only fields, parameters, literals, comparisons, AND/OR/NOT, and IS NULL.");
            if (!IsConstant(condition.WhenTrue))
                Add(results, node, $"Layer '{layer.Name}' condition THEN value must be a literal or parameter.");
            if (condition.WhenFalse is not null && !IsConstant(condition.WhenFalse))
                Add(results, node, $"Layer '{layer.Name}' condition ELSE value must be a literal or parameter.");
            if (condition.Channel == AdvancedChartConditionChannel.Shape)
            {
                if (layer.Mark != AdvancedChartMarkKind.Point)
                    Add(results, node, $"Layer '{layer.Name}' may condition SHAPE only on POINT marks.");
                ValidateConditionShape(condition.WhenTrue, "THEN");
                if (condition.WhenFalse is not null) ValidateConditionShape(condition.WhenFalse, "ELSE");
            }

            void ValidateConditionShape(Expression value, string branch)
            {
                if (LiteralText(value) is { } shape && !PointShapeVocabulary.IsSupported(shape))
                    Add(results, node, $"Layer '{layer.Name}' condition {branch} SHAPE accepts only {PointShapeVocabulary.DisplayList}; found '{shape}'.");
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
        if (coordinate.AspectRatio is null) return;
        if (coordinate.AspectRatio <= 0m)
            Add(results, node, "Cartesian ASPECT_RATIO must be greater than zero.");
        if (coordinate.Kind != AdvancedChartCoordinateKind.Cartesian)
            Add(results, node, "ASPECT_RATIO currently supports CARTESIAN coordinates only.");
        else if (!ContinuousPositionalScale(chart, AdvancedChartChannel.X) || !ContinuousPositionalScale(chart, AdvancedChartChannel.Y))
            Add(results, node, "ASPECT_RATIO requires continuous quantitative primary X and Y scales.");
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
