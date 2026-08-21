using System.Collections.Immutable;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Tests.Reporting.GrammarOfGraphics;

internal static class GrammarOfGraphicsContractFixtures
{
    internal static ChartSpec ChartSpec() => ETL_SQL.Reporting.Semantics.ChartSpec.Create(
        id: "monthly-revenue",
        dataReference: "&monthly_revenue",
        bindings:
        [
            new FieldBinding(FieldChannel.X, "month", DataSemanticKind.Temporal, "x"),
            new FieldBinding(FieldChannel.Y, "revenue", DataSemanticKind.Quantitative, "y", AxisRole.Primary, Format: "C2"),
            new FieldBinding(FieldChannel.Color, "region", DataSemanticKind.Nominal, "color")
        ],
        layers:
        [
            new MarkLayerSpec("revenue-bars", MarkKind.Rect, 0, [], [new StyleToken("opacity", "0.9")]),
            new MarkLayerSpec("target-rule", MarkKind.Rule, 1,
                [new FieldBinding(FieldChannel.Y, "target", DataSemanticKind.Quantitative, "y")], [])
        ],
        coordinate: new CoordinateSpec(CoordinateKind.Cartesian),
        scales:
        [
            new ScaleSpec("x", FieldChannel.X, ScaleKind.Time, false, []),
            new ScaleSpec("y", FieldChannel.Y, ScaleKind.Linear, true, []),
            new ScaleSpec("color", FieldChannel.Color, ScaleKind.Ordinal, false, ["North", "South"])
        ],
        formatting: new FormattingSpec("en-US", "America/Chicago", "—", [new FieldFormat("revenue", "C2")]),
        nullHandling: new NullHandlingSpec(NullValuePolicy.Gap, [new FieldNullPolicy("target", NullValuePolicy.Skip)]),
        theme: new ThemeSpec("default", [new StyleToken("accent", "#2563eb")]),
        accessibility: new AccessibilitySpec(
            "Monthly revenue by region",
            "Bars show revenue and a rule shows the target.",
            "{series}: {value}",
            true),
        title: "Monthly Revenue",
        interactions: new InteractionSpec(
            [new SelectionSpec("region-selection", SelectionMode.Single, ["region"])],
            [new InteractionBinding("ON_SELECT", InteractionEffect.Filter, "detail-table")]));

    internal static ChartDataSet ChartData() => ChartDataSet.Create(
        "monthly_revenue",
        2,
        [
            new ChartColumn("count", ChartValueKind.Integer, DataSemanticKind.Quantitative,
                [ChartValue.From(7L), ChartValue.From(9L)], ["7", "9"]),
            new ChartColumn("ratio", ChartValueKind.FloatingPoint, DataSemanticKind.Quantitative,
                [ChartValue.From(0.25d), ChartValue.Null()], ["25%", null]),
            new ChartColumn("revenue", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                [ChartValue.From(1234.50m), ChartValue.From(987.65m)], ["$1,234.50", "$987.65"]),
            new ChartColumn("region", ChartValueKind.Text, DataSemanticKind.Nominal,
                [ChartValue.From("North"), ChartValue.From("South")], []),
            new ChartColumn("priority", ChartValueKind.Text, DataSemanticKind.Ordinal,
                [ChartValue.From("High"), ChartValue.From("Low")], []),
            new ChartColumn("day", ChartValueKind.Date, DataSemanticKind.Temporal,
                [ChartValue.From(new DateOnly(2026, 8, 20)), ChartValue.From(new DateOnly(2026, 8, 21))], []),
            new ChartColumn("time", ChartValueKind.Time, DataSemanticKind.Temporal,
                [ChartValue.From(new TimeOnly(9, 30)), ChartValue.From(new TimeOnly(10, 45))], []),
            new ChartColumn("local", ChartValueKind.LocalDateTime, DataSemanticKind.Temporal,
                [ChartValue.FromLocal(new DateTime(2026, 8, 20, 9, 30, 0, DateTimeKind.Unspecified)), ChartValue.FromLocal(new DateTime(2026, 8, 21, 10, 45, 0, DateTimeKind.Unspecified))], []),
            new ChartColumn("instant", ChartValueKind.OffsetDateTime, DataSemanticKind.Temporal,
                [ChartValue.From(new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.FromHours(-5))), ChartValue.From(new DateTimeOffset(2026, 8, 21, 10, 45, 0, TimeSpan.FromHours(2)))], []),
            new ChartColumn("active", ChartValueKind.Boolean, DataSemanticKind.Nominal,
                [ChartValue.From(true), ChartValue.From(false)], ["Yes", "No"])
        ]);

    internal static PlotPlan PlotPlan() => ETL_SQL.Reporting.Semantics.PlotPlan.Create(
        specId: "monthly-revenue",
        title: "Monthly Revenue",
        bounds: new PlotBounds(40m, 20m, 720m, 360m),
        scales:
        [
            new ResolvedScale("x", FieldChannel.X, ScaleKind.Band,
                [ChartValue.From("2026-07"), ChartValue.From("2026-08")], ["2026-07", "2026-08"],
                [new PlotTick(ChartValue.From("2026-07"), "Jul"), new PlotTick(ChartValue.From("2026-08"), "Aug")], false),
            new ResolvedScale("y", FieldChannel.Y, ScaleKind.Linear,
                [ChartValue.From(0m), ChartValue.From(1500m)], [],
                [new PlotTick(ChartValue.From(0m), "$0"), new PlotTick(ChartValue.From(1500m), "$1,500")], true)
        ],
        series:
        [
            new ResolvedSeries("North", "North", 0, "#2563eb"),
            new ResolvedSeries("South", "South", 1, "#dc2626")
        ],
        palette:
        [
            new PaletteAssignment("North", "#2563eb"),
            new PaletteAssignment("South", "#dc2626")
        ],
        legend:
        [
            new LegendEntry("North", "North", 0, "#2563eb"),
            new LegendEntry("South", "South", 1, "#dc2626")
        ],
        layers:
        [
            new ResolvedMarkLayer("revenue-bars", MarkKind.Rect, 0, null,
            [
                new ResolvedDatum(0,
                    [new ResolvedChannelValue(FieldChannel.X, ChartValue.From("2026-07"), "Jul"), new ResolvedChannelValue(FieldChannel.Y, ChartValue.From(1234.50m), "$1,234.50")], false, "North: $1,234.50"),
                new ResolvedDatum(1,
                    [new ResolvedChannelValue(FieldChannel.X, ChartValue.From("2026-08"), "Aug"), new ResolvedChannelValue(FieldChannel.Y, ChartValue.Null(), "—")], true, "South: —")
            ]),
            new ResolvedMarkLayer("target-rule", MarkKind.Rule, 1, null,
            [
                new ResolvedDatum(0, [new ResolvedChannelValue(FieldChannel.Y, ChartValue.From(1000m), "$1,000")], false, "Target: $1,000")
            ])
        ],
        nulls: new ResolvedNullPolicy(NullValuePolicy.Gap, [], [1], []),
        accessibleSummary: "North revenue was $1,234.50; South revenue is missing. Target was $1,000.",
        fallback: new SemanticFallback(SemanticFallbackKind.RankedTable, "Monthly Revenue",
            [new SemanticFallbackItem("North", "$1,234.50", 0), new SemanticFallbackItem("South", "—", 1)]));
}
