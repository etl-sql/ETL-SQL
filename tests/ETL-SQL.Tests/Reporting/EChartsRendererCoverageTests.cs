using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Directly exercises EChartsRenderer dispatch to SpecializedRenderer,
    /// StatisticalRenderer, HierarchicalRenderer, and OverlayRenderer by
    /// constructing VisualManifest objects without DashboardService overhead.
    /// </summary>
    public class EChartsRendererCoverageTests
    {
        private static EChartsRenderer R() => new EChartsRenderer();

        private static VisualManifest V(string name, string type,
            string[] cols, string[][]? rows = null, Dictionary<string, string>? opts = null)
        {
            var v = new VisualManifest { Name = name, VisualType = type };
            v.Columns.AddRange(cols);
            if (rows != null)
                v.Rows.AddRange(rows.Select(r => r.Select(c => (string?)c).ToList()));
            if (opts != null)
                foreach (var (k, val) in opts)
                    v.Options[k] = val;
            return v;
        }

        private static OverlayManifest Ov(string type, double? param = null,
            string lineStyle = "dashed", string? color = null, string? label = null) =>
            new OverlayManifest { OverlayType = type, Parameter = param, LineStyle = lineStyle, Color = color, Label = label };

        // ── SpecializedRenderer ───────────────────────────────────────────────

        [Fact]
        public void Scatter_ProducesJsonWithScatterType()
        {
            var v = V("S", "SCATTER", new[] { "X", "Y" }, new[]
            {
                new[] { "1", "10" }, new[] { "2", "20" }, new[] { "3", "15" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"scatter\"", json);
        }

        [Fact]
        public void HeatMap_ProducesJsonWithHeatmapType()
        {
            var v = V("H", "HEATMAP", new[] { "X", "Y", "Val" }, new[]
            {
                new[] { "A", "Mon", "5" }, new[] { "B", "Mon", "10" },
                new[] { "A", "Tue", "8" }, new[] { "B", "Tue", "3" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"heatmap\"", json);
        }

        [Fact]
        public void Gauge_DefaultStyle_ProducesGaugeSeries()
        {
            var v = V("G", "GAUGE", new[] { "Value", "Label" }, new[] { new[] { "75", "Score" } });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"gauge\"", json);
        }

        [Fact]
        public void Gauge_WithMinMax_AppliesOptions()
        {
            var v = V("G", "GAUGE", new[] { "Value" }, new[] { new[] { "50" } },
                new Dictionary<string, string> { ["MIN"] = "0", ["MAX"] = "200" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("200", json);
        }

        [Fact]
        public void Gauge_ProgressStyle_IncludesProgressSection()
        {
            var v = V("G", "GAUGE", new[] { "Value" }, new[] { new[] { "40" } },
                new Dictionary<string, string> { ["GAUGE_STYLE"] = "PROGRESS" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"progress\"", json);
        }

        [Fact]
        public void Gauge_SemiCircleStyle_IncludesStartAngle()
        {
            var v = V("G", "GAUGE", new[] { "Value" }, new[] { new[] { "60" } },
                new Dictionary<string, string> { ["GAUGE_STYLE"] = "SEMI_CIRCLE" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"startAngle\"", json);
        }

        [Fact]
        public void Gauge_RingStyle_HasEndAngleMinus270()
        {
            var v = V("G", "GAUGE", new[] { "Value" }, new[] { new[] { "30" } },
                new Dictionary<string, string> { ["GAUGE_STYLE"] = "RING" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("-270", json);
        }

        [Fact]
        public void Funnel_ProducesJsonWithFunnelType()
        {
            var v = V("F", "FUNNEL", new[] { "Stage", "Count" }, new[]
            {
                new[] { "Lead", "100" }, new[] { "Opportunity", "60" }, new[] { "Close", "20" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"funnel\"", json);
        }

        [Fact]
        public void Waterfall_ProducesJsonWithTransparentBase()
        {
            var v = V("W", "WATERFALL", new[] { "Item", "Amount" }, new[]
            {
                new[] { "Start", "200" }, new[] { "Add", "50" }, new[] { "Loss", "-30" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("transparent", json);
        }

        [Fact]
        public void Bubble_ProducesJsonWithBubbleSymbolSizeMarker()
        {
            var v = V("B", "BUBBLE", new[] { "X", "Y", "Size", "Label" }, new[]
            {
                new[] { "1", "2", "10", "Alpha" }, new[] { "3", "4", "20", "Beta" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("__bubbleSymbolSize", json);
        }

        [Fact]
        public void Bubble_NoSizeColumn_UsesUniformSize()
        {
            var v = V("B", "BUBBLE", new[] { "X", "Y" }, new[]
            {
                new[] { "1", "2" }, new[] { "3", "4" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"scatter\"", json);
        }

        [Fact]
        public void Radar_ProducesJsonWithRadarSection()
        {
            var v = V("R", "RADAR", new[] { "Series", "Speed", "Power", "Defense" }, new[]
            {
                new[] { "Fighter", "80", "70", "60" },
                new[] { "Mage",    "40", "95", "50" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"radar\"", json);
        }

        [Fact]
        public void Radar_WithMaxOption_AppliesMaxToIndicators()
        {
            var v = V("R", "RADAR", new[] { "Series", "Metric" }, new[]
            {
                new[] { "Alpha", "60" }
            }, new Dictionary<string, string> { ["MAX"] = "100", ["MIN"] = "10" });
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Radar_OneColumn_ReturnsMinimalJson()
        {
            var v = V("R", "RADAR", new[] { "Series" }, new[] { new[] { "Fighter" } });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.DoesNotContain("\"radar\"", json);
        }

        [Fact]
        public void Candlestick_ProducesJsonWithCandlestickType()
        {
            var v = V("C", "CANDLESTICK", new[] { "Date", "Open", "High", "Low", "Close" }, new[]
            {
                new[] { "Jan", "100", "110", "95",  "105" },
                new[] { "Feb", "105", "115", "100", "108" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"candlestick\"", json);
        }

        [Fact]
        public void Candlestick_WithColorOptions_AppliesColors()
        {
            var v = V("C", "CANDLESTICK", new[] { "Date", "Open", "High", "Low", "Close" }, new[]
            {
                new[] { "Jan", "100", "110", "95", "105" }
            }, new Dictionary<string, string> { ["COLOR_UP"] = "#00FF00", ["COLOR_DOWN"] = "#FF0000" });
            var json = R().Render(v);
            Assert.Contains("#00FF00", json);
        }

        [Fact]
        public void Scatter_WithBrushOptions_EmitsRuntimeBrushMarkers()
        {
            var v = V("S", "SCATTER", new[] { "X", "Y" }, new[]
            {
                new[] { "1", "10" }, new[] { "2", "20" }
            }, new Dictionary<string, string>
            {
                ["BRUSH"] = "ON",
                ["BRUSH_PARAM"] = "@selectedX",
                ["BRUSH_TYPE"] = "polygon"
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("__brushParam", json);
            Assert.Contains("@selectedX", json);
            Assert.Contains("polygon", json);
        }

        [Fact]
        public void Gantt_ProducesCustomSeriesWithRuntimeRenderMarker()
        {
            var v = V("G", "GANTT", new[] { "Task", "Start", "End", "Color" }, new[]
            {
                new[] { "Extract", "2026-01-01", "2026-01-03", "#5470c6" },
                new[] { "Load", "2026-01-04", "2026-01-06", "#91cc75" }
            }, new Dictionary<string, string>
            {
                ["mapping:y"] = "Task",
                ["mapping:start"] = "Start",
                ["mapping:end"] = "End",
                ["mapping:color"] = "Color"
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"custom\"", json);
            Assert.Contains("__ganttRenderItem", json);
        }

        [Fact]
        public void Sankey_ProducesSankeySeries()
        {
            var v = V("Flow", "SANKEY", new[] { "Source", "Target", "Amount" }, new[]
            {
                new[] { "Raw", "Stage", "10" },
                new[] { "Stage", "Warehouse", "8" }
            }, new Dictionary<string, string>
            {
                ["mapping:source"] = "Source",
                ["mapping:target"] = "Target",
                ["mapping:value"] = "Amount"
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"sankey\"", json);
            Assert.Contains("\"links\"", json);
        }

        [Fact]
        public void Sunburst_LevelMappings_ProducesHierarchySeries()
        {
            var v = V("Tree", "SUNBURST", new[] { "Region", "Category", "Revenue" }, new[]
            {
                new[] { "North", "Software", "100" },
                new[] { "North", "Services", "60" }
            }, new Dictionary<string, string>
            {
                ["mapping:level1"] = "Region",
                ["mapping:level2"] = "Category",
                ["mapping:value"] = "Revenue"
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"sunburst\"", json);
            Assert.Contains("Software", json);
        }

        [Fact]
        public void Network_ProducesGraphSeriesAndHonorsRoamOff()
        {
            var v = V("Lineage", "NETWORK", new[] { "From", "To", "Weight", "Group" }, new[]
            {
                new[] { "Extract", "Stage", "2", "Pipeline" },
                new[] { "Stage", "Warehouse", "3", "Storage" }
            }, new Dictionary<string, string>
            {
                ["mapping:from"] = "From",
                ["mapping:to"] = "To",
                ["mapping:value"] = "Weight",
                ["mapping:node_group"] = "Group",
                ["ROAM"] = "OFF"
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"graph\"", json);
            Assert.Contains("\"roam\":false", json);
            Assert.Contains("\"categories\"", json);
        }

        [Fact]
        public void Trellis_ProducesMultipleGridsAndSeries()
        {
            var v = V("Facets", "TRELLIS", new[] { "Month", "Revenue", "Region" }, new[]
            {
                new[] { "Jan", "10", "North" },
                new[] { "Feb", "15", "North" },
                new[] { "Jan", "8", "South" }
            }, new Dictionary<string, string>
            {
                ["mapping:x"] = "Month",
                ["mapping:y"] = "Revenue",
                ["mapping:facet"] = "Region",
                ["CHART_TYPE"] = "LINE",
                ["COLUMNS"] = "2"
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"grid\"", json);
            Assert.Contains("\"line\"", json);
            Assert.Contains("North", json);
            Assert.Contains("South", json);
        }

        [Fact]
        public void Matrix_ProducesHierarchicalPivotMetadataAndGrandTotals()
        {
            var v = V("Pivot", "MATRIX", new[] { "Region", "Segment", "Year", "Quarter", "Revenue" }, new[]
            {
                new[] { "North", "Enterprise", "2026", "Q1", "10" },
                new[] { "North", "Enterprise", "2026", "Q2", "15" },
                new[] { "South", "SMB", "2026", "Q1", "7" }
            }, new Dictionary<string, string>
            {
                ["mapping:row1"] = "Region",
                ["mapping:row2"] = "Segment",
                ["mapping:col1"] = "Year",
                ["mapping:col2"] = "Quarter",
                ["mapping:value"] = "Revenue",
                ["GRAND_TOTAL"] = "ON"
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("__matrix", json);
            Assert.Contains("\"rowHeaders\":[\"Region\",\"Segment\"]", json);
            Assert.Contains("\"colHeaders\":[\"Year\",\"Quarter\"]", json);
            Assert.Contains("\"colParts\":[[\"2026\",\"Q1\"],[\"2026\",\"Q2\"]]", json);
            Assert.Contains("\"colValues\":[\"2026 / Q1\",\"2026 / Q2\"]", json);
            Assert.Contains("\"aggregate\":\"SUM\"", json);
            Assert.Contains("\"grandTotals\"", json);
        }

        [Fact]
        public void Matrix_MultipleValues_EmitsValueHeadersAndInterleavedCells()
        {
            var v = V("Pivot", "MATRIX",
                new[] { "Region", "Quarter", "Revenue", "Units" },
                new[]
                {
                    new[] { "North", "Q1", "100", "10" },
                    new[] { "North", "Q2", "200", "20" },
                    new[] { "South", "Q1",  "80",  "8" }
                },
                new Dictionary<string, string>
                {
                    ["mapping:row"] = "Region",
                    ["mapping:col"] = "Quarter",
                    ["mapping:value"] = "Revenue",
                    ["mapping:value2"] = "Units"
                });
            var json = R().Render(v);
            Assert.Contains("\"valueHeaders\":[\"Revenue\",\"Units\"]", json);
            // North row should have 4 value cells: Q1 Revenue, Q1 Units, Q2 Revenue, Q2 Units
            Assert.Contains("\"rows\"", json);
        }

        [Fact]
        public void Matrix_AxisSortDesc_OrdersColKeysByValueDescending()
        {
            var v = V("Pivot", "MATRIX",
                new[] { "Cat", "Region", "Revenue" },
                new[]
                {
                    new[] { "A", "West",  "500" },
                    new[] { "A", "East",  "100" },
                    new[] { "B", "West",  "300" },
                    new[] { "B", "East",  "200" }
                },
                new Dictionary<string, string>
                {
                    ["mapping:row"] = "Cat",
                    ["mapping:col"] = "Region",
                    ["mapping:value"] = "Revenue",
                    ["AXIS_SORT"] = "DESC"
                });
            var json = R().Render(v);
            // West total = 800, East total = 300 — West should appear first in colKeys
            var westIdx = json.IndexOf("\"West\"", StringComparison.Ordinal);
            var eastIdx = json.IndexOf("\"East\"", StringComparison.Ordinal);
            Assert.True(westIdx < eastIdx, "DESC sort should put West (higher sum) before East");
        }

        [Fact]
        public void Matrix_SubtotalsEnabled_FlagSentInJson()
        {
            var v = V("Pivot", "MATRIX",
                new[] { "Cat", "Region", "Revenue" },
                new[]
                {
                    new[] { "A", "East", "100" },
                    new[] { "B", "West", "200" }
                },
                new Dictionary<string, string>
                {
                    ["mapping:row"] = "Cat",
                    ["mapping:col"] = "Region",
                    ["mapping:value"] = "Revenue",
                    ["SUBTOTALS"] = "ON"
                });
            var json = R().Render(v);
            Assert.Contains("\"subtotalsEnabled\":true", json);
        }

        // ── StatisticalRenderer ───────────────────────────────────────────────

        [Fact]
        public void BoxPlot_AutoStats_ComputedFromRawValues()
        {
            var v = V("B", "BOXPLOT", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "5"  }, new[] { "A", "10" }, new[] { "A", "15" },
                new[] { "A", "20" }, new[] { "A", "25" },
                new[] { "B", "3"  }, new[] { "B", "9"  }, new[] { "B", "12" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"boxplot\"", json);
        }

        [Fact]
        public void BoxPlot_ExplicitStats_UsesProvidedValues()
        {
            var v = V("B", "BOXPLOT",
                new[] { "Cat", "Min", "Q1", "Median", "Q3", "Max" },
                new[] { new[] { "Group A", "5", "10", "15", "20", "25" } },
                new Dictionary<string, string>
                {
                    ["mapping:x"] = "Cat",
                    ["mapping:min"] = "Min",
                    ["mapping:q1"] = "Q1",
                    ["mapping:median"] = "Median",
                    ["mapping:q3"] = "Q3",
                    ["mapping:max"] = "Max"
                });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"boxplot\"", json);
        }

        [Fact]
        public void BoxPlot_EmptyValues_HandledGracefully()
        {
            var v = V("B", "BOXPLOT", new[] { "Cat", "Val" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"boxplot\"", json);
        }

        // ── HierarchicalRenderer ──────────────────────────────────────────────

        [Fact]
        public void Treemap_ProducesJsonWithTreemapType()
        {
            var v = V("T", "TREEMAP", new[] { "Name", "Value" }, new[]
            {
                new[] { "Sales",     "500" },
                new[] { "Marketing", "300" },
                new[] { "Support",   "150" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"treemap\"", json);
        }

        // ── OverlayRenderer (via CartesianRenderer) ───────────────────────────

        private static VisualManifest BarWithOverlay(string overlayType, double? param = null,
            string lineStyle = "dashed", string? color = null)
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "Q1", "100" }, new[] { "Q2", "150" }, new[] { "Q3", "120" }, new[] { "Q4", "180" }
            });
            v.Overlays = new List<OverlayManifest> { Ov(overlayType, param, lineStyle, color) };
            return v;
        }

        [Fact]
        public void Overlay_Goal_AddsMarkLine()
        {
            var v = BarWithOverlay("Goal", 130.0);
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("markLine", json);
        }

        [Fact]
        public void Overlay_Average_AddsMarkLineWithComputedAvg()
        {
            var v = BarWithOverlay("Average");
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("markLine", json);
        }

        [Fact]
        public void Overlay_MovingAvg_AddsExtraSeries()
        {
            var v = V("L", "LINE", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }, new[] { "C", "15" }, new[] { "D", "25" }
            });
            v.Overlays = new List<OverlayManifest> { Ov("MovingAvg", 3) };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Overlay_Linear_AddsRegressionSeries()
        {
            var v = V("L", "LINE", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }, new[] { "C", "30" }
            });
            v.Overlays = new List<OverlayManifest> { Ov("Linear", lineStyle: "solid") };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Overlay_Exponential_AddsRegressionSeries()
        {
            var v = V("L", "LINE", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }, new[] { "C", "30" }
            });
            v.Overlays = new List<OverlayManifest> { Ov("Exponential", lineStyle: "dotted") };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Overlay_Logarithmic_AddsRegressionSeries()
        {
            var v = V("L", "LINE", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }, new[] { "C", "30" }
            });
            v.Overlays = new List<OverlayManifest> { Ov("Logarithmic") };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Overlay_Power_AddsRegressionSeries()
        {
            var v = V("L", "LINE", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }, new[] { "C", "30" }
            });
            v.Overlays = new List<OverlayManifest> { Ov("Power") };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Overlay_Polynomial_AddsQuadraticSeries()
        {
            var v = V("L", "LINE", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "25" }, new[] { "C", "15" }, new[] { "D", "30" }
            });
            v.Overlays = new List<OverlayManifest> { Ov("Polynomial", 2) };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Overlay_Polynomial_FallsBackToLinearForHigherOrder()
        {
            var v = V("L", "LINE", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }, new[] { "C", "30" }
            });
            v.Overlays = new List<OverlayManifest> { Ov("Polynomial", 3) };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Overlay_MultipleOverlays_AllProcessed()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "Q1", "100" }, new[] { "Q2", "150" }
            });
            v.Overlays = new List<OverlayManifest>
            {
                Ov("Goal",    100.0, color: "#FF0000", label: "Target"),
                Ov("Average", lineStyle: "dotted")
            };
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("markLine", json);
        }

        [Fact]
        public void Overlay_HorizontalBar_GoalOnXAxis()
        {
            var v = V("H", "HBAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "100" }, new[] { "B", "150" }
            });
            v.Overlays = new List<OverlayManifest> { Ov("Goal", 120.0) };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void NonChartType_ReturnsNull()
        {
            var v = V("T", "TABLE", new[] { "A", "B" }, new[] { new[] { "x", "y" } });
            var json = R().Render(v);
            Assert.Null(json);
        }

        // ── GeographicRenderer ────────────────────────────────────────────────

        [Fact]
        public void Map_Choropleth_Default_ProducesMapSeriesJson()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" }, new[]
            {
                new[] { "California", "100" }, new[] { "Texas", "80" }, new[] { "Florida", "60" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"map\"", json);
            Assert.Contains("__mapKey", json);
        }

        [Fact]
        public void Map_Choropleth_WithMapName_UsesBuiltinKey()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" }, new[]
            {
                new[] { "California", "100" }
            }, new Dictionary<string, string> { ["MAP_NAME"] = "US_STATES" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("us-states", json);
        }

        [Fact]
        public void Map_Choropleth_WorldMap_UsesWorldKey()
        {
            var v = V("M", "MAP", new[] { "Country", "GDP" }, new[]
            {
                new[] { "USA", "21000" }, new[] { "China", "14000" }
            }, new Dictionary<string, string> { ["MAP_NAME"] = "WORLD" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("world", json);
        }

        [Fact]
        public void Map_Choropleth_CustomColors_AppliesColorRange()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" }, new[]
            {
                new[] { "North", "50" }, new[] { "South", "80" }
            }, new Dictionary<string, string> { ["COLOR_LOW"] = "#ffffff", ["COLOR_HIGH"] = "#ff0000" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("#ffffff", json);
            Assert.Contains("#ff0000", json);
        }

        [Fact]
        public void Map_Choropleth_ShowLabels_IncludesLabelShow()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" }, new[]
            {
                new[] { "East", "30" }
            }, new Dictionary<string, string> { ["SHOW_LABELS"] = "ON" });
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Map_Choropleth_MatchBy_IncludesMatchByProperty()
        {
            var v = V("M", "MAP", new[] { "FIPS", "Value" }, new[]
            {
                new[] { "06001", "42" }
            }, new Dictionary<string, string> { ["MATCH_BY"] = "FIPS", ["MAP_NAME"] = "US_COUNTIES" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("FIPS", json);
        }

        [Fact]
        public void Map_Choropleth_MapFile_AppendsMapFileProperty()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" }, new[]
            {
                new[] { "Zone A", "10" }
            }, new Dictionary<string, string> { ["MAP_FILE"] = "/data/custom.geojson" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("__mapFile", json);
        }

        [Fact]
        public void Map_Points_ProducesGeoSeriesJson()
        {
            var v = V("M", "MAP", new[] { "Lon", "Lat", "Size", "Label" }, new[]
            {
                new[] { "-118.2", "34.05", "10", "LA" },
                new[] { "-87.6",  "41.85", "20", "Chicago" }
            }, new Dictionary<string, string> { ["MODE"] = "POINTS" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("__pointsSymbolSize", json);
            Assert.Contains("geo", json);
        }

        [Fact]
        public void Map_Points_NoValueColumn_UsesUniformSize()
        {
            var v = V("M", "MAP", new[] { "Lon", "Lat" }, new[]
            {
                new[] { "-73.9", "40.7" }, new[] { "-122.4", "37.8" }
            }, new Dictionary<string, string> { ["MODE"] = "POINTS" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("__mapKey", json);
        }

        [Fact]
        public void Map_Points_ZeroMaxSize_DoesNotDivideByZero()
        {
            var v = V("M", "MAP", new[] { "Lon", "Lat", "Size" }, new[]
            {
                new[] { "0", "0", "0" }
            }, new Dictionary<string, string> { ["MODE"] = "POINTS" });
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Map_CustomMapName_NormalisesUnderscores()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" }, new[]
            {
                new[] { "A", "1" }
            }, new Dictionary<string, string> { ["MAP_NAME"] = "my_custom_map" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("my-custom-map", json);
        }

        [Fact]
        public void Map_NoRows_HandledGracefully()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" });
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        // ── CartesianRenderer — COMBO with series column ──────────────────────

        [Fact]
        public void Combo_WithSeriesColumn_ProducesMultipleSeries()
        {
            var v = V("C", "COMBO", new[] { "Quarter", "Product", "Revenue" }, new[]
            {
                new[] { "Q1", "Widgets", "100" }, new[] { "Q2", "Widgets", "120" },
                new[] { "Q1", "Gadgets", "80"  }, new[] { "Q2", "Gadgets", "90"  }
            });
            v.SeriesDefs = new List<SeriesDefManifest>
            {
                new SeriesDefManifest { Column = "Widgets", SeriesType = "bar" },
                new SeriesDefManifest { Column = "Gadgets", SeriesType = "line" }
            };
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Combo_DualAxis_ProducesYAxisArray()
        {
            var v = V("C", "COMBO", new[] { "Quarter", "Revenue", "Margin" }, new[]
            {
                new[] { "Q1", "100", "20" }, new[] { "Q2", "150", "25" }
            });
            v.SeriesDefs = new List<SeriesDefManifest>
            {
                new SeriesDefManifest { Column = "Revenue", SeriesType = "bar" },
                new SeriesDefManifest { Column = "Margin",  SeriesType = "line" }
            };
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("yAxisIndex", json);
        }

        // ── RendererBase — uncovered paths ────────────────────────────────────

        [Fact]
        public void Bar_WithLegendLeft_IncludesLeftLegend()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }
            }, new Dictionary<string, string> { ["LEGEND_POSITION"] = "left" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"left\"", json);
        }

        [Fact]
        public void Bar_WithLegendRight_IncludesRightLegend()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }
            }, new Dictionary<string, string> { ["LEGEND_POSITION"] = "right" });
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Bar_WithLegendTop_IncludesTopLegend()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }
            }, new Dictionary<string, string> { ["LEGEND_POSITION"] = "top" });
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Bar_WithAxisMinMax_AppliesAxisBounds()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "50" }
            }, new Dictionary<string, string> { ["axis:y:min"] = "0", ["axis:y:max"] = "200" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("200", json);
        }

        [Fact]
        public void Bar_WithAxisLabel_AppliesLabelFormat()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "50" }
            }, new Dictionary<string, string> { ["axis:x:label"] = "Quarter" });
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        [Fact]
        public void Bar_WithPerBarColors_AppliesItemStyle()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }
            }, new Dictionary<string, string> { ["COLOR:A"] = "#ff0000", ["COLOR:B"] = "#0000ff" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("#ff0000", json);
        }

        [Fact]
        public void Bar_Stacked_IncludesStackProperty()
        {
            var v = V("B", "BAR", new[] { "Cat", "Series", "Val" }, new[]
            {
                new[] { "Q1", "Alpha", "10" }, new[] { "Q1", "Beta", "20" },
                new[] { "Q2", "Alpha", "15" }, new[] { "Q2", "Beta", "25" }
            }, new Dictionary<string, string> { ["STACKED"] = "ON" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("stack", json);
        }

        [Fact]
        public void Line_Smooth_IncludesSmoothProperty()
        {
            var v = V("L", "LINE", new[] { "Cat", "Val" }, new[]
            {
                new[] { "A", "10" }, new[] { "B", "20" }
            }, new Dictionary<string, string> { ["SMOOTH"] = "ON" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("smooth", json);
        }

        [Fact]
        public void Bar_WithSeriesColumn_ProducesMultipleSeries()
        {
            var v = V("B", "BAR", new[] { "Quarter", "Region", "Sales" }, new[]
            {
                new[] { "Q1", "East", "100" }, new[] { "Q2", "East", "120" },
                new[] { "Q1", "West", "80"  }, new[] { "Q2", "West", "95"  }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("\"bar\"", json);
        }

        [Fact]
        public void Bar_XLabelsDatetime_SortedChronologically()
        {
            var v = V("B", "BAR", new[] { "Date", "Val" }, new[]
            {
                new[] { "2024-03-01", "30" }, new[] { "2024-01-01", "10" }, new[] { "2024-02-01", "20" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("2024-01-01", json);
        }

        [Fact]
        public void Bar_XLabelsNumeric_SortedNumerically()
        {
            var v = V("B", "BAR", new[] { "Year", "Val" }, new[]
            {
                new[] { "2023", "30" }, new[] { "2021", "10" }, new[] { "2022", "20" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.Contains("2021", json);
        }

        [Fact]
        public void Bar_ShowNoDataPlaceholder_FillsGaps()
        {
            var v = V("B", "BAR", new[] { "Quarter", "Region", "Sales" }, new[]
            {
                new[] { "Q1", "East", "100" }, new[] { "Q2", "West", "80" }
            }, new Dictionary<string, string> { ["SHOW_NO_DATA_PLACEHOLDER"] = "ON" });
            var json = R().Render(v);
            Assert.NotNull(json);
        }

        // ── AXIS_SORT option ──────────────────────────────────────────────────

        [Fact]
        public void AxisSort_Default_Asc_AlphabeticalOrder()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "Zebra", "10" }, new[] { "Apple", "30" }, new[] { "Mango", "20" }
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("Apple", StringComparison.Ordinal) < json.IndexOf("Zebra", StringComparison.Ordinal));
        }

        [Fact]
        public void AxisSort_Desc_StringsReversedAlphabetical()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "Apple", "10" }, new[] { "Mango", "20" }, new[] { "Zebra", "30" }
            }, new Dictionary<string, string> { ["AXIS_SORT"] = "DESC" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("Zebra", StringComparison.Ordinal) < json.IndexOf("Apple", StringComparison.Ordinal));
        }

        [Fact]
        public void AxisSort_Desc_DatesReversedChronological()
        {
            var v = V("B", "BAR", new[] { "Date", "Val" }, new[]
            {
                new[] { "2024-01-01", "10" }, new[] { "2024-03-01", "30" }, new[] { "2024-02-01", "20" }
            }, new Dictionary<string, string> { ["AXIS_SORT"] = "DESC" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("2024-03-01", StringComparison.Ordinal) < json.IndexOf("2024-01-01", StringComparison.Ordinal));
        }

        [Fact]
        public void AxisSort_Desc_NumericLargestFirst()
        {
            var v = V("B", "BAR", new[] { "Year", "Val" }, new[]
            {
                new[] { "2021", "10" }, new[] { "2023", "30" }, new[] { "2022", "20" }
            }, new Dictionary<string, string> { ["AXIS_SORT"] = "DESC" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("2023", StringComparison.Ordinal) < json.IndexOf("2021", StringComparison.Ordinal));
        }

        [Fact]
        public void AxisSort_Source_PreservesRowOrder()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" }, new[]
            {
                new[] { "December", "10" }, new[] { "January", "30" }, new[] { "June", "20" }
            }, new Dictionary<string, string> { ["AXIS_SORT"] = "SOURCE" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("December", StringComparison.Ordinal) < json.IndexOf("January", StringComparison.Ordinal));
            Assert.True(json.IndexOf("January", StringComparison.Ordinal) < json.IndexOf("June", StringComparison.Ordinal));
        }

        [Fact]
        public void AxisSort_ValueDesc_LargestCategoryFirst()
        {
            var v = V("B", "BAR", new[] { "Region", "Revenue" }, new[]
            {
                new[] { "North", "100" }, new[] { "South", "500" }, new[] { "East", "300" }
            }, new Dictionary<string, string> { ["AXIS_SORT"] = "VALUE_DESC" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("South", StringComparison.Ordinal) < json.IndexOf("North", StringComparison.Ordinal));
            Assert.True(json.IndexOf("East", StringComparison.Ordinal) < json.IndexOf("North", StringComparison.Ordinal));
        }

        [Fact]
        public void AxisSort_Value_SmallestCategoryFirst()
        {
            var v = V("B", "BAR", new[] { "Region", "Revenue" }, new[]
            {
                new[] { "North", "500" }, new[] { "South", "100" }, new[] { "East", "300" }
            }, new Dictionary<string, string> { ["AXIS_SORT"] = "VALUE" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("South", StringComparison.Ordinal) < json.IndexOf("North", StringComparison.Ordinal));
        }

        [Fact]
        public void AxisSort_MultiSeries_Source_PreservesRowOrder()
        {
            var v = V("B", "BAR", new[] { "Month", "Region", "Revenue" }, new[]
            {
                new[] { "December", "East", "10" }, new[] { "January", "East", "30" },
                new[] { "December", "West", "20" }, new[] { "January", "West", "40" }
            }, new Dictionary<string, string> { ["AXIS_SORT"] = "SOURCE" });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("December", StringComparison.Ordinal) < json.IndexOf("January", StringComparison.Ordinal));
        }

        [Fact]
        public void AxisSort_MultiSeries_ValueDesc_SortsByTotalMetric()
        {
            var v = V("B", "BAR", new[] { "Region", "Segment", "Revenue" }, new[]
            {
                new[] { "North", "Enterprise", "100" }, new[] { "South", "Enterprise", "400" },
                new[] { "North", "SMB",        "50"  }, new[] { "South", "SMB",        "150" }
            }, new Dictionary<string, string>
            {
                ["AXIS_SORT"] = "VALUE_DESC",
                ["mapping:x"] = "Region",
                ["mapping:y"] = "Revenue",
                ["mapping:series"] = "Segment"
            });
            var json = R().Render(v);
            Assert.NotNull(json);
            Assert.True(json.IndexOf("South", StringComparison.Ordinal) < json.IndexOf("North", StringComparison.Ordinal));
        }
    }
}
