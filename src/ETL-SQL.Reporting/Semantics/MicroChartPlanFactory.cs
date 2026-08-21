using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Reporting.Renderers;

namespace ETL_SQL.Reporting.Semantics.Runtime;

internal sealed record MicroChartSemanticBundle(ChartSpec Spec, ChartDataSet Data, PlotPlan Plan, string PlainText);

internal sealed class MicroChartPlanFactory
{
    private static readonly ImmutableArray<StyleToken> EmptyStyle = [];

    public MicroChartSemanticBundle CreateSparkline(
        string id,
        IReadOnlyList<decimal?> values,
        string type = "line",
        string? color = null,
        IReadOnlyList<string?>? labels = null,
        decimal width = 160m,
        decimal height = 42m)
    {
        var mark = type.ToUpperInvariant() switch
        {
            "BAR" => MarkKind.Rect,
            "AREA" => MarkKind.Area,
            _ => MarkKind.Line
        };
        var xValues = Enumerable.Range(0, values.Count).Select(index => ChartValue.From((long)index)).ToImmutableArray();
        var yValues = values.Select(value => value.HasValue ? ChartValue.From(value.Value) : ChartValue.Null()).ToImmutableArray();
        var display = labels?.Select(value => value).ToImmutableArray() ?? Enumerable.Range(0, values.Count).Select(index => (string?)index.ToString(CultureInfo.InvariantCulture)).ToImmutableArray();
        var data = ChartDataSet.Create($"micro:{id}", values.Count,
        [
            new ChartColumn("x", ChartValueKind.Integer, DataSemanticKind.Ordinal, xValues, display),
            new ChartColumn("value", ChartValueKind.Decimal, DataSemanticKind.Quantitative, yValues,
                values.Select(value => value?.ToString("0.##", CultureInfo.InvariantCulture)).ToImmutableArray())
        ]);
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "x", DataSemanticKind.Ordinal, "x"),
            new FieldBinding(FieldChannel.Y, "value", DataSemanticKind.Quantitative, "y", AxisRole.Primary));
        var theme = new ThemeSpec("micro", [
            new StyleToken("MICRO_CHART", "SPARKLINE"),
            new StyleToken("COLOR", SafeColor(color, "#5470c6"))
        ]);
        var spec = ChartSpec.Create(id, data.Name, bindings,
            [new MarkLayerSpec("primary", mark, 0, bindings, EmptyStyle)],
            new CoordinateSpec(CoordinateKind.Cartesian),
            [
                new ScaleSpec("x", FieldChannel.X, ScaleKind.Point, false, []),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [])
            ],
            new FormattingSpec(CultureInfo.InvariantCulture.Name, "UTC", "", []),
            new NullHandlingSpec(NullValuePolicy.Gap, []), theme,
            new AccessibilitySpec($"Trend for {id}", null, null, true));
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, width, height));
        var populated = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        var fallback = populated.Count == 0
            ? "Trend: no data"
            : $"Trend: first {populated[0]:0.##}, last {populated[^1]:0.##}, range {populated.Min():0.##}–{populated.Max():0.##}";
        return new MicroChartSemanticBundle(spec, data, plan, fallback);
    }

    public MicroChartSemanticBundle CreateProgress(
        string id, decimal value, decimal minimum, decimal maximum, string? color = null,
        decimal width = 160m, decimal height = 24m)
    {
        if (maximum <= minimum) maximum = minimum + 1m;
        var bounded = Math.Clamp(value, minimum, maximum);
        var data = ChartDataSet.Create($"micro:{id}", 1,
        [
            new ChartColumn("category", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("value")], ["value"]),
            new ChartColumn("value", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(bounded)], [bounded.ToString("0.##", CultureInfo.InvariantCulture)])
        ]);
        var bindings = ImmutableArray.Create(
            new FieldBinding(FieldChannel.X, "category", DataSemanticKind.Nominal, "x"),
            new FieldBinding(FieldChannel.Y, "value", DataSemanticKind.Quantitative, "y", AxisRole.Primary));
        var spec = ChartSpec.Create(id, data.Name, bindings,
            [new MarkLayerSpec("primary", MarkKind.Rect, 0, bindings, EmptyStyle)],
            new CoordinateSpec(CoordinateKind.TransposedCartesian),
            [
                new ScaleSpec("x", FieldChannel.X, ScaleKind.Band, false, ["value"]),
                new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, false, [], ChartValue.From(minimum), ChartValue.From(maximum))
            ],
            new FormattingSpec(CultureInfo.InvariantCulture.Name, "UTC", "", []),
            new NullHandlingSpec(NullValuePolicy.Zero, []),
            new ThemeSpec("micro", [
                new StyleToken("MICRO_CHART", "PROGRESS"),
                new StyleToken("COLOR", SafeColor(color, "#3ba272"))
            ]),
            new AccessibilitySpec($"Progress for {id}", null, null, true));
        var plan = new PlotPlanResolver().Resolve(spec, data, new PlotBounds(0, 0, width, height));
        var percent = (bounded - minimum) / (maximum - minimum) * 100m;
        return new MicroChartSemanticBundle(spec, data, plan,
            $"Progress: {bounded:0.##} of {maximum:0.##} ({percent:0.#}%)");
    }

    public MicroChartManifest ToManifest(MicroChartSemanticBundle bundle, string kind, string role,
        int? rowIndex = null, int? columnIndex = null, string? sourceValue = null)
        => new()
        {
            Id = bundle.Spec.Id,
            Kind = kind,
            Role = role,
            RowIndex = rowIndex,
            ColumnIndex = columnIndex,
            SourceValue = sourceValue,
            PlotPlan = bundle.Plan,
            Svg = new PlotPlanSvgRenderer().Render(bundle.Plan),
            PlainText = bundle.PlainText,
            AccessibleLabel = bundle.Plan.AccessibleSummary
        };

    private static string SafeColor(string? candidate, string fallback)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return fallback;
        var value = candidate.Trim();
        if (value.Length is 4 or 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)) return value;
        return fallback;
    }
}
