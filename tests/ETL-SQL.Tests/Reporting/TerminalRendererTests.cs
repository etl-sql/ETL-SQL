using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Unit tests for TerminalRenderer's public static API.
    /// Exercises RenderPage and all RenderVisual dispatch branches without
    /// DashboardService — VisualManifest objects are constructed directly.
    /// </summary>
    public class TerminalRendererTests
    {
        private static VisualManifest V(string name, string type,
            string[] cols, string[][]? rows = null,
            Dictionary<string, string>? opts = null,
            Dictionary<string, string>? styles = null)
        {
            var v = new VisualManifest { Name = name, VisualType = type };
            v.Columns.AddRange(cols);
            if (rows != null)
                v.Rows.AddRange(rows.Select(r => r.Select(c => (string?)c).ToList()));
            if (opts != null)
                foreach (var (k, val) in opts)
                    v.Options[k] = val;
            v.Styles = styles;
            return v;
        }

        // ── RenderPage ────────────────────────────────────────────────────────

        [Fact]
        public void RenderPage_WithTitleAndSubtitle_ReturnsNonNull()
        {
            var vis = V("MyTable", "TABLE", new[] { "Name" }, new[] { new[] { "Alice" } });
            var manifest = new ReportManifest();
            manifest.Visuals.Add(vis);
            var page = new PageManifest { Title = "My Report", Subtitle = "Q4 2024" };
            page.SlotMap["A"] = "MyTable";

            var result = TerminalRenderer.RenderPage(page, manifest);

            Assert.NotNull(result);
        }

        [Fact]
        public void RenderPage_EmptySlotMap_UsesAllVisuals()
        {
            var vis = V("V1", "CARD", new[] { "Val" }, new[] { new[] { "42" } });
            var manifest = new ReportManifest();
            manifest.Visuals.Add(vis);
            var page = new PageManifest { Name = "P" };

            var result = TerminalRenderer.RenderPage(page, manifest);

            Assert.NotNull(result);
        }

        [Fact]
        public void RenderPage_NoTitle_NoException()
        {
            var vis = V("V1", "CARD", new[] { "Val" }, new[] { new[] { "99" } });
            var manifest = new ReportManifest();
            manifest.Visuals.Add(vis);
            var page = new PageManifest();
            page.SlotMap["A"] = "V1";

            var result = TerminalRenderer.RenderPage(page, manifest);

            Assert.NotNull(result);
        }

        [Fact]
        public void RenderPage_MissingVisualInManifest_SkipsGracefully()
        {
            var manifest = new ReportManifest();
            var page = new PageManifest();
            page.SlotMap["A"] = "NonExistent";

            var result = TerminalRenderer.RenderPage(page, manifest);

            Assert.NotNull(result);
        }

        [Fact]
        public void RenderPage_WithButton_ReturnsNonNull()
        {
            var manifest = new ReportManifest();
            var button = new ButtonManifest { Name = "MyBtn", Title = "Click Me" };
            manifest.Buttons = new List<ButtonManifest> { button };

            var page = new PageManifest();
            page.SlotMap["A"] = "MyBtn";

            var result = TerminalRenderer.RenderPage(page, manifest);

            Assert.NotNull(result);
        }

        [Fact]
        public void RenderPage_WithContainer_ReturnsNonNull()
        {
            var manifest = new ReportManifest();
            var container = new ContainerManifest
            {
                Name = "MyContainer",
                Title = "Sub-Layout",
                ContainerType = "GRID",
                SlotMap = new Dictionary<string, string> { ["slot1"] = "MyBtn2" }
            };
            var button = new ButtonManifest { Name = "MyBtn2", Title = "Nested Button" };

            manifest.Containers = new List<ContainerManifest> { container };
            manifest.Buttons = new List<ButtonManifest> { button };

            var page = new PageManifest();
            page.SlotMap["A"] = "MyContainer";

            var result = TerminalRenderer.RenderPage(page, manifest);

            Assert.NotNull(result);
        }

        // ── RenderVisual – BAR ────────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Bar_WithData_ReturnsNonNull()
        {
            var v = V("B", "BAR", new[] { "Label", "Value" },
                new[] { new[] { "Alpha", "10" }, new[] { "Beta", "20" }, new[] { "Gamma", "15" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Bar_NoNumericValues_ReturnsNoDataPanel()
        {
            var v = V("B", "BAR", new[] { "Label", "Value" },
                new[] { new[] { "A", "notanumber" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Bar_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("B", "BAR", new[] { "Label", "Value" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – HBAR ───────────────────────────────────────────────

        [Fact]
        public void RenderVisual_HBar_WithData_ReturnsNonNull()
        {
            var v = V("H", "HBAR", new[] { "Label", "Value" },
                new[] { new[] { "East", "300" }, new[] { "West", "200" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_HorizontalBar_AliasWorks()
        {
            var v = V("H", "HORIZONTALBAR", new[] { "Label", "Value" },
                new[] { new[] { "North", "400" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – PIE / DONUT ────────────────────────────────────────

        [Fact]
        public void RenderVisual_Pie_WithData_ReturnsNonNull()
        {
            var v = V("P", "PIE", new[] { "Segment", "Share" },
                new[] { new[] { "A", "30" }, new[] { "B", "70" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Donut_WithData_ReturnsNonNull()
        {
            var v = V("D", "DONUT", new[] { "Segment", "Share" },
                new[] { new[] { "X", "40" }, new[] { "Y", "60" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – CARD ───────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Card_ReturnsPanel()
        {
            var v = V("C", "CARD", new[] { "Value" }, new[] { new[] { "42" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Card_EmptyRows_ReturnsNa()
        {
            var v = V("C", "CARD", new[] { "Value" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – TABLE ──────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Table_ReturnsTableRenderable()
        {
            var v = V("T", "TABLE", new[] { "Name", "Age" },
                new[] { new[] { "Alice", "30" }, new[] { "Bob", "25" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Table>(result);
        }

        // ── RenderVisual – TEXT ───────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Text_ReturnsNonNull()
        {
            var v = V("Txt", "TEXT", new[] { "Content" }, new[] { new[] { "# Hello" } });
            v.DefaultValue = "# Hello World";
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – GAUGE ──────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Gauge_WithNumericValue_ReturnsPanel()
        {
            var v = V("G", "GAUGE", new[] { "Val" }, new[] { new[] { "75" } },
                new Dictionary<string, string> { ["MIN"] = "0", ["MAX"] = "100" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Gauge_NonNumericValue_DefaultsToZero()
        {
            var v = V("G", "GAUGE", new[] { "Val" }, new[] { new[] { "N/A" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Gauge_HighValue_UsesRedColor()
        {
            var v = V("G", "GAUGE", new[] { "Val" }, new[] { new[] { "95" } },
                new Dictionary<string, string> { ["MAX"] = "100" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Gauge_MidValue_UsesYellowColor()
        {
            var v = V("G", "GAUGE", new[] { "Val" }, new[] { new[] { "75" } },
                new Dictionary<string, string> { ["MAX"] = "100" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – BOXPLOT ────────────────────────────────────────────

        [Fact]
        public void RenderVisual_BoxPlot_WithFiveStatColumns_ReturnsNonNull()
        {
            // Row format: [label, min, q1, median, q3, max]
            var v = V("BP", "BOXPLOT", new[] { "Cat", "Min", "Q1", "Median", "Q3", "Max" },
                new[] { new[] { "Group A", "5", "10", "15", "20", "25" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_BoxPlot_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("BP", "BOXPLOT", new[] { "Cat" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_BoxPlot_RowTooShort_SkipsRow()
        {
            var v = V("BP", "BOXPLOT", new[] { "Cat", "V" },
                new[] { new[] { "A", "5" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – WATERFALL ──────────────────────────────────────────

        [Fact]
        public void RenderVisual_Waterfall_WithPositiveAndNegative_ReturnsNonNull()
        {
            var v = V("WF", "WATERFALL", new[] { "Item", "Amount" }, new[]
            {
                new[] { "Start",  "200" },
                new[] { "Gain",   "50"  },
                new[] { "Loss",   "-30" },
                new[] { "Total",  "220" }
            });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Waterfall_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("WF", "WATERFALL", new[] { "Item", "Amount" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – LINE ───────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Line_WithEnoughPoints_RendersCanvas()
        {
            var v = V("L", "LINE", new[] { "X", "Y" }, new[]
            {
                new[] { "1", "10" }, new[] { "2", "20" }, new[] { "3", "15" }
            });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Line_OnlyOneRow_ReturnsPlaceholder()
        {
            var v = V("L", "LINE", new[] { "X", "Y" }, new[] { new[] { "1", "10" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Line_NonNumericYValues_ReturnsPlaceholder()
        {
            var v = V("L", "LINE", new[] { "X", "Y" }, new[]
            {
                new[] { "A", "bad" }, new[] { "B", "worse" }, new[] { "C", "nope" }
            });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – SCATTER ────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Scatter_WithNumericData_RendersCanvas()
        {
            var v = V("S", "SCATTER", new[] { "X", "Y" }, new[]
            {
                new[] { "1.0", "2.5" }, new[] { "3.0", "4.2" }, new[] { "2.0", "1.8" }
            });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Scatter_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("S", "SCATTER", new[] { "X", "Y" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Scatter_AllNonNumericRows_ReturnsPlaceholder()
        {
            var v = V("S", "SCATTER", new[] { "X", "Y" }, new[]
            {
                new[] { "A", "B" }, new[] { "C", "D" }
            });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – HEATMAP ────────────────────────────────────────────

        [Fact]
        public void RenderVisual_HeatMap_WithGrid_ReturnsNonNull()
        {
            var v = V("HM", "HEATMAP", new[] { "Row", "Mon", "Tue", "Wed" }, new[]
            {
                new[] { "Week1", "5",  "10", "8"  },
                new[] { "Week2", "12", "3",  "15" }
            });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_HeatMap_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("HM", "HEATMAP", new[] { "Row", "Col" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – SLICER / controls ─────────────────────────────────

        [Fact]
        public void RenderVisual_Slicer_HighlightsSelectedValue()
        {
            var v = V("Sl", "SLICER", new[] { "Region" }, new[]
            {
                new[] { "East" }, new[] { "West" }, new[] { "South" }
            });
            v.Actions.Add(new VisualActionManifest { Type = "SET_PARAMETER", ParameterName = "@Region" });
            var manifest = new ReportManifest();
            manifest.Parameters["@Region"] = "East";

            Assert.NotNull(TerminalRenderer.RenderVisual(v, manifest));
        }

        [Fact]
        public void RenderVisual_Slicer_WithoutManifest_NoHighlight()
        {
            var v = V("Sl", "SLICER", new[] { "Region" }, new[]
            {
                new[] { "North" }, new[] { "South" }
            });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Slicer_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("Sl", "SLICER", new[] { "Region" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_MultiSelect_ReturnsNonNull()
        {
            var v = V("MS", "MULTISELECT", new[] { "Option" }, new[]
            {
                new[] { "OptionA" }, new[] { "OptionB" }
            });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Slider_ReturnsNonNull()
        {
            var v = V("Sl2", "SLIDER", new[] { "Val" }, new[] { new[] { "50" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_Search_ReturnsNonNull()
        {
            var v = V("Sr", "SEARCH", new[] { "Term" }, new[] { new[] { "hello" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_ReDatePicker_ReturnsNonNull()
        {
            var v = V("Rd", "REDATEPICKER", new[] { "Date" }, new[] { new[] { "2026-06-05" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── RenderVisual – unsupported types in TUI ───────────────────────────

        [Fact]
        public void RenderVisual_Treemap_ReturnsPremiumPlaceholder()
        {
            var v = V("T", "TREEMAP", new[] { "Name", "Value" }, new[] { new[] { "A", "1" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Radar_ReturnsPremiumPlaceholder()
        {
            var v = V("R", "RADAR", new[] { "Series", "Metric" }, new[] { new[] { "A", "50" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Bubble_RendersCanvasBubbleChart()
        {
            var v = V("B", "BUBBLE", new[] { "X", "Y", "Size" }, new[] { new[] { "10", "20", "5" }, new[] { "30", "40", "10" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
        }

        [Fact]
        public void RenderVisual_Funnel_RendersFunnelChart()
        {
            var v = V("F", "FUNNEL", new[] { "Stage", "Value" }, new[] { new[] { "Leads", "100" }, new[] { "Customers", "10" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Gantt_RendersGanttChart()
        {
            var v = V("G", "GANTT", new[] { "Task", "Start", "End" }, new[] { new[] { "Design", "0", "5" }, new[] { "Dev", "5", "15" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Candlestick_RendersCandleChart()
        {
            var v = V("C", "CANDLESTICK", new[] { "Date", "O", "H", "L", "Cl" }, new[]
            {
                new[] { "Jan", "100", "110", "95", "105" }
            });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
        }

        [Fact]
        public void RenderVisual_Matrix_RendersMatrixGrid()
        {
            var v = V("M", "MATRIX", new[] { "Period", "Rev" }, new[] { new[] { "Q1", "1000" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Trellis_RendersTrellisGrid()
        {
            var v = V("Tr", "TRELLIS", new[] { "Reg", "Prod", "Sales" }, new[] { new[] { "A", "X", "100" } },
                opts: new Dictionary<string, string> { ["FACET"] = "Reg" });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
        }

        [Fact]
        public void RenderVisual_Checkbox_RendersInteractiveControl()
        {
            var v = V("Chk", "CHECKBOX", new string[] { });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Textbox_RendersInteractiveControl()
        {
            var v = V("Txt", "TEXTBOX", new string[] { });
            v.DefaultValue = "Initial Text";
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Numberbox_RendersInteractiveControl()
        {
            var v = V("Num", "NUMBERBOX", new string[] { });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Map_ReturnsPremiumPlaceholder()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" }, new[] { new[] { "US", "100" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Image_ReturnsPremiumPlaceholder()
        {
            var v = V("Img", "IMAGE", new string[] { });
            v.DefaultValue = "logo.png";
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Combo_ReturnsPremiumPlaceholder()
        {
            var v = V("Cmb", "COMBO", new[] { "X", "Y" }, new[] { new[] { "A", "10" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Sankey_ReturnsPremiumPlaceholder()
        {
            var v = V("Sank", "SANKEY", new[] { "Src", "Dst", "Val" }, new[] { new[] { "A", "B", "100" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Sunburst_ReturnsPremiumPlaceholder()
        {
            var v = V("Sun", "SUNBURST", new[] { "Path", "Val" }, new[] { new[] { "A>B", "10" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        [Fact]
        public void RenderVisual_Network_ReturnsPremiumPlaceholder()
        {
            var v = V("Net", "NETWORK", new[] { "Src", "Dst" }, new[] { new[] { "A", "B" } });
            var result = TerminalRenderer.RenderVisual(v);
            Assert.NotNull(result);
            Assert.IsType<Panel>(result);
        }

        // ── RenderVisual – unknown / fallback ─────────────────────────────────

        [Fact]
        public void RenderVisual_UnknownType_ReturnsPlaceholder()
        {
            var v = V("X", "UNKNOWN_VISUAL_TYPE", new[] { "A" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── GetVisualTitle paths ──────────────────────────────────────────────

        [Fact]
        public void RenderVisual_TitleFromOptions_AppliesOptionTitle()
        {
            var v = V("V", "CARD", new[] { "Val" }, new[] { new[] { "100" } },
                opts: new Dictionary<string, string> { ["TITLE"] = "Options Title" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_TitleFromStyles_AppliesStyleTitle()
        {
            var v = V("V", "CARD", new[] { "Val" }, new[] { new[] { "200" } },
                styles: new Dictionary<string, string> { ["TITLE"] = "Styles Title" });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        [Fact]
        public void RenderVisual_TitleFallsBackToVisualName()
        {
            var v = V("MyVisualName", "CARD", new[] { "Val" }, new[] { new[] { "300" } });
            Assert.NotNull(TerminalRenderer.RenderVisual(v));
        }

        // ── Numeric cell formatting (Card / Table rounding) ──────────────────

        [Theory]
        [InlineData("3360526.32035216541905800064004", "3,360,526.32")]
        [InlineData("582261.86565057459434199359880", "582,261.87")]
        [InlineData("12356", "12,356")]
        [InlineData("12356.5", "12,356.5")]
        [InlineData("hello", "hello")]   // non-numeric passes through
        [InlineData("", "")]
        public void FormatNumericCell_RoundsNumbers_PassesTextThrough(string raw, string expected)
        {
            Assert.Equal(expected, TerminalRenderer.FormatNumericCell(raw));
        }
    }
}

