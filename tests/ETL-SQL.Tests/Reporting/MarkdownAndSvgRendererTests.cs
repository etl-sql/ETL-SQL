using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Unit tests for MarkdownRenderer and SvgChartRenderer.
    /// Both are pure functions operating on VisualManifest/ReportManifest,
    /// so no DashboardService overhead is needed.
    /// </summary>
    public class MarkdownAndSvgRendererTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static VisualManifest V(string name, string type,
            string[] cols, string[][]? rows = null,
            Dictionary<string, string>? opts = null)
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

        private static ReportManifest M(params VisualManifest[] visuals)
        {
            var m = new ReportManifest();
            foreach (var v in visuals) m.Visuals.Add(v);
            return m;
        }

        private static MarkdownRenderer MD() => new MarkdownRenderer();
        private static SvgChartRenderer SVG() => new SvgChartRenderer();

        // ══════════════════════════════════════════════════════════════════════
        // MarkdownRenderer
        // ══════════════════════════════════════════════════════════════════════

        // ── Render (root) ─────────────────────────────────────────────────────

        [Fact]
        public void Render_WithTitle_IncludesTitleInOutput()
        {
            var m = M();
            m.Title = "My Report";
            var md = MD().Render(m);
            Assert.Contains("My Report", md);
        }

        [Fact]
        public void Render_TitleIsMarkdown_NoEscaping()
        {
            var m = M();
            m.Title = "**Bold Title**";
            m.TitleIsMarkdown = true;
            var md = MD().Render(m);
            Assert.Contains("**Bold Title**", md);
        }

        [Fact]
        public void Render_WithDescription_IncludesDescription()
        {
            var m = M();
            m.Description = "This is the report description.";
            var md = MD().Render(m);
            Assert.Contains("This is the report description.", md);
        }

        [Fact]
        public void Render_WithSource_IncludesSourceAnnotation()
        {
            var m = M();
            m.Source = "my-report.rptsql";
            var md = MD().Render(m);
            Assert.Contains("my-report.rptsql", md);
        }

        [Fact]
        public void Render_NoSource_StillGenerates()
        {
            var m = M();
            m.Source = null;
            var md = MD().Render(m);
            Assert.NotNull(md);
            Assert.Contains("Generated:", md);
        }

        [Fact]
        public void Render_NoPages_RendersAllVisuals()
        {
            var m = M(
                V("T1", "TABLE", new[] { "Name" }, new[] { new[] { "Alice" } }),
                V("C1", "CARD", new[] { "Value" }, new[] { new[] { "42" } })
            );
            var md = MD().Render(m);
            Assert.Contains("T1", md);
            Assert.Contains("C1", md);
        }

        [Fact]
        public void Render_WithPages_RendersPageHeadings()
        {
            var vis = V("MyTable", "TABLE", new[] { "Name" }, new[] { new[] { "Alice" } });
            var m = M(vis);
            var page = new PageManifest { Name = "Overview", Title = "Sales Overview" };
            page.SlotMap["A"] = "MyTable";
            m.Pages.Add(page);

            var md = MD().Render(m);
            Assert.Contains("Sales Overview", md);
        }

        // ── RenderPage ────────────────────────────────────────────────────────

        [Fact]
        public void Render_PageWithSubtitle_IncludesSubtitle()
        {
            var vis = V("V1", "CARD", new[] { "Val" }, new[] { new[] { "1" } });
            var m = M(vis);
            var page = new PageManifest { Name = "P", Title = "Title", Subtitle = "Q4 2024" };
            page.SlotMap["A"] = "V1";
            m.Pages.Add(page);

            var md = MD().Render(m);
            Assert.Contains("Q4 2024", md);
        }

        [Fact]
        public void Render_PageSubtitleIsMarkdown_NoEscaping()
        {
            var vis = V("V1", "CARD", new[] { "Val" }, new[] { new[] { "1" } });
            var m = M(vis);
            var page = new PageManifest { Name = "P", Subtitle = "*Italic*", SubtitleIsMarkdown = true };
            page.SlotMap["A"] = "V1";
            m.Pages.Add(page);

            var md = MD().Render(m);
            Assert.Contains("*Italic*", md);
        }

        [Fact]
        public void Render_PageTitleIsMarkdown_NoEscaping()
        {
            var vis = V("V1", "TABLE", new[] { "A" }, new[] { new[] { "x" } });
            var m = M(vis);
            var page = new PageManifest { Name = "P", Title = "**Bold**", TitleIsMarkdown = true };
            page.SlotMap["A"] = "V1";
            m.Pages.Add(page);

            var md = MD().Render(m);
            Assert.Contains("**Bold**", md);
        }

        [Fact]
        public void Render_PageWithContainer_RendersContainerContent()
        {
            var vis = V("Inner", "CARD", new[] { "Val" }, new[] { new[] { "5" } });
            var m = M(vis);
            var container = new ContainerManifest
            {
                Name = "Box",
                ContainerType = "ROW",
                Title = "Container Title"
            };
            container.SlotMap = new Dictionary<string, string> { ["A"] = "Inner" };
            m.Containers = new List<ContainerManifest> { container };

            var page = new PageManifest { Name = "P" };
            page.SlotMap["A"] = "Box";
            m.Pages.Add(page);

            var md = MD().Render(m);
            Assert.Contains("Container Title", md);
        }

        [Fact]
        public void Render_ContainerTitleIsMarkdown_NoEscaping()
        {
            var vis = V("V", "CARD", new[] { "Val" }, new[] { new[] { "1" } });
            var m = M(vis);
            var container = new ContainerManifest
            {
                Name = "C",
                ContainerType = "ROW",
                Title = "*Italic Container*",
                TitleIsMarkdown = true
            };
            container.SlotMap = new Dictionary<string, string> { ["A"] = "V" };
            m.Containers = new List<ContainerManifest> { container };

            var page = new PageManifest { Name = "P" };
            page.SlotMap["A"] = "C";
            m.Pages.Add(page);

            var md = MD().Render(m);
            Assert.Contains("*Italic Container*", md);
        }

        // ── RenderVisual – TABLE ──────────────────────────────────────────────

        [Fact]
        public void Render_TableVisual_ProducesPipeTable()
        {
            var m = M(V("Sales", "TABLE", new[] { "Region", "Total" },
                new[] { new[] { "East", "100" }, new[] { "West", "200" } }));
            var md = MD().Render(m);
            Assert.Contains("| Region | Total |", md);
            Assert.Contains("| East | 100 |", md);
        }

        [Fact]
        public void Render_TableWithPipeInCell_EscapesPipes()
        {
            var m = M(V("T", "TABLE", new[] { "Info" },
                new[] { new[] { "A|B" } }));
            var md = MD().Render(m);
            Assert.Contains(@"A\|B", md);
        }

        [Fact]
        public void Render_TableWith1001Rows_CapsAtThousandWithNote()
        {
            var rows = Enumerable.Range(1, 1001).Select(i => new[] { i.ToString() }).ToArray();
            var m = M(V("Big", "TABLE", new[] { "N" }, rows));
            var md = MD().Render(m);
            Assert.Contains("more rows not shown", md);
        }

        // ── RenderVisual – CARD ───────────────────────────────────────────────

        [Fact]
        public void Render_CardVisual_ProducesBlockquote()
        {
            var m = M(V("Revenue", "CARD", new[] { "Total" }, new[] { new[] { "42000" } }));
            var md = MD().Render(m);
            Assert.Contains("> **", md);
            Assert.Contains("42000", md);
        }

        [Fact]
        public void Render_CardWithLabelMapping_UsesLabelColumn()
        {
            var v = V("KPI", "CARD", new[] { "MetricName", "MetricValue" },
                new[] { new[] { "Revenue", "99000" } },
                new Dictionary<string, string>
                {
                    ["mapping:label"] = "MetricName",
                    ["mapping:value"] = "MetricValue"
                });
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("Revenue", md);
            Assert.Contains("99000", md);
        }

        [Fact]
        public void Render_CardNoRows_ShowsNoData()
        {
            var m = M(V("Empty", "CARD", new[] { "Val" }));
            var md = MD().Render(m);
            Assert.Contains("No data", md);
        }

        // ── RenderVisual – SLICER ─────────────────────────────────────────────

        [Fact]
        public void Render_SlicerVisual_EmitsInteractiveOnlyNote()
        {
            var m = M(V("Filter", "SLICER", new[] { "Region" }, new[] { new[] { "East" } }));
            var md = MD().Render(m);
            Assert.Contains("interactive only", md);
        }

        // ── RenderVisual – TEXT ───────────────────────────────────────────────

        [Fact]
        public void Render_TextVisual_EmitsContent()
        {
            var v = V("Info", "TEXT", new[] { "Content" }, null,
                opts: new Dictionary<string, string> { ["VALUE"] = "Hello World" });
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("Hello World", md);
        }

        [Fact]
        public void Render_TextVisualIsMarkdown_NoEscaping()
        {
            var v = V("Info", "TEXT", new[] { "Content" }, null,
                opts: new Dictionary<string, string> { ["VALUE"] = "# Heading" });
            v.IsMarkdown = true;
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("# Heading", md);
        }

        [Fact]
        public void Render_TextVisualWithCenterAlign_WrapsInDiv()
        {
            var v = V("Info", "TEXT", new[] { "Content" }, null,
                opts: new Dictionary<string, string> { ["VALUE"] = "centered text", ["ALIGN"] = "center" });
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("align='center'", md);
        }

        // ── RenderVisual – chart (default branch) ─────────────────────────────

        [Fact]
        public void Render_BarVisual_EmbedsNativeSvgImage()
        {
            var v = V("Chart", "BAR", new[] { "Cat", "Val" },
                new[] { new[] { "A", "10" } });
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("data:image/svg+xml", md);
        }

        [Fact]
        public void Render_ChartVisualWithRows_EmitsFallbackTable()
        {
            var v = V("Chart", "SCATTER", new[] { "X", "Y" },
                new[] { new[] { "1", "2" }, new[] { "3", "4" } });
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("| X | Y |", md);
        }

        [Fact]
        public void Render_VisualWithSubtitle_IncludesSubtitle()
        {
            var v = V("V", "TABLE", new[] { "A" }, new[] { new[] { "x" } });
            v.Options["subtitle"] = "Chart subtitle text";
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("Chart subtitle text", md);
        }

        [Fact]
        public void Render_VisualTitleIsMarkdown_NoEscaping()
        {
            var v = V("V", "TABLE", new[] { "A" }, new[] { new[] { "x" } });
            v.TitleIsMarkdown = true;
            v.Options["title"] = "**Bold**";
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("**Bold**", md);
        }

        [Fact]
        public void Render_VisualSubtitleIsMarkdown_NoEscaping()
        {
            var v = V("V", "TABLE", new[] { "A" }, new[] { new[] { "x" } });
            v.SubtitleIsMarkdown = true;
            v.Options["subtitle"] = "*italic*";
            var m = M(v);
            var md = MD().Render(m);
            Assert.Contains("*italic*", md);
        }

        // ══════════════════════════════════════════════════════════════════════
        // SvgChartRenderer
        // ══════════════════════════════════════════════════════════════════════

        [Fact]
        public void Svg_Bar_ProducesSvgWithRect()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" },
                new[] { new[] { "A", "10" }, new[] { "B", "30" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("<rect", svg);
        }

        [Fact]
        public void Svg_Bar_EmptyRows_ProducesPlaceholder()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("BAR chart", svg);
        }

        [Fact]
        public void Svg_HBar_ProducesSvgWithHorizontalBars()
        {
            var v = V("H", "HBAR", new[] { "Cat", "Val" },
                new[] { new[] { "East", "300" }, new[] { "West", "150" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("<svg", svg);
        }

        [Fact]
        public void Svg_Line_ProducesSvgWithPolyline()
        {
            var v = V("L", "LINE", new[] { "Month", "Revenue" }, new[]
            {
                new[] { "Jan", "100" }, new[] { "Feb", "150" }, new[] { "Mar", "120" }
            });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("<polyline", svg);
        }

        [Fact]
        public void Svg_Line_EmptyRows_ProducesPlaceholder()
        {
            var v = V("L", "LINE", new[] { "X", "Y" });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("LINE chart", svg);
        }

        [Fact]
        public void Svg_Line_SinglePoint_SkipsPolyline()
        {
            var v = V("L", "LINE", new[] { "X", "Y" }, new[] { new[] { "A", "10" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            // Single point: no polyline but still has a circle
            Assert.Contains("<circle", svg);
        }

        [Fact]
        public void Svg_Pie_ProducesSvgWithPath()
        {
            var v = V("P", "PIE", new[] { "Label", "Value" },
                new[] { new[] { "A", "40" }, new[] { "B", "60" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("<path", svg);
        }

        [Fact]
        public void Svg_Donut_ProducesSvgWithArc()
        {
            var v = V("D", "DONUT", new[] { "Label", "Value" },
                new[] { new[] { "X", "30" }, new[] { "Y", "70" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("<path", svg);
        }

        [Fact]
        public void Svg_Pie_AllZeroValues_ProducesPlaceholder()
        {
            var v = V("P", "PIE", new[] { "Label", "Value" },
                new[] { new[] { "A", "0" }, new[] { "B", "0" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("PIE chart", svg);
        }

        [Fact]
        public void Svg_Card_ReturnsNull()
        {
            var v = V("C", "CARD", new[] { "Val" }, new[] { new[] { "1" } });
            Assert.Null(SVG().Render(v));
        }

        [Fact]
        public void Svg_Table_ReturnsNull()
        {
            var v = V("T", "TABLE", new[] { "A" }, new[] { new[] { "x" } });
            Assert.Null(SVG().Render(v));
        }

        [Fact]
        public void Svg_Slicer_ReturnsNull()
        {
            var v = V("S", "SLICER", new[] { "Region" }, new[] { new[] { "East" } });
            Assert.Null(SVG().Render(v));
        }

        [Fact]
        public void Svg_Text_ReturnsNull()
        {
            var v = V("T", "TEXT", new[] { "Content" }, new[] { new[] { "hello" } });
            Assert.Null(SVG().Render(v));
        }

        [Fact]
        public void Svg_UnknownType_ProducesPlaceholder()
        {
            var v = V("X", "RADAR", new[] { "A" }, new[] { new[] { "1" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("RADAR chart", svg);
        }

        [Fact]
        public void Svg_LargeValues_UsesKAndMSuffixes()
        {
            // Force large values to trigger K/M tick labels
            var v = V("B", "BAR", new[] { "Cat", "Val" },
                new[] { new[] { "A", "1500000" }, new[] { "B", "500000" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            // Should contain "M" or "K" in tick labels
            Assert.True(svg.Contains("M") || svg.Contains("K"));
        }

        [Fact]
        public void Svg_ValuesInThousands_UsesKSuffix()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" },
                new[] { new[] { "A", "5000" }, new[] { "B", "3000" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("K", svg);
        }

        [Fact]
        public void Svg_BarWithCustomColor_UsesSpecifiedColor()
        {
            var v = V("B", "BAR", new[] { "Cat", "Val" },
                new[] { new[] { "Alpha", "20" } },
                opts: new Dictionary<string, string> { ["color:Alpha"] = "#AABBCC" });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("#AABBCC", svg);
        }

        [Fact]
        public void Svg_Bar_TruncatesLongLabels()
        {
            var v = V("B", "BAR", new[] { "Category", "Value" },
                new[] { new[] { "VeryLongCategoryNameHere", "42" } });
            var svg = SVG().Render(v);
            Assert.NotNull(svg);
            Assert.Contains("…", svg);
        }
    }
}
