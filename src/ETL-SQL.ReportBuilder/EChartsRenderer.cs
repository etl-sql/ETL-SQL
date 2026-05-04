using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ETL_SQL.ReportBuilder.Renderers;

namespace ETL_SQL.ReportBuilder
{
    /// <summary>
    /// Converts a <see cref="VisualManifest"/> into an Apache ECharts option JSON string.
    /// This class now acts as a dispatcher to specialized renderers.
    /// </summary>
    public class EChartsRenderer
    {
        private readonly CartesianRenderer    _cartesian    = new();
        private readonly CircularRenderer     _circular     = new();
        private readonly HierarchicalRenderer _hierarchical = new();
        private readonly StatisticalRenderer  _statistical  = new();
        private readonly SpecializedRenderer  _specialized  = new();
        private readonly GeographicRenderer   _geographic   = new();

        /// <summary>
        /// Returns an ECharts option JSON string, or null for non-chart visual types.
        /// </summary>
        public string? Render(VisualManifest visual) =>
            visual.VisualType.ToUpperInvariant() switch
            {
                "BAR"                     => _cartesian.Render(visual, "bar"),
                "LINE"                    => _cartesian.Render(visual, "line"),
                "HBAR" or "HORIZONTALBAR" => _cartesian.RenderHorizontalBar(visual),
                "COMBO"                   => _cartesian.RenderCombo(visual),
                
                "PIE"                     => _circular.RenderPie(visual, donut: false),
                "DONUT"                   => _circular.RenderPie(visual, donut: true),
                
                "TREEMAP"                 => _hierarchical.RenderTreemap(visual),
                
                "BOXPLOT"                 => _statistical.RenderBoxPlot(visual),
                
                "SCATTER"                 => _specialized.RenderScatter(visual),
                "HEATMAP"                 => _specialized.RenderHeatMap(visual),
                "GAUGE"                   => _specialized.RenderGauge(visual),
                "FUNNEL"                  => _specialized.RenderFunnel(visual),
                "WATERFALL"               => _specialized.RenderWaterfall(visual),
                "BUBBLE"                  => _specialized.RenderBubble(visual),
                "RADAR"                   => _specialized.RenderRadar(visual),
                "CANDLESTICK"             => _specialized.RenderCandlestick(visual),

                "MAP"                     => _geographic.RenderMap(visual),

                _                         => null   // TABLE, CARD, SLICER, TEXT — rendered client-side
            };
    }
}
