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
        var layers = chart.Layers.Select(Layer).ToImmutableArray();
        var bindings = layers.SelectMany(layer => layer.Bindings)
            .GroupBy(binding => (binding.Channel, binding.Field), new BindingKeyComparer())
            .Select(group => group.First()).ToList();
        if (chart.Facet?.RowField is { } row)
            bindings.Add(new FieldBinding(FieldChannel.Row, row, DataSemanticKind.Nominal));
        if (chart.Facet?.ColumnField is { } column)
            bindings.Add(new FieldBinding(FieldChannel.Column, column, DataSemanticKind.Nominal));

        var resolution = new ScaleResolutionSpec(
            Resolution(chart.Resolution.X), Resolution(chart.Resolution.Y), Resolution(chart.Resolution.Color));
        var title = manifest.Options.GetValueOrDefault("title") ?? manifest.Name;
        var spec = ChartSpec.Create(
            manifest.Name,
            statement.Source.TempTableName ?? $"inline:{manifest.Name}",
            bindings.ToImmutableArray(),
            layers,
            new CoordinateSpec(Coordinate(chart.Coordinate.Kind), chart.Coordinate.StartAngle,
                chart.Coordinate.EndAngle, chart.Coordinate.InnerRadius),
            chart.Scales.Select(Scale).ToImmutableArray(),
            new FormattingSpec(CultureInfo.InvariantCulture.Name, "UTC", "",
                layers.SelectMany(layer => layer.Bindings)
                    .Where(binding => binding.Format is not null)
                    .Select(binding => new FieldFormat(binding.Field, binding.Format))
                    .Distinct().ToImmutableArray()),
            new NullHandlingSpec(NullValuePolicy.Gap, []),
            new ThemeSpec(manifest.Styles?.GetValueOrDefault("THEME") ?? "default", []),
            new AccessibilitySpec(title, manifest.Options.GetValueOrDefault("subtitle"), null, true),
            title,
            resolution,
            chart.Facet is null ? null : new FacetSpec(chart.Facet.RowField, chart.Facet.ColumnField, resolution));
        spec.Validate();
        return spec;
    }

    private MarkLayerSpec Layer(AdvancedChartLayer layer) => new(
        layer.Name, Mark(layer.Mark), layer.ZIndex,
        layer.Encodings.Select(Binding).ToImmutableArray(),
        layer.Styles.Select(style => new StyleToken(style.Name, Display(EvaluateLiteral(style.Value)))).ToImmutableArray(),
        layer.Name)
    {
        Conditions = layer.Conditions.Select(Condition).ToImmutableArray()
    };

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
        return Value(context.VarContext.GetVariable(name));
    }

    private ScaleSpec Scale(AdvancedChartScale scale) => new(
        scale.Name, Channel(scale.Channel), Scale(scale.Kind), scale.IncludeZero,
        scale.ExplicitOrder.Select(item => Display(EvaluateLiteral(item))).ToImmutableArray(),
        scale.Minimum is null ? null : EvaluateLiteral(scale.Minimum),
        scale.Maximum is null ? null : EvaluateLiteral(scale.Maximum));

    private static FieldBinding Binding(AdvancedChartEncoding binding) => new(
        Channel(binding.Channel), binding.Field, DataKind(binding.DataKind), binding.Scale,
        Enum.Parse<AxisRole>(binding.Axis.ToString()),
        binding.Sort == AdvancedChartSortDirection.Source ? SortDirection.None : Enum.Parse<SortDirection>(binding.Sort.ToString()),
        binding.Format);

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

    private sealed class BindingKeyComparer : IEqualityComparer<(FieldChannel Channel, string Field)>
    {
        public bool Equals((FieldChannel Channel, string Field) x, (FieldChannel Channel, string Field) y) =>
            x.Channel == y.Channel && x.Field.Equals(y.Field, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((FieldChannel Channel, string Field) obj) => HashCode.Combine(obj.Channel, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Field));
    }
}
