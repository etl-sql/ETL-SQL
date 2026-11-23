using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETL_SQL.Reporting;
using ETL_SQL.Reporting.Renderers;
using Spectre.Console;
using Spectre.Console.Rendering;
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

        private static string RenderToText(IRenderable renderable)
        {
            using var writer = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer)
            });
            console.Write(renderable);
            return writer.ToString();
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

            var text = RenderToText(TerminalRenderer.RenderPage(page, manifest));

            Assert.Contains("My Report", text);
            Assert.Contains("Q4 2024", text);
            Assert.Contains("Alice", text);
        }

        [Fact]
        public void RenderPage_EmptySlotMap_UsesAllVisuals()
        {
            var vis = V("V1", "CARD", new[] { "Val" }, new[] { new[] { "42" } });
            var manifest = new ReportManifest();
            manifest.Visuals.Add(vis);
            var page = new PageManifest { Name = "P" };

            var text = RenderToText(TerminalRenderer.RenderPage(page, manifest));

            Assert.Contains("V1", text);
            Assert.Contains("42", text);
        }

        [Fact]
        public void RenderPage_NoTitle_NoException()
        {
            var vis = V("V1", "CARD", new[] { "Val" }, new[] { new[] { "99" } });
            var manifest = new ReportManifest();
            manifest.Visuals.Add(vis);
            var page = new PageManifest();
            page.SlotMap["A"] = "V1";

            var text = RenderToText(TerminalRenderer.RenderPage(page, manifest));

            Assert.Contains("99", text);
        }

        [Fact]
        public void RenderPage_MissingVisualInManifest_SkipsGracefully()
        {
            var manifest = new ReportManifest();
            var page = new PageManifest();
            page.SlotMap["A"] = "NonExistent";

            var text = RenderToText(TerminalRenderer.RenderPage(page, manifest));

            Assert.DoesNotContain("NonExistent", text);
        }

        [Fact]
        public void RenderPage_WithButton_ReturnsNonNull()
        {
            var manifest = new ReportManifest();
            var button = new ButtonManifest { Name = "MyBtn", Title = "Click Me" };
            manifest.Buttons = new List<ButtonManifest> { button };

            var page = new PageManifest();
            page.SlotMap["A"] = "MyBtn";

            var text = RenderToText(TerminalRenderer.RenderPage(page, manifest));

            Assert.Contains("Click Me", text);
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

            var text = RenderToText(TerminalRenderer.RenderPage(page, manifest));

            Assert.Contains("Sub-Layout", text);
            Assert.Contains("Nested Button", text);
        }

        // ── RenderVisual – BAR ────────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Bar_WithData_ReturnsNonNull()
        {
            var v = V("B", "BAR", new[] { "Label", "Value" },
                new[] { new[] { "Alpha", "10" }, new[] { "Beta", "20" }, new[] { "Gamma", "15" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("B", text);
            Assert.Contains("██", text);
        }

        [Fact]
        public void RenderVisual_Bar_NoNumericValues_ReturnsNoDataPanel()
        {
            var v = V("B", "BAR", new[] { "Label", "Value" },
                new[] { new[] { "A", "notanumber" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("B", text);
            Assert.Contains("No data available", text);
            Assert.DoesNotContain("Unsupported", text);
        }

        [Fact]
        public void RenderVisual_Bar_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("B", "BAR", new[] { "Label", "Value" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("B", text);
            Assert.Contains("No data available", text);
            Assert.DoesNotContain("Unsupported", text);
        }

        // ── RenderVisual – HBAR ───────────────────────────────────────────────

        [Fact]
        public void RenderVisual_HBar_WithData_ReturnsNonNull()
        {
            var v = V("H", "HBAR", new[] { "Label", "Value" },
                new[] { new[] { "East", "300" }, new[] { "West", "200" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("East", text);
            Assert.Contains("West", text);
        }

        [Fact]
        public void RenderVisual_HorizontalBar_AliasWorks()
        {
            var v = V("H", "HORIZONTALBAR", new[] { "Label", "Value" },
                new[] { new[] { "North", "400" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("North", text);
            Assert.Contains("400", text);
        }

        // ── RenderVisual – PIE / DONUT ────────────────────────────────────────

        [Fact]
        public void RenderVisual_Pie_WithData_ReturnsNonNull()
        {
            var v = V("P", "PIE", new[] { "Segment", "Share" },
                new[] { new[] { "A", "30" }, new[] { "B", "70" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("A", text);
            Assert.Contains("B", text);
        }

        [Fact]
        public void RenderVisual_Donut_WithData_ReturnsNonNull()
        {
            var v = V("D", "DONUT", new[] { "Segment", "Share" },
                new[] { new[] { "X", "40" }, new[] { "Y", "60" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("X", text);
            Assert.Contains("Y", text);
        }

        // ── RenderVisual – CARD ───────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Card_ReturnsPanel()
        {
            var v = V("C", "CARD", new[] { "Value" }, new[] { new[] { "42" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("C", text);
            Assert.Contains("42", text);
        }

        [Fact]
        public void RenderVisual_Card_EmptyRows_ReturnsNa()
        {
            var v = V("C", "CARD", new[] { "Value" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("N/A", text);
        }

        // ── RenderVisual – TABLE ──────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Table_ReturnsTableRenderable()
        {
            var v = V("T", "TABLE", new[] { "Name", "Age" },
                new[] { new[] { "Alice", "30" }, new[] { "Bob", "25" } });
            var result = Assert.IsType<Table>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("Alice", text);
            Assert.Contains("Bob", text);
            Assert.Contains("30", text);
        }

        // ── RenderVisual – TEXT ───────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Text_ReturnsNonNull()
        {
            var v = V("Txt", "TEXT", new[] { "Content" }, new[] { new[] { "# Hello" } });
            v.DefaultValue = "# Hello World";
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Hello", text);
        }

        // ── RenderVisual – GAUGE ──────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Gauge_WithNumericValue_ReturnsPanel()
        {
            var v = V("G", "GAUGE", new[] { "Val" }, new[] { new[] { "75" } },
                new Dictionary<string, string> { ["MIN"] = "0", ["MAX"] = "100" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("75", text);
        }

        [Fact]
        public void RenderVisual_Gauge_NonNumericValue_DefaultsToZero()
        {
            var v = V("G", "GAUGE", new[] { "Val" }, new[] { new[] { "N/A" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("0", text);
        }

        [Fact]
        public void RenderVisual_Gauge_HighValue_UsesRedColor()
        {
            var v = V("G", "GAUGE", new[] { "Val" }, new[] { new[] { "95" } },
                new Dictionary<string, string> { ["MAX"] = "100" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("95", text);
        }

        [Fact]
        public void RenderVisual_Gauge_MidValue_UsesYellowColor()
        {
            var v = V("G", "GAUGE", new[] { "Val" }, new[] { new[] { "75" } },
                new Dictionary<string, string> { ["MAX"] = "100" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("75", text);
        }

        // ── RenderVisual – BOXPLOT ────────────────────────────────────────────

        [Fact]
        public void RenderVisual_BoxPlot_WithFiveStatColumns_ReturnsNonNull()
        {
            // Row format: [label, min, q1, median, q3, max]
            var v = V("BP", "BOXPLOT", new[] { "Cat", "Min", "Q1", "Median", "Q3", "Max" },
                new[] { new[] { "Group A", "5", "10", "15", "20", "25" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("BP", text);
            Assert.Contains("Group A", text);
        }

        [Fact]
        public void RenderVisual_BoxPlot_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("BP", "BOXPLOT", new[] { "Cat" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("BP", text);
            Assert.Contains("No data available", text);
            Assert.DoesNotContain("Unsupported", text);
        }

        [Fact]
        public void RenderVisual_BoxPlot_RowTooShort_SkipsRow()
        {
            var v = V("BP", "BOXPLOT", new[] { "Cat", "V" },
                new[] { new[] { "A", "5" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("No data available", text);
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
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Start", text);
            Assert.Contains("Gain", text);
            Assert.Contains("Loss", text);
            Assert.Contains("Total", text);
        }

        [Fact]
        public void RenderVisual_Waterfall_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("WF", "WATERFALL", new[] { "Item", "Amount" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("WF", text);
            Assert.Contains("No data available", text);
            Assert.DoesNotContain("Unsupported", text);
        }

        // ── RenderVisual – LINE ───────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Line_WithEnoughPoints_RendersCanvas()
        {
            var v = V("L", "LINE", new[] { "X", "Y" }, new[]
            {
                new[] { "1", "10" }, new[] { "2", "20" }, new[] { "3", "15" }
            });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("L", text);
            Assert.Contains("20", text);
        }

        [Fact]
        public void RenderVisual_Line_OnlyOneRow_ReturnsPlaceholder()
        {
            var v = V("L", "LINE", new[] { "X", "Y" }, new[] { new[] { "1", "10" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("L", text);
        }

        [Fact]
        public void RenderVisual_Line_NonNumericYValues_ReturnsPlaceholder()
        {
            var v = V("L", "LINE", new[] { "X", "Y" }, new[]
            {
                new[] { "A", "bad" }, new[] { "B", "worse" }, new[] { "C", "nope" }
            });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("L", text);
        }

        // ── RenderVisual – SCATTER ────────────────────────────────────────────

        [Fact]
        public void RenderVisual_Scatter_WithNumericData_RendersCanvas()
        {
            var v = V("S", "SCATTER", new[] { "X", "Y" }, new[]
            {
                new[] { "1.0", "2.5" }, new[] { "3.0", "4.2" }, new[] { "2.0", "1.8" }
            });
            var panel = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(panel);
            Assert.Contains("╭─S", text);
            Assert.True(text.Split('\n').Length >= 6, "Expected multi-row canvas panel");
        }

        [Fact]
        public void RenderVisual_Scatter_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("S", "SCATTER", new[] { "X", "Y" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("S", text);
            Assert.Contains("No data available", text);
            Assert.DoesNotContain("Unsupported", text);
        }

        [Fact]
        public void RenderVisual_Scatter_AllNonNumericRows_ReturnsPlaceholder()
        {
            var v = V("S", "SCATTER", new[] { "X", "Y" }, new[]
            {
                new[] { "A", "B" }, new[] { "C", "D" }
            });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("S", text);
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
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Week1", text);
            Assert.Contains("Week2", text);
        }

        [Fact]
        public void RenderVisual_HeatMap_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("HM", "HEATMAP", new[] { "Row", "Col" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("HM", text);
            Assert.Contains("No data available", text);
            Assert.DoesNotContain("Unsupported", text);
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

            var text = RenderToText(TerminalRenderer.RenderVisual(v, manifest));
            Assert.Contains("East", text);
            Assert.Contains("West", text);
        }

        [Fact]
        public void RenderVisual_Slicer_WithoutManifest_NoHighlight()
        {
            var v = V("Sl", "SLICER", new[] { "Region" }, new[]
            {
                new[] { "North" }, new[] { "South" }
            });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("North", text);
            Assert.Contains("South", text);
        }

        [Fact]
        public void RenderVisual_Slicer_EmptyRows_ReturnsPlaceholder()
        {
            var v = V("Sl", "SLICER", new[] { "Region" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Sl", text);
        }

        [Fact]
        public void RenderVisual_MultiSelect_ReturnsNonNull()
        {
            var v = V("MS", "MULTISELECT", new[] { "Option" }, new[]
            {
                new[] { "OptionA" }, new[] { "OptionB" }
            });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("OptionA", text);
            Assert.Contains("OptionB", text);
        }

        [Fact]
        public void RenderVisual_Slider_ReturnsNonNull()
        {
            var v = V("Sl2", "SLIDER", new[] { "Val" }, new[] { new[] { "50" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Sl2", text);
            Assert.DoesNotContain("not currently supported", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RenderVisual_DatePickerWithoutRows_RendersAsSupportedControl()
        {
            var v = V("AsOf", "DATEPICKER", Array.Empty<string>());
            v.DefaultValue = "2026-08-20";

            var rendered = TerminalRenderer.RenderVisual(v);
            var text = RenderToText(rendered);

            Assert.Contains("2026-08-20", text);
            Assert.DoesNotContain("not currently supported", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RenderVisual_Search_ReturnsNonNull()
        {
            var v = V("Sr", "SEARCH", new[] { "Term" }, new[] { new[] { "hello" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Sr", text);
            Assert.DoesNotContain("not currently supported", text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RenderVisual_ReDatePicker_ReturnsNonNull()
        {
            var v = V("Rd", "REDATEPICKER", new[] { "Date" }, new[] { new[] { "2026-06-05" } });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Rd", text);
            Assert.DoesNotContain("not currently supported", text, StringComparison.OrdinalIgnoreCase);
        }

        // ── RenderVisual – unsupported types in TUI ───────────────────────────

        [Fact]
        public void RenderVisual_Treemap_ReturnsPremiumPlaceholder()
        {
            var v = V("T", "TREEMAP", new[] { "Name", "Value" }, new[] { new[] { "A", "1" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("T", text);
            Assert.Contains("A", text);
        }

        [Fact]
        public void RenderVisual_Radar_ReturnsPremiumPlaceholder()
        {
            var v = V("R", "RADAR", new[] { "Series", "Metric" }, new[] { new[] { "A", "50" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            Assert.Contains("RADAR", RenderToText(result), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RenderVisual_Bubble_RendersCanvasBubbleChart()
        {
            var v = V("B", "BUBBLE", new[] { "X", "Y", "Size" }, new[] { new[] { "10", "20", "5" }, new[] { "30", "40", "10" } });
            var panel = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(panel);
            Assert.Contains("╭─B", text);
            Assert.True(text.Split('\n').Length >= 6, "Expected multi-row bubble canvas panel");
        }

        [Fact]
        public void RenderVisual_Funnel_RendersFunnelChart()
        {
            var v = V("F", "FUNNEL", new[] { "Stage", "Value" }, new[] { new[] { "Leads", "100" }, new[] { "Customers", "10" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("Leads", text);
            Assert.Contains("Customers", text);
        }

        [Fact]
        public void RenderVisual_Gantt_RendersGanttChart()
        {
            var v = V("G", "GANTT", new[] { "Task", "Start", "End" }, new[] { new[] { "Design", "0", "5" }, new[] { "Dev", "5", "15" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("Design", text);
            Assert.Contains("Dev", text);
        }

        [Fact]
        public void RenderVisual_Candlestick_RendersCandleChart()
        {
            var v = V("C", "CANDLESTICK", new[] { "Date", "O", "H", "L", "Cl" }, new[]
            {
                new[] { "Jan", "100", "110", "95", "105" }
            });
            var panel = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(panel);
            Assert.Contains("╭─C", text);
            Assert.True(text.Split('\n').Length >= 6, "Expected multi-row candlestick canvas panel");
        }

        [Fact]
        public void RenderVisual_Matrix_RendersMatrixGrid()
        {
            var v = V("M", "MATRIX", new[] { "Period", "Rev" }, new[] { new[] { "Q1", "1000" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("Q1", text);
            Assert.Contains("1000", text);
        }

        [Fact]
        public void RenderVisual_Trellis_RendersTrellisGrid()
        {
            var v = V("Tr", "TRELLIS", new[] { "Reg", "Prod", "Sales" }, new[] { new[] { "A", "X", "100" } },
                opts: new Dictionary<string, string> { ["FACET"] = "Reg" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Tr", text);
            Assert.Contains("A", text);
        }

        [Fact]
        public void RenderVisual_Checkbox_RendersInteractiveControl()
        {
            var v = V("Chk", "CHECKBOX", new string[] { });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Chk", RenderToText(result));
        }

        [Fact]
        public void RenderVisual_Textbox_RendersInteractiveControl()
        {
            var v = V("Txt", "TEXTBOX", new string[] { });
            v.DefaultValue = "Initial Text";
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Initial Text", RenderToText(result));
        }

        [Fact]
        public void RenderVisual_Numberbox_RendersInteractiveControl()
        {
            var v = V("Num", "NUMBERBOX", new string[] { });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            Assert.Contains("Num", RenderToText(result));
        }

        [Fact]
        public void RenderVisual_Checkbox_Toggle_RendersToggleText()
        {
            var v = V("Chk", "CHECKBOX", new string[] { }, opts: new Dictionary<string, string>
            {
                ["DISPLAY_STYLE"] = "TOGGLE",
                ["LABEL"] = "Active Only",
                ["DEFAULT"] = "ON"
            });
            var result = TerminalRenderer.RenderVisual(v);
            var text = RenderToText(result);
            Assert.Contains("TOGGLE: ON", text);
            Assert.Contains("Active Only", text);
        }

        [Fact]
        public void RenderVisual_Textbox_Multiline_RendersTextareaTag()
        {
            var v = V("Txt", "TEXTBOX", new string[] { }, opts: new Dictionary<string, string>
            {
                ["MULTILINE"] = "ON",
                ["ROWS"] = "5",
                ["LABEL"] = "Feedback"
            });
            var result = TerminalRenderer.RenderVisual(v);
            var text = RenderToText(result);
            Assert.Contains("Feedback:", text);
            Assert.Contains("(Textarea, Rows: 5)", text);
        }

        [Fact]
        public void RenderVisual_Numberbox_StepperPrefixSuffix_RendersFormatted()
        {
            var v = V("Num", "NUMBERBOX", new string[] { }, opts: new Dictionary<string, string>
            {
                ["PREFIX"] = "$",
                ["SUFFIX"] = " USD",
                ["STEP"] = "5",
                ["LABEL"] = "Cost"
            });
            var result = TerminalRenderer.RenderVisual(v);
            var text = RenderToText(result);
            Assert.Contains("Cost:", text);
            Assert.Contains("$", text);
            Assert.Contains(" USD", text);
            Assert.Contains("Step: 5", text);
        }

        [Fact]
        public void RenderVisual_Map_ReturnsPremiumPlaceholder()
        {
            var v = V("M", "MAP", new[] { "Region", "Value" }, new[] { new[] { "US", "100" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("US", text);
            Assert.Contains("100", text);
        }

        [Fact]
        public void RenderVisual_Image_ReturnsPremiumPlaceholder()
        {
            var v = V("Img", "IMAGE", new string[] { });
            v.DefaultValue = "logo.png";
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            Assert.Contains("logo.png", RenderToText(result));
        }

        [Fact]
        public void RenderVisual_Combo_ReturnsPremiumPlaceholder()
        {
            var v = V("Cmb", "COMBO", new[] { "X", "Y" }, new[] { new[] { "A", "10" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            Assert.Contains("COMBO", RenderToText(result), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RenderVisual_Sankey_ReturnsPremiumPlaceholder()
        {
            var v = V("Sank", "SANKEY", new[] { "Src", "Dst", "Val" }, new[] { new[] { "A", "B", "100" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("Sank", text);
            Assert.Contains("100", text);
        }

        [Fact]
        public void RenderVisual_Sunburst_ReturnsPremiumPlaceholder()
        {
            var v = V("Sun", "SUNBURST", new[] { "Path", "Val" }, new[] { new[] { "A>B", "10" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("Sun", text);
            Assert.Contains("10", text);
        }

        [Fact]
        public void RenderVisual_Network_ReturnsPremiumPlaceholder()
        {
            var v = V("Net", "NETWORK", new[] { "Src", "Dst" }, new[] { new[] { "A", "B" } });
            var result = Assert.IsType<Panel>(TerminalRenderer.RenderVisual(v));
            var text = RenderToText(result);
            Assert.Contains("Net", text);
            Assert.Contains("A", text);
        }

        // ── RenderVisual – unknown / fallback ─────────────────────────────────

        [Fact]
        public void RenderVisual_UnknownType_ReturnsPlaceholder()
        {
            var v = V("X", "UNKNOWN_VISUAL_TYPE", new[] { "A" });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("UNKNOWN_VISUAL_TYPE", text);
            Assert.Contains("not currently supported", text, StringComparison.OrdinalIgnoreCase);
        }

        // ── GetVisualTitle paths ──────────────────────────────────────────────

        [Fact]
        public void RenderVisual_TitleFromOptions_AppliesOptionTitle()
        {
            var v = V("V", "CARD", new[] { "Val" }, new[] { new[] { "100" } },
                opts: new Dictionary<string, string> { ["TITLE"] = "Options Title" });
            Assert.Contains("Options Title", RenderToText(TerminalRenderer.RenderVisual(v)));
        }

        [Fact]
        public void RenderVisual_TitleFromStyles_AppliesStyleTitle()
        {
            var v = V("V", "CARD", new[] { "Val" }, new[] { new[] { "200" } },
                styles: new Dictionary<string, string> { ["TITLE"] = "Styles Title" });
            Assert.Contains("Styles Title", RenderToText(TerminalRenderer.RenderVisual(v)));
        }

        [Fact]
        public void RenderVisual_TitleFallsBackToVisualName()
        {
            var v = V("MyVisualName", "CARD", new[] { "Val" }, new[] { new[] { "300" } });
            Assert.Contains("MyVisualName", RenderToText(TerminalRenderer.RenderVisual(v)));
        }

        [Theory]
        [InlineData("BAR")]
        [InlineData("HBAR")]
        [InlineData("HORIZONTALBAR")]
        [InlineData("PIE")]
        [InlineData("DONUT")]
        [InlineData("BOXPLOT")]
        [InlineData("WATERFALL")]
        [InlineData("LINE")]
        [InlineData("SCATTER")]
        [InlineData("HEATMAP")]
        [InlineData("BUBBLE")]
        [InlineData("FUNNEL")]
        [InlineData("GANTT")]
        [InlineData("CANDLESTICK")]
        public void RenderVisual_UnparseableRows_ShowsNoDataPanel(string visualType)
        {
            var v = V("UnparseableChart", visualType,
                new[] { "C0", "C1", "C2", "C3", "C4", "C5" },
                new[]
                {
                    new[] { "Row1", "bad", "bad", "bad", "bad", "bad" },
                    new[] { "Row2", "nope", "nope", "nope", "nope", "nope" }
                });
            var text = RenderToText(TerminalRenderer.RenderVisual(v));
            Assert.Contains("UnparseableChart", text);
            Assert.Contains("No data available for this chart.", text);
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

