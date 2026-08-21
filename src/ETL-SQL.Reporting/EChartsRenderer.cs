using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.Reporting.Renderers;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting
{
    /// <summary>
    /// Converts a <see cref="VisualManifest"/> into an Apache ECharts option JSON string.
    /// This class now acts as a dispatcher to specialized renderers.
    /// </summary>
    public class EChartsRenderer
    {
        private readonly CartesianRenderer _cartesian = new();
        private readonly CircularRenderer _circular = new();
        private readonly HierarchicalRenderer _hierarchical = new();
        private readonly StatisticalRenderer _statistical = new();
        private readonly SpecializedRenderer _specialized = new();
        private readonly GeographicRenderer _geographic = new();
        private readonly PlotPlanEChartsRenderer _plotPlan = new();

        public string Render(PlotPlan plan) => _plotPlan.Render(plan);

        /// <summary>
        /// Returns an ECharts option JSON string, or null for non-chart visual types.
        /// </summary>
        public string? Render(VisualManifest visual) => visual.PlotPlan is not null
            ? _plotPlan.Render(visual.PlotPlan)
            : visual.VisualType.ToUpperInvariant() switch
            {
                "BAR" => _cartesian.Render(visual, "bar"),
                "LINE" => _cartesian.Render(visual, "line"),
                "HBAR" or "HORIZONTALBAR" => _cartesian.RenderHorizontalBar(visual),
                "COMBO" => _cartesian.RenderCombo(visual),

                "PIE" => _circular.RenderPie(visual, donut: false),
                "DONUT" => _circular.RenderPie(visual, donut: true),

                "TREEMAP" => _hierarchical.RenderTreemap(visual),

                "BOXPLOT" => _statistical.RenderBoxPlot(visual),

                "SCATTER" => _specialized.RenderScatter(visual),
                "HEATMAP" => _specialized.RenderHeatMap(visual),
                "GAUGE" => _specialized.RenderGauge(visual),
                "FUNNEL" => _specialized.RenderFunnel(visual),
                "WATERFALL" => _specialized.RenderWaterfall(visual),
                "BUBBLE" => _specialized.RenderBubble(visual),
                "RADAR" => _specialized.RenderRadar(visual),
                "CANDLESTICK" => _specialized.RenderCandlestick(visual),
                "GANTT" => _specialized.RenderGantt(visual),
                "SANKEY" => _specialized.RenderSankey(visual),
                "SUNBURST" => _specialized.RenderSunburst(visual),
                "NETWORK" => _specialized.RenderNetwork(visual),
                "TRELLIS" => _specialized.RenderTrellis(visual),
                "MATRIX" => _specialized.RenderMatrix(visual),

                "MAP" => _geographic.RenderMap(visual),

                _ => null   // TABLE, CARD, SLICER, TEXT — rendered client-side
            };
    }
}
