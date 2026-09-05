using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Lowers native advanced Report-SQL chart syntax into renderer-neutral intent.</summary>
public sealed class AdvancedChartLowerer(IExecutionContext context)
{
    /// <summary>Anchor for failures that belong to the CHART clause as a whole.</summary>
    private AstNode? _chartNode;

    public ChartSpec Lower(CreateVisualStatement statement, VisualManifest manifest)
    {
        var chart = statement.AdvancedChart ?? throw new InvalidOperationException("CUSTOM visual requires a CHART definition.");
        // One shared semantic pass, identical to the one the lint rule runs, so the editor and preview
        // can never disagree about which authoring failures exist or where they are.
        var diagnostics = AdvancedChartSemanticValidator.Validate(chart, statement);
        if (diagnostics.Count > 0) throw new AdvancedChartSemanticException(diagnostics);
        _chartNode = chart.Line > 0 ? chart : statement;
        var global = chart.Encodings.Select(Binding).ToImmutableArray();
        var declaredScales = chart.Scales.Select(Scale).ToImmutableArray();
        var layers = chart.Layers.Select(layer => Layer(layer, global)).ToImmutableArray();
        var inference = InferScales(chart.Coordinate.Kind, layers);
        layers = inference.Layers;
        var inferredScales = inference.Scales;
        var bindings = layers.SelectMany(layer => layer.Bindings)
            .GroupBy(binding => binding.Channel)
            .Select(group => group.First()).ToList();
        if (chart.Facet?.RowField is { } row)
            bindings.Add(new FieldBinding(FieldChannel.Row, row, DataSemanticKind.Nominal));
        if (chart.Facet?.ColumnField is { } column)
            bindings.Add(new FieldBinding(FieldChannel.Column, column, DataSemanticKind.Nominal));
        if (chart.Facet?.WrapField is { } wrap)
            bindings.Add(new FieldBinding(FieldChannel.Wrap, wrap, DataSemanticKind.Nominal));

        if (chart.Annotations.Length > 0)
        {
            var annotLayers = new List<MarkLayerSpec>();
            for (var index = 0; index < chart.Annotations.Length; index++)
            {
                var overlay = chart.Annotations[index];
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
                    annotLayers.Add(new MarkLayerSpec(
                        $"annotation-point-{index:D2}",
                        MarkKind.Point,
                        150 + index,
                        [],
                        annotTokens.ToImmutableArray(),
                        overlay.Label));
                }
            }
            if (annotLayers.Count > 0)
                layers = layers.AddRange(annotLayers);
        }

        var resolution = new ScaleResolutionSpec(
            Resolution(chart.Resolution.X), Resolution(chart.Resolution.Y), Resolution(chart.Resolution.Color));
        var title = manifest.Options.GetValueOrDefault("title") ?? manifest.Name;
        var resolvedBindings = bindings.ToImmutableArray();
        var spec = ChartSpec.Create(
            manifest.Name,
            statement.Source.TempTableName ?? $"inline:{manifest.Name}",
            resolvedBindings,
            layers,
            new CoordinateSpec(Coordinate(chart.Coordinate.Kind), chart.Coordinate.StartAngle,
                chart.Coordinate.EndAngle, chart.Coordinate.InnerRadius, chart.Coordinate.AspectRatio)
            {
                Geography = chart.Coordinate.Kind != AdvancedChartCoordinateKind.Geographic ? null : new GeographicCoordinateSpec(
                    chart.Coordinate.Projection == AdvancedChartGeographicProjection.Mercator
                        ? GeographicProjectionKind.Mercator : GeographicProjectionKind.Equirectangular,
                    chart.Coordinate.MapFile is null ? GeographicMapSourceKind.BuiltIn : GeographicMapSourceKind.File,
                    chart.Coordinate.MapFile ?? chart.Coordinate.MapName ?? "WORLD",
                    chart.Coordinate.FeatureKey ?? "name")
            },
            declaredScales.AddRange(inferredScales.Where(inferred => declaredScales.All(declared =>
                !declared.Id.Equals(inferred.Id, StringComparison.OrdinalIgnoreCase)))),
            ChartStyleTokens.Formatting(context, manifest,
                layers.SelectMany(layer => layer.Bindings)
                    .Where(binding => binding.SourceKind == BindingSourceKind.Field && binding.Field is not null && binding.Format is not null)
                    .Select(binding => new FieldFormat(binding.Field!, binding.Format))
                    .Distinct().ToImmutableArray()),
            new NullHandlingSpec(ResolveCustomNullPolicy(chart, statement), []),
            ChartStyleTokens.Theme(manifest),
            new AccessibilitySpec(title, manifest.Options.GetValueOrDefault("subtitle"), null, true),
            title,
            resolution,
            chart.Facet is null ? null : new FacetSpec(chart.Facet.RowField, chart.Facet.ColumnField, resolution,
                chart.Facet.WrapField, chart.Facet.Columns),
            // CUSTOM has no MAPPINGS clause, so its interaction key can only come from the resolved
            // encodings. Lowering it here is what stops the browser cross-filtering on column zero.
            ChartInteractionResolver.Lower(statement, resolvedBindings));
        try
        {
            spec.Validate();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            // Defence in depth: the shared validator models this contract, so reaching here means the two
            // have drifted. Publish it as a positioned diagnostic rather than an unpositioned error string.
            throw AdvancedChartSemanticException.At(_chartNode ?? statement, ex.Message);
        }
        return spec;
    }

    private static NullValuePolicy ResolveCustomNullPolicy(AdvancedChartDefinition chart, CreateVisualStatement statement)
    {
        var nullOpt = chart.Layers.FirstOrDefault(l => !string.IsNullOrEmpty(l.NullHandling))?.NullHandling
            ?? statement.Options.FirstOrDefault(o => o.Key.Equals("NULL_HANDLING", StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(nullOpt))
        {
            return nullOpt.ToUpperInvariant() switch
            {
                "CONNECT" => NullValuePolicy.Skip,
                "ZERO" => NullValuePolicy.Zero,
                _ => NullValuePolicy.Gap
            };
        }
        return NullValuePolicy.Gap;
    }

    private static (ImmutableArray<MarkLayerSpec> Layers, ImmutableArray<ScaleSpec> Scales) InferScales(
        AdvancedChartCoordinateKind coordinate,
        ImmutableArray<MarkLayerSpec> layers)
    {
        var inferred = new Dictionary<string, ScaleSpec>(StringComparer.OrdinalIgnoreCase);
        var output = layers.Select(layer => layer with
        {
            Bindings = layer.Bindings.Select(binding =>
            {
                if (binding.ScaleId is not null || binding.SourceKind == BindingSourceKind.Value)
                    return binding;
                var advancedChannel = AdvancedChartEnumBridge.Channel(binding.Channel);
                if (advancedChannel is null)
                {
                    if (binding.Channel is FieldChannel.Longitude or FieldChannel.Latitude or FieldChannel.Region or FieldChannel.Route or FieldChannel.Text or FieldChannel.Tooltip or FieldChannel.Detail) return binding;
                    throw new InvalidOperationException($"Channel {binding.Channel} has no CUSTOM grammar counterpart and cannot infer a scale.");
                }
                var kind = AdvancedChartScaleInference.Infer(
                    advancedChannel.Value,
                    AdvancedChartEnumBridge.DataKind(binding.SemanticKind),
                    AdvancedChartEnumBridge.Mark(layer.Mark));
                if (kind is null)
                {
                    if (binding.Channel is FieldChannel.Longitude or FieldChannel.Latitude or FieldChannel.Region or FieldChannel.Route or FieldChannel.Text or FieldChannel.Tooltip or FieldChannel.Detail) return binding;
                    throw new InvalidOperationException($"No deterministic scale inference exists for {layer.Mark} {binding.Channel} {binding.SemanticKind}; declare a compatible scale or encoding.");
                }
                var axis = binding.Axis == AxisRole.Secondary ? "secondary" : "primary";
                var scaleChannel = BaseScaleChannel(binding.Channel);
                var id = $"inferred-{Coordinate(coordinate).ToString().ToLowerInvariant()}-{axis}-{scaleChannel.ToString().ToLowerInvariant()}";
                var scale = new ScaleSpec(id, scaleChannel, AdvancedChartEnumBridge.Scale(kind.Value), false, []);
                if (inferred.TryGetValue(id, out var existing) && existing.Kind != scale.Kind)
                    throw new InvalidOperationException($"Channel {binding.Channel} requires incompatible inferred scales ({existing.Kind} and {scale.Kind}); declare named scales explicitly.");
                inferred[id] = scale;
                return binding with { ScaleId = id };
            }).ToImmutableArray()
        }).ToImmutableArray();
        return (output, inferred.Values.OrderBy(scale => scale.Id, StringComparer.Ordinal).ToImmutableArray());
    }

    private static FieldChannel BaseScaleChannel(FieldChannel channel) => channel switch
    {
        FieldChannel.X2 or FieldChannel.XStart or FieldChannel.XEnd => FieldChannel.X,
        FieldChannel.YStart or FieldChannel.YEnd or
        FieldChannel.Low or FieldChannel.Q1 or FieldChannel.Median or FieldChannel.Q3 or FieldChannel.High or
        FieldChannel.Open or FieldChannel.Close or FieldChannel.ErrorLow or FieldChannel.ErrorHigh or
        FieldChannel.ConfidenceLow or FieldChannel.ConfidenceHigh => FieldChannel.Y,
        _ => channel
    };

    private MarkLayerSpec Layer(AdvancedChartLayer layer, ImmutableArray<FieldBinding> global)
    {
        var local = layer.Encodings.Select(Binding).ToImmutableArray();
        var effective = layer.InheritEncodings
            ? global.Where(inherited => local.All(binding => binding.Channel != inherited.Channel)).Concat(local).ToImmutableArray()
            : local;
        var styles = layer.Styles.Select(style =>
        {
            var value = Display(EvaluateLiteral(style.Value, style));
            if (style.Name.Equals("ERROR_BAR_STYLE", StringComparison.OrdinalIgnoreCase))
            {
                var upper = value.ToUpperInvariant();
                if (upper is not ("CAPS" or "NO_CAPS"))
                {
                    throw AdvancedChartSemanticException.At(style,
                        $"Layer '{layer.Name}' ERROR_BAR_STYLE accepts only CAPS or NO_CAPS; found '{value}'.");
                }
                return new StyleToken(style.Name, upper);
            }
            if (style.Name.Equals("SYMBOL_STROKE_COLOR", StringComparison.OrdinalIgnoreCase))
            {
                if (!PointMarkerStroke.IsPortableColor(value))
                    throw AdvancedChartSemanticException.At(style,
                        $"Layer '{layer.Name}' SYMBOL_STROKE_COLOR accepts portable #RRGGBB colors only; found '{value}'.");
                return new StyleToken(style.Name, value);
            }
            if (style.Name.Equals("SYMBOL_STROKE_WIDTH", StringComparison.OrdinalIgnoreCase))
            {
                if (!PointMarkerStroke.TryNormalizeWidth(value, out var width))
                    throw AdvancedChartSemanticException.At(style,
                        $"Layer '{layer.Name}' SYMBOL_STROKE_WIDTH must be a non-negative number; found '{value}'.");
                return new StyleToken(style.Name, width);
            }
            if (style.Name.Equals("LINE_WIDTH", StringComparison.OrdinalIgnoreCase))
            {
                if (!LineSeriesWidth.TryNormalize(value, out var width))
                    throw AdvancedChartSemanticException.At(style,
                        $"Layer '{layer.Name}' LINE_WIDTH must be from {LineSeriesWidth.Minimum} through {LineSeriesWidth.Maximum} pixels; found '{value}'.");
                return new StyleToken(style.Name, width);
            }
            return new StyleToken(style.Name, value);
        });
        if (!string.IsNullOrEmpty(layer.NullHandling))
        {
            styles = styles.Append(new StyleToken("nullHandling", layer.NullHandling.ToUpperInvariant()));
        }
        if (!string.IsNullOrEmpty(layer.AreaBaseline))
        {
            styles = styles.Append(new StyleToken("areaBaseline", layer.AreaBaseline.ToUpperInvariant()));
        }
        if (!string.IsNullOrEmpty(layer.HoverFocus))
        {
            styles = styles.Append(new StyleToken("hoverFocus", layer.HoverFocus.ToUpperInvariant()));
        }
        return new(
            layer.Name, Mark(layer.Mark), layer.ZIndex,
            effective,
            styles.ToImmutableArray(),
            layer.Name)
        {
            Conditions = layer.Conditions.Select(Condition).ToImmutableArray(),
            BandSize = layer.BandSize,
            TickThickness = layer.TickThickness,
            TickOrientation = AdvancedChartEnumBridge.Tick(layer.TickOrientation),
            Position = Position(layer.Position)
        };
    }

    private static PositionAdjustmentSpec? Position(AdvancedChartPosition position) => position.Kind == AdvancedChartPositionKind.Identity
        ? null
        : new PositionAdjustmentSpec(
            AdvancedChartEnumBridge.Position(position.Kind),
            position.X,
            position.Y,
            position.KeyField,
            position.Seed,
            AdvancedChartEnumBridge.Unit(position.Unit));

    private EncodingConditionSpec Condition(AdvancedChartCondition condition)
    {
        var whenTrue = EvaluateLiteral(condition.WhenTrue, condition);
        var whenFalse = condition.WhenFalse is null ? null : EvaluateLiteral(condition.WhenFalse, condition);
        if (condition.Channel == AdvancedChartConditionChannel.Shape)
        {
            ValidatePointShape(Display(whenTrue), condition);
            if (whenFalse is not null) ValidatePointShape(Display(whenFalse), condition);
        }
        return new EncodingConditionSpec(
            AdvancedChartEnumBridge.Condition(condition.Channel),
            Predicate(condition.Predicate, condition),
            whenTrue,
            whenFalse);
    }

    private EncodingPredicate Predicate(Expression expression, AstNode anchor) => expression switch
    {
        BinaryExpression binary when binary.Operator is TokenType.AND or TokenType.OR => new(
            binary.Operator == TokenType.AND ? PredicateKind.And : PredicateKind.Or,
            First: Predicate(binary.Left, anchor), Second: Predicate(binary.Right, anchor)),
        BinaryExpression binary when Comparison(binary.Operator) is { } comparison => new(
            PredicateKind.Comparison, Operand(binary.Left, anchor), Operand(binary.Right, anchor), comparison),
        UnaryExpression unary when unary.Operator == TokenType.NOT => new(PredicateKind.Not, First: Predicate(unary.Expression, anchor)),
        IsNullExpression isNull => new(isNull.Not ? PredicateKind.IsNotNull : PredicateKind.IsNull, Left: Operand(isNull.Expression, anchor)),
        IdentifierExpression or VariableExpression or LiteralExpression => new(PredicateKind.Truthy, Left: Operand(expression, anchor)),
        _ => throw AdvancedChartSemanticException.At(anchor,
            $"Unsupported advanced chart condition expression '{expression.GetType().Name}'.")
    };

    private PredicateOperand Operand(Expression expression, AstNode anchor) => expression switch
    {
        IdentifierExpression identifier => new(PredicateOperandKind.Field, identifier.Name.Split('.').Last(), null),
        VariableExpression variable => new(PredicateOperandKind.Literal, null, EvaluateVariable(variable.Name, anchor)),
        LiteralExpression literal => new(PredicateOperandKind.Literal, null, Value(literal.Value)),
        _ => throw AdvancedChartSemanticException.At(anchor,
            $"Advanced chart conditions support only fields, parameters, and literals; found '{expression.GetType().Name}'.")
    };

    private ChartValue EvaluateLiteral(Expression expression, AstNode anchor) => expression switch
    {
        LiteralExpression literal => Value(literal.Value),
        VariableExpression variable => EvaluateVariable(variable.Name, anchor),
        _ => throw AdvancedChartSemanticException.At(anchor,
            "Advanced chart style, scale, and condition result values must be literals or parameters.")
    };

    private ChartValue EvaluateVariable(string name, AstNode anchor)
    {
        if (!context.VarContext.ContainsVariable(name))
            throw AdvancedChartSemanticException.At(anchor, $"Advanced chart parameter '{name}' is not declared.");
        var metadata = context.VarContext.GetVariablesWithMetadata()
            .FirstOrDefault(item => item.Key.TrimStart('@').Equals(name.TrimStart('@'), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(metadata.Key) && (metadata.Value.Metadata.IsSecret || metadata.Value.Metadata.IsSensitive))
            throw AdvancedChartSemanticException.At(anchor,
                $"Advanced chart parameter '{name}' is secret-bearing and cannot be used in an encoding binding.");
        return Value(context.VarContext.GetVariable(name));
    }

    private ScaleSpec Scale(AdvancedChartScale scale) => new(
        scale.Name, Channel(scale.Channel), Scale(scale.Kind), scale.IncludeZero,
        scale.ExplicitOrder.Select(item => Display(EvaluateLiteral(item, scale))).ToImmutableArray(),
        scale.Minimum is null ? null : EvaluateLiteral(scale.Minimum, scale),
        scale.Maximum is null ? null : EvaluateLiteral(scale.Maximum, scale),
        scale.Reverse,
        scale.MajorTickCount,
        scale.TickInterval,
        scale.MinorTicks,
        scale.LabelRotation,
        scale.LabelSkip,
        scale.OuterPadding,
        scale.TickFormat,
        scale.TimeUnit)
    {
        ColorRange = scale.ColorRange is null ? null : new ColorRangeSpec(
            AdvancedChartEnumBridge.ColorRange(scale.ColorRange.Kind),
            Display(EvaluateLiteral(scale.ColorRange.Low, scale.ColorRange)),
            Display(EvaluateLiteral(scale.ColorRange.High, scale.ColorRange)),
            scale.ColorRange.Mid is null ? null : Display(EvaluateLiteral(scale.ColorRange.Mid, scale.ColorRange)),
            scale.ColorRange.Midpoint is null ? null : PlotPlanResolver.Number(EvaluateLiteral(scale.ColorRange.Midpoint, scale.ColorRange)),
            scale.ColorRange.NullColor is null ? "#9ca3af" : Display(EvaluateLiteral(scale.ColorRange.NullColor, scale.ColorRange)))
    };

    private FieldBinding Binding(AdvancedChartEncoding binding)
    {
        var channel = Channel(binding.Channel);
        var semanticKind = DataKind(binding.DataKind);
        var axis = AdvancedChartEnumBridge.Axis(binding.Axis);
        var sort = AdvancedChartEnumBridge.Sort(binding.Sort);
        if (binding.Source.Kind == AdvancedChartBindingSourceKind.Field)
            return new FieldBinding(channel, binding.Source.Field, semanticKind, binding.Scale, axis, sort, binding.Format)
            { Stack = AdvancedChartEnumBridge.Stack(binding.Stack) };

        var expression = binding.Source.Constant
            ?? throw AdvancedChartSemanticException.At(binding, "Constant binding has no value.");
        var value = EvaluateLiteral(expression, binding);
        if (binding.Channel == AdvancedChartChannel.Shape)
            ValidatePointShape(Display(value), binding);
        var parameter = expression is VariableExpression variable ? variable.Name : null;
        return binding.Source.Kind == AdvancedChartBindingSourceKind.Datum
            ? FieldBinding.Datum(channel, value, semanticKind, binding.Scale, axis, parameter) with
            { Sort = sort, Format = binding.Format, Stack = AdvancedChartEnumBridge.Stack(binding.Stack) }
            : FieldBinding.Value(channel, value, semanticKind, parameter) with
            { Sort = sort, Format = binding.Format, Stack = AdvancedChartEnumBridge.Stack(binding.Stack) };
    }

    private static void ValidatePointShape(string value, AstNode anchor)
    {
        if (!PointShapeVocabulary.IsSupported(value))
            throw AdvancedChartSemanticException.At(anchor,
                $"SHAPE accepts only {PointShapeVocabulary.DisplayList}; found '{value}'.");
    }

    private static ComparisonKind? Comparison(TokenType token) => token switch
    {
        TokenType.EQUALS => ComparisonKind.Equal,
        TokenType.NOT_EQUALS => ComparisonKind.NotEqual,
        TokenType.LESS_THAN => ComparisonKind.LessThan,
        TokenType.LESS_EQUALS => ComparisonKind.LessThanOrEqual,
        TokenType.GREATER_THAN => ComparisonKind.GreaterThan,
        TokenType.GREATER_EQUALS => ComparisonKind.GreaterThanOrEqual,
        _ => null
    };

    private static ChartValue Value(object? value) => value switch
    {
        null or DBNull => ChartValue.Null(),
        bool item => ChartValue.From(item),
        byte or sbyte or short or ushort or int or uint or long or ulong => ChartValue.From(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        float or double => ChartValue.From(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        decimal item => ChartValue.From(item),
        DateOnly item => ChartValue.From(item),
        TimeOnly item => ChartValue.From(item),
        DateTimeOffset item => ChartValue.From(item),
        DateTime item => ChartValue.FromLocal(DateTime.SpecifyKind(item, DateTimeKind.Unspecified)),
        _ => ChartValue.From(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty)
    };

    private static string Display(ChartValue value) => PlotPlanResolver.Display(value);
    private static MarkKind Mark(AdvancedChartMarkKind value) => AdvancedChartEnumBridge.Mark(value);
    private static FieldChannel Channel(AdvancedChartChannel value) => AdvancedChartEnumBridge.Channel(value);
    private static DataSemanticKind DataKind(AdvancedChartDataKind value) => AdvancedChartEnumBridge.DataKind(value);
    private static ScaleKind Scale(AdvancedChartScaleKind value) => AdvancedChartEnumBridge.Scale(value);
    private static CoordinateKind Coordinate(AdvancedChartCoordinateKind value) => AdvancedChartEnumBridge.Coordinate(value);
    private static ScaleResolutionMode Resolution(AdvancedChartResolutionMode value) => AdvancedChartEnumBridge.Resolution(value);

}
