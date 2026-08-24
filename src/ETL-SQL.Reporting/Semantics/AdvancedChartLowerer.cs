using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>Lowers native advanced Report-SQL chart syntax into renderer-neutral intent.</summary>
public sealed class AdvancedChartLowerer(IExecutionContext context)
{
    public ChartSpec Lower(CreateVisualStatement statement, VisualManifest manifest)
    {
        var chart = statement.AdvancedChart ?? throw new InvalidOperationException("CUSTOM visual requires a CHART definition.");
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

        var resolution = new ScaleResolutionSpec(
            Resolution(chart.Resolution.X), Resolution(chart.Resolution.Y), Resolution(chart.Resolution.Color));
        var title = manifest.Options.GetValueOrDefault("title") ?? manifest.Name;
        var spec = ChartSpec.Create(
            manifest.Name,
            statement.Source.TempTableName ?? $"inline:{manifest.Name}",
            bindings.ToImmutableArray(),
            layers,
            new CoordinateSpec(Coordinate(chart.Coordinate.Kind), chart.Coordinate.StartAngle,
                chart.Coordinate.EndAngle, chart.Coordinate.InnerRadius, chart.Coordinate.AspectRatio),
            declaredScales.AddRange(inferredScales.Where(inferred => declaredScales.All(declared =>
                !declared.Id.Equals(inferred.Id, StringComparison.OrdinalIgnoreCase)))),
            new FormattingSpec(CultureInfo.InvariantCulture.Name, "UTC", "",
                layers.SelectMany(layer => layer.Bindings)
                    .Where(binding => binding.SourceKind == BindingSourceKind.Field && binding.Field is not null && binding.Format is not null)
                    .Select(binding => new FieldFormat(binding.Field!, binding.Format))
                    .Distinct().ToImmutableArray()),
            new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec(manifest.Styles?.GetValueOrDefault("THEME") ?? "default", []),
            new AccessibilitySpec(title, manifest.Options.GetValueOrDefault("subtitle"), null, true),
            title,
            resolution,
            chart.Facet is null ? null : new FacetSpec(chart.Facet.RowField, chart.Facet.ColumnField, resolution,
                chart.Facet.WrapField, chart.Facet.Columns));
        spec.Validate();
        return spec;
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
                var kind = AdvancedChartScaleInference.Infer(
                    Enum.Parse<AdvancedChartChannel>(binding.Channel.ToString()),
                    Enum.Parse<AdvancedChartDataKind>(binding.SemanticKind.ToString()),
                    Enum.Parse<AdvancedChartMarkKind>(layer.Mark.ToString()));
                if (kind is null)
                {
                    if (binding.Channel is FieldChannel.Text or FieldChannel.Tooltip or FieldChannel.Detail) return binding;
                    throw new InvalidOperationException($"No deterministic scale inference exists for {layer.Mark} {binding.Channel} {binding.SemanticKind}; declare a compatible scale or encoding.");
                }
                var axis = binding.Axis == AxisRole.Secondary ? "secondary" : "primary";
                var scaleChannel = BaseScaleChannel(binding.Channel);
                var id = $"inferred-{Coordinate(coordinate).ToString().ToLowerInvariant()}-{axis}-{scaleChannel.ToString().ToLowerInvariant()}";
                var scale = new ScaleSpec(id, scaleChannel, Enum.Parse<ScaleKind>(kind.Value.ToString()), false, []);
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
        FieldChannel.YStart or FieldChannel.YEnd => FieldChannel.Y,
        _ => channel
    };

    private MarkLayerSpec Layer(AdvancedChartLayer layer, ImmutableArray<FieldBinding> global)
    {
        var local = layer.Encodings.Select(Binding).ToImmutableArray();
        var effective = layer.InheritEncodings
            ? global.Where(inherited => local.All(binding => binding.Channel != inherited.Channel)).Concat(local).ToImmutableArray()
            : local;
        return new(
        layer.Name, Mark(layer.Mark), layer.ZIndex,
        effective,
        layer.Styles.Select(style => new StyleToken(style.Name, Display(EvaluateLiteral(style.Value)))).ToImmutableArray(),
        layer.Name)
        {
            Conditions = layer.Conditions.Select(Condition).ToImmutableArray(),
            BandSize = layer.BandSize,
            TickThickness = layer.TickThickness,
            TickOrientation = Enum.Parse<TickOrientation>(layer.TickOrientation.ToString()),
            Position = Position(layer.Position)
        };
    }

    private static PositionAdjustmentSpec? Position(AdvancedChartPosition position) => position.Kind == AdvancedChartPositionKind.Identity
        ? null
        : new PositionAdjustmentSpec(
            Enum.Parse<PositionAdjustmentKind>(position.Kind.ToString()),
            position.X,
            position.Y,
            position.KeyField,
            position.Seed,
            Enum.Parse<PositionAdjustmentUnit>(position.Unit.ToString()));

    private EncodingConditionSpec Condition(AdvancedChartCondition condition) => new(
        Enum.Parse<ConditionalEncodingChannel>(condition.Channel.ToString()),
        Predicate(condition.Predicate),
        EvaluateLiteral(condition.WhenTrue),
        condition.WhenFalse is null ? null : EvaluateLiteral(condition.WhenFalse));

    private EncodingPredicate Predicate(Expression expression) => expression switch
    {
        BinaryExpression binary when binary.Operator is TokenType.AND or TokenType.OR => new(
            binary.Operator == TokenType.AND ? PredicateKind.And : PredicateKind.Or,
            First: Predicate(binary.Left), Second: Predicate(binary.Right)),
        BinaryExpression binary when Comparison(binary.Operator) is { } comparison => new(
            PredicateKind.Comparison, Operand(binary.Left), Operand(binary.Right), comparison),
        UnaryExpression unary when unary.Operator == TokenType.NOT => new(PredicateKind.Not, First: Predicate(unary.Expression)),
        IsNullExpression isNull => new(isNull.Not ? PredicateKind.IsNotNull : PredicateKind.IsNull, Left: Operand(isNull.Expression)),
        IdentifierExpression or VariableExpression or LiteralExpression => new(PredicateKind.Truthy, Left: Operand(expression)),
        _ => throw new InvalidOperationException($"Unsupported advanced chart condition expression '{expression.GetType().Name}'.")
    };

    private PredicateOperand Operand(Expression expression) => expression switch
    {
        IdentifierExpression identifier => new(PredicateOperandKind.Field, identifier.Name.Split('.').Last(), null),
        VariableExpression variable => new(PredicateOperandKind.Literal, null, EvaluateVariable(variable.Name)),
        LiteralExpression literal => new(PredicateOperandKind.Literal, null, Value(literal.Value)),
        _ => throw new InvalidOperationException($"Advanced chart conditions support only fields, parameters, and literals; found '{expression.GetType().Name}'.")
    };

    private ChartValue EvaluateLiteral(Expression expression) => expression switch
    {
        LiteralExpression literal => Value(literal.Value),
        VariableExpression variable => EvaluateVariable(variable.Name),
        _ => throw new InvalidOperationException("Advanced chart style, scale, and condition result values must be literals or parameters.")
    };

    private ChartValue EvaluateVariable(string name)
    {
        if (!context.VarContext.ContainsVariable(name))
            throw new InvalidOperationException($"Advanced chart parameter '{name}' is not declared.");
        var metadata = context.VarContext.GetVariablesWithMetadata()
            .FirstOrDefault(item => item.Key.TrimStart('@').Equals(name.TrimStart('@'), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(metadata.Key) && (metadata.Value.Metadata.IsSecret || metadata.Value.Metadata.IsSensitive))
            throw new InvalidOperationException($"Advanced chart parameter '{name}' is secret-bearing and cannot be used in an encoding binding.");
        return Value(context.VarContext.GetVariable(name));
    }

    private ScaleSpec Scale(AdvancedChartScale scale) => new(
        scale.Name, Channel(scale.Channel), Scale(scale.Kind), scale.IncludeZero,
        scale.ExplicitOrder.Select(item => Display(EvaluateLiteral(item))).ToImmutableArray(),
        scale.Minimum is null ? null : EvaluateLiteral(scale.Minimum),
        scale.Maximum is null ? null : EvaluateLiteral(scale.Maximum))
    {
        ColorRange = scale.ColorRange is null ? null : new ColorRangeSpec(
            Enum.Parse<ColorRangeKind>(scale.ColorRange.Kind.ToString()),
            Display(EvaluateLiteral(scale.ColorRange.Low)),
            Display(EvaluateLiteral(scale.ColorRange.High)),
            scale.ColorRange.Mid is null ? null : Display(EvaluateLiteral(scale.ColorRange.Mid)),
            scale.ColorRange.Midpoint is null ? null : PlotPlanResolver.Number(EvaluateLiteral(scale.ColorRange.Midpoint)),
            scale.ColorRange.NullColor is null ? "#9ca3af" : Display(EvaluateLiteral(scale.ColorRange.NullColor)))
    };

    private FieldBinding Binding(AdvancedChartEncoding binding)
    {
        var channel = Channel(binding.Channel);
        var semanticKind = DataKind(binding.DataKind);
        var axis = Enum.Parse<AxisRole>(binding.Axis.ToString());
        var sort = binding.Sort == AdvancedChartSortDirection.Source ? SortDirection.None : Enum.Parse<SortDirection>(binding.Sort.ToString());
        if (binding.Source.Kind == AdvancedChartBindingSourceKind.Field)
            return new FieldBinding(channel, binding.Source.Field, semanticKind, binding.Scale, axis, sort, binding.Format)
            { Stack = Enum.Parse<StackMode>(binding.Stack.ToString()) };

        var expression = binding.Source.Constant ?? throw new InvalidOperationException("Constant binding has no value.");
        var value = EvaluateLiteral(expression);
        var parameter = expression is VariableExpression variable ? variable.Name : null;
        return binding.Source.Kind == AdvancedChartBindingSourceKind.Datum
            ? FieldBinding.Datum(channel, value, semanticKind, binding.Scale, axis, parameter) with
            { Sort = sort, Format = binding.Format, Stack = Enum.Parse<StackMode>(binding.Stack.ToString()) }
            : FieldBinding.Value(channel, value, semanticKind, parameter) with
            { Sort = sort, Format = binding.Format, Stack = Enum.Parse<StackMode>(binding.Stack.ToString()) };
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
    private static MarkKind Mark(AdvancedChartMarkKind value) => Enum.Parse<MarkKind>(value.ToString());
    private static FieldChannel Channel(AdvancedChartChannel value) => Enum.Parse<FieldChannel>(value.ToString());
    private static DataSemanticKind DataKind(AdvancedChartDataKind value) => Enum.Parse<DataSemanticKind>(value.ToString());
    private static ScaleKind Scale(AdvancedChartScaleKind value) => Enum.Parse<ScaleKind>(value.ToString());
    private static CoordinateKind Coordinate(AdvancedChartCoordinateKind value) => Enum.Parse<CoordinateKind>(value.ToString());
    private static ScaleResolutionMode Resolution(AdvancedChartResolutionMode value) => Enum.Parse<ScaleResolutionMode>(value.ToString());

}
