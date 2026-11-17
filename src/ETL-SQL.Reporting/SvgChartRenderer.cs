using System;
using System.Collections.Generic;
using System.Text;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting;

/// <summary>
/// Generates static SVG chart images from a <see cref="VisualManifest"/>.
/// Used by <see cref="MarkdownRenderer"/> and <see cref="PdfExporter"/> to embed charts
/// without requiring a headless browser.
/// </summary>
public class SvgChartRenderer
{
    private static readonly HashSet<string> PlotPlanBackedVisualTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BAR", "HBAR", "HORIZONTALBAR", "LINE", "AREA", "PIE", "DONUT",
        "SCATTER", "BUBBLE", "HEATMAP", "FUNNEL", "GAUGE", "BOXPLOT",
        "WATERFALL", "CANDLESTICK", "TRELLIS", "GANTT", "RADAR", "COMBO", "CUSTOM"
    };

    private readonly PlotPlanSvgRenderer _plotPlan = new();
    private readonly SpecializedNativeSvgRenderer _specialized = new();

    public string Render(PlotPlan plan) => _plotPlan.Render(plan);

    public string? RenderFocused(VisualManifest visual, PlotBounds bounds) =>
        _specialized.Render(visual, bounds);

    public string? Render(VisualManifest v)
    {
        ArgumentNullException.ThrowIfNull(v);

        if (v.PlotPlan is not null)
            return _plotPlan.Render(v.PlotPlan);

        var visualType = (v.VisualType ?? string.Empty).Trim().ToUpperInvariant();
        switch (visualType)
        {
            case "TREEMAP":
            case "SUNBURST":
            case "SANKEY":
            case "NETWORK":
            case "MAP":
            case "MATRIX":
                return _specialized.Render(v);
            case "CARD":
            case "TABLE":
            case "SLICER":
            case "TEXT":
            case "HTML":
            case "IMAGE":
            case "DATEPICKER":
            case "RELDATEPICKER":
            case "SLIDER":
            case "MULTISELECT":
            case "SEARCH":
            case "CHECKBOX":
            case "TEXTBOX":
            case "NUMBERBOX":
                return null;
            default:
                if (PlotPlanBackedVisualTypes.Contains(visualType))
                {
                    throw new InvalidOperationException(
                        $"Visual '{v.Name}' of type '{v.VisualType}' cannot be rendered without a PlotPlan.");
                }

                throw new InvalidOperationException(
                    $"Visual '{v.Name}' has unsupported type '{v.VisualType}' and no PlotPlan.");
        }
    }
}
