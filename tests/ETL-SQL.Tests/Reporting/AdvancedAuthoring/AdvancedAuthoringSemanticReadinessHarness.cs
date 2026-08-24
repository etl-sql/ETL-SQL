using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Tests.Reporting.AdvancedAuthoring;

public record SemanticCapabilityItem(
    string Concept,
    string Category,
    bool IsSupportedInContracts,
    bool IsSupportedInRenderers,
    bool IsExposedInReportSql,
    string StatusSummary,
    string TechnicalGapDetail);

public record AdvancedAuthoringReadinessReport(
    DateTime GeneratedAtUtc,
    string GitBranch,
    IReadOnlyList<SemanticCapabilityItem> CapabilityInventory,
    IReadOnlyDictionary<string, string> SurfaceConformanceMatrix,
    IReadOnlyList<string> ArchitecturalReadinessSummary);

public static class AdvancedAuthoringSemanticReadinessHarness
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static IReadOnlyList<SemanticCapabilityItem> GetCapabilityInventory()
    {
        return new List<SemanticCapabilityItem>
        {
            new(
                Concept: "Ordered Multi-Layer Marks (RECT, LINE, POINT, RULE, AREA, ARC, TICK)",
                Category: "Layer Composition",
                IsSupportedInContracts: true,
                IsSupportedInRenderers: true,
                IsExposedInReportSql: true,
                StatusSummary: "Supported by CUSTOM CHART layers, versioned ChartSpec/PlotPlan contracts, and all semantic fallback surfaces",
                TechnicalGapDetail: "Layer data-source overrides remain deliberately separate; all layers in one chart use the root staged source"
            ),
            new(
                Concept: "Dual-Axis Bindings (Primary Y vs Secondary Y2)",
                Category: "Scales & Axes",
                IsSupportedInContracts: true,
                IsSupportedInRenderers: true,
                IsExposedInReportSql: true,
                StatusSummary: "Supported in contracts and exposed via COMBO (Y on Left, Y2 on Right)",
                TechnicalGapDetail: "Fully functional in ChartSpec, ECharts, and SVG renderers; terminal renderer provides placeholder/table fallback"
            ),
            new(
                Concept: "Scale Resolution Policies (Shared vs Independent)",
                Category: "Faceting & Resolution",
                IsSupportedInContracts: true,
                IsSupportedInRenderers: true,
                IsExposedInReportSql: true,
                StatusSummary: "Shared and independent X/Y/color scales resolve per facet in PlotPlanResolver",
                TechnicalGapDetail: "Independent offset-scale policy is not separately exposed; offset slots remain stable across panels"
            ),
            new(
                Concept: "1D and 2D Facet Grid Specifications",
                Category: "Faceting & Resolution",
                IsSupportedInContracts: true,
                IsSupportedInRenderers: true,
                IsExposedInReportSql: true,
                StatusSummary: "ROW/COLUMN grids and one-dimensional WRAP resolve deterministic bounded panels",
                TechnicalGapDetail: "Wrap is intentionally mutually exclusive with ROW/COLUMN grids"
            ),
            new(
                Concept: "Coordinate Systems (Cartesian, Transposed, Polar)",
                Category: "Coordinates",
                IsSupportedInContracts: true,
                IsSupportedInRenderers: true,
                IsExposedInReportSql: true,
                StatusSummary: "Fully supported across contracts, ECharts, and SVG renderers (BAR, HBAR, PIE, DONUT)",
                TechnicalGapDetail: "Complete across contracts and native SVG; terminal renderer maps Polar to proportional Spectre breakdown tables"
            ),
            new(
                Concept: "Layer-Level Independent Data Source Overrides",
                Category: "Layer Composition",
                IsSupportedInContracts: false,
                IsSupportedInRenderers: false,
                IsExposedInReportSql: false,
                StatusSummary: "Absent in contracts; all layers currently bind to the single root dataReference",
                TechnicalGapDetail: "MarkLayerSpec does not contain an optional DataReference override property"
            ),
            new(
                Concept: "Typed Field, Datum, and Visual-Value Binding Sources",
                Category: "Data Transformation",
                IsSupportedInContracts: true,
                IsSupportedInRenderers: true,
                IsExposedInReportSql: true,
                StatusSummary: "Bare fields, scaled DATUM constants/parameters, and unscaled VALUE constants are supported",
                TechnicalGapDetail: "Arbitrary expressions and transformations remain intentionally rejected and must be staged in SQL"
            ),
            new(
                Concept: "Conditional Visual Mark Encodings",
                Category: "Encodings & Styles",
                IsSupportedInContracts: true,
                IsSupportedInRenderers: true,
                IsExposedInReportSql: true,
                StatusSummary: "Typed conditional color, opacity, size, shape, and text encodings resolve once into PlotPlan",
                TechnicalGapDetail: "Connected LINE/AREA marks reject row-level conditions to avoid ambiguous segment semantics"
            ),
            new(
                Concept: "Accessible Semantic Fallbacks & Plain-Text Summaries",
                Category: "Accessibility & Governance",
                IsSupportedInContracts: true,
                IsSupportedInRenderers: true,
                IsExposedInReportSql: true,
                StatusSummary: "Fully supported via SemanticFallback and AccessibleSummary generation in PlotPlanResolver",
                TechnicalGapDetail: "Production-ready: produces deterministic structured text tables and screen-reader narratives"
            )
        };
    }

    public static ChartSpec CreateMultiLayerSpec()
    {
        return ChartSpec.Create(
            id: "advanced-composite-chart",
            dataReference: "&telemetry_data",
            bindings:
            [
                new FieldBinding(FieldChannel.X, "step", DataSemanticKind.Nominal, "scale_x"),
                new FieldBinding(FieldChannel.Y, "volume", DataSemanticKind.Quantitative, "scale_y", AxisRole.Primary),
                new FieldBinding(FieldChannel.Y2, "efficiency", DataSemanticKind.Quantitative, "scale_y2", AxisRole.Secondary)
            ],
            layers:
            [
                new MarkLayerSpec("layer-rect-bars", MarkKind.Rect, 0,
                    [new FieldBinding(FieldChannel.Y, "volume", DataSemanticKind.Quantitative, "scale_y")],
                    [new StyleToken("opacity", "0.85")]),
                new MarkLayerSpec("layer-line-trend", MarkKind.Line, 1,
                    [new FieldBinding(FieldChannel.Y2, "efficiency", DataSemanticKind.Quantitative, "scale_y2")],
                    [new StyleToken("strokeWidth", "2.5")]),
                new MarkLayerSpec("layer-point-markers", MarkKind.Point, 2,
                    [new FieldBinding(FieldChannel.Y2, "efficiency", DataSemanticKind.Quantitative, "scale_y2")],
                    [new StyleToken("pointSize", "6")]),
                new MarkLayerSpec("layer-rule-target", MarkKind.Rule, 3,
                    [new FieldBinding(FieldChannel.Y, "benchmark", DataSemanticKind.Quantitative, "scale_y")],
                    [new StyleToken("strokeDash", "4 4")])
            ],
            coordinate: new CoordinateSpec(CoordinateKind.Cartesian),
            scales:
            [
                new ScaleSpec("scale_x", FieldChannel.X, ScaleKind.Band, false, ["S1", "S2", "S3", "S4"]),
                new ScaleSpec("scale_y", FieldChannel.Y, ScaleKind.Linear, true, []),
                new ScaleSpec("scale_y2", FieldChannel.Y2, ScaleKind.Linear, false, [])
            ],
            formatting: new FormattingSpec("en-US", "America/New_York", "—", [new FieldFormat("volume", "N0")]),
            nullHandling: new NullHandlingSpec(NullValuePolicy.Gap, []),
            theme: new ThemeSpec("corporate", [new StyleToken("accent", "#1E3A8A")]),
            accessibility: new AccessibilitySpec(
                "Composite Production Velocity & Quality",
                "Four-layer chart showing volume bars, efficiency trend line, marker points, and benchmark rule.",
                "{category}: Volume {volume}, Efficiency {efficiency}",
                true),
            title: "Composite Telemetry Performance",
            scaleResolution: new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Independent),
            facet: new FacetSpec("Region", "Year", new ScaleResolutionSpec(ScaleResolutionMode.Shared, ScaleResolutionMode.Independent))
        );
    }

    public static ChartDataSet CreateMultiLayerDataSet()
    {
        return ChartDataSet.Create(
            "telemetry_data",
            4,
            [
                new ChartColumn("step", ChartValueKind.Text, DataSemanticKind.Nominal,
                    [ChartValue.From("S1"), ChartValue.From("S2"), ChartValue.From("S3"), ChartValue.From("S4")], []),
                new ChartColumn("volume", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                    [ChartValue.From(1200m), ChartValue.From(1450m), ChartValue.From(1100m), ChartValue.From(1800m)], ["1,200", "1,450", "1,100", "1,800"]),
                new ChartColumn("efficiency", ChartValueKind.FloatingPoint, DataSemanticKind.Quantitative,
                    [ChartValue.From(0.88d), ChartValue.From(0.92d), ChartValue.From(0.85d), ChartValue.From(0.96d)], ["88%", "92%", "85%", "96%"]),
                new ChartColumn("benchmark", ChartValueKind.Decimal, DataSemanticKind.Quantitative,
                    [ChartValue.From(1500m), ChartValue.From(1500m), ChartValue.From(1500m), ChartValue.From(1500m)], ["1,500", "1,500", "1,500", "1,500"])
            ]);
    }

    public static AdvancedAuthoringReadinessReport GenerateReadinessReport()
    {
        var capabilities = GetCapabilityInventory();

        var surfaceMatrix = new Dictionary<string, string>
        {
            ["ECharts Browser Runtime"] = "Full support for Cartesian, Transposed, Polar, Multi-Layer, Dual-Axis, and Series Palette.",
            ["Native PlotPlan SVG"] = "Full support for Native Vector Cartesian, Dual-Axis, Lines, Rects, Points, Rules, Arcs, Micro-Sparklines, and Micro-Progress without browser/V8 dependencies.",
            ["Spectre Terminal Output"] = "Support for Braille Continuous Curves, Bar Panels, Slicers, and Semantic Plain-Text Breakdown Tables.",
            ["Accessible Screen-Reader Fallbacks"] = "Deterministic SemanticFallbackItem tables, Summary narratives, and GFM tables."
        };

        var summary = new List<string>
        {
            "ChartSpec and PlotPlan provide a robust semantic foundation capable of multi-layer composition, dual-scale coordinates, and rich accessibility.",
            "Primary semantic gaps for Phase 7 are at the authoring/syntax layer (Report-SQL grammar for layer definitions) and client-side reactive facet splitting.",
            "Existing rendering pipelines (ECharts and Native SVG) cleanly decouple from Report-SQL syntax and can consume future advanced authoring specs without architectural redesign."
        };

        return new AdvancedAuthoringReadinessReport(
            GeneratedAtUtc: DateTime.UtcNow,
            GitBranch: "test/reporting-phase7-semantic-readiness",
            CapabilityInventory: capabilities,
            SurfaceConformanceMatrix: surfaceMatrix,
            ArchitecturalReadinessSummary: summary);
    }

    public static string FormatMarkdownReport(AdvancedAuthoringReadinessReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Phase 7 Semantic Authoring Readiness & Capability Inventory");
        sb.AppendLine();
        sb.AppendLine($"> **Timestamp (UTC):** {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} | **Branch:** `{report.GitBranch}`");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 1. Semantic Capability & Gap Inventory");
        sb.AppendLine();
        sb.AppendLine("| Concept | Category | Contracts | Renderers | Report-SQL | Status & Technical Detail |");
        sb.AppendLine("| :--- | :--- | :---: | :---: | :---: | :--- |");

        foreach (var cap in report.CapabilityInventory)
        {
            var cBadge = cap.IsSupportedInContracts ? "✅" : "❌";
            var rBadge = cap.IsSupportedInRenderers ? "✅" : "❌";
            var sBadge = cap.IsExposedInReportSql ? "✅" : "❌";
            sb.AppendLine($"| **{cap.Concept}** | `{cap.Category}` | {cBadge} | {rBadge} | {sBadge} | **{cap.StatusSummary}**<br/>_{cap.TechnicalGapDetail}_ |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 2. Multi-Surface Semantic Conformance Matrix");
        sb.AppendLine();
        sb.AppendLine("| Rendering Surface | Operational Conformance Level |");
        sb.AppendLine("| :--- | :--- |");

        foreach (var (surface, status) in report.SurfaceConformanceMatrix)
        {
            sb.AppendLine($"| **{surface}** | {status} |");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 3. Key Architectural Findings");
        sb.AppendLine();

        foreach (var point in report.ArchitecturalReadinessSummary)
        {
            sb.AppendLine($"- {point}");
        }

        return sb.ToString();
    }

    public static string FormatJsonReport(AdvancedAuthoringReadinessReport report)
    {
        return JsonSerializer.Serialize(report, JsonOpts);
    }
}
