using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.ReportBuilder;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Phase 9B: Unit tests for ChartJsRenderer and MarkdownRenderer.
    /// These tests work directly on pre-built manifests — no engine evaluation required.
    /// </summary>
    public class ReportBuilderTests
    {
        // ── ChartJsRenderer ──────────────────────────────────────────────────

        [Theory]
        [InlineData("BAR")]
        [InlineData("LINE")]
        [InlineData("SCATTER")]
        [InlineData("PIE")]
        public void ChartJsRenderer_ProducesValidConfig_ForChartVisualTypes(string visualType)
        {
            var visual = MakeSampleVisual(visualType);
            var renderer = new ChartJsRenderer();

            var config = renderer.Render(visual);

            Assert.NotNull(config);
            Assert.Contains("\"type\"", config);
            Assert.Contains("\"data\"", config);
        }

        [Theory]
        [InlineData("TABLE")]
        [InlineData("CARD")]
        [InlineData("SLICER")]
        public void ChartJsRenderer_ReturnsNull_ForNonChartVisualTypes(string visualType)
        {
            var visual   = MakeSampleVisual(visualType);
            var renderer = new ChartJsRenderer();

            Assert.Null(renderer.Render(visual));
        }

        [Fact]
        public void ChartJsRenderer_Bar_ContainsCorrectChartType()
        {
            var visual   = MakeSampleVisual("BAR");
            var renderer = new ChartJsRenderer();
            var config   = renderer.Render(visual)!;

            Assert.Contains("\"type\":\"bar\"", config);
        }

        [Fact]
        public void ChartJsRenderer_Pie_ContainsBackgroundColors()
        {
            var visual   = MakeSampleVisual("PIE");
            var renderer = new ChartJsRenderer();
            var config   = renderer.Render(visual)!;

            Assert.Contains("backgroundColor", config);
        }

        [Fact]
        public void ChartJsRenderer_Scatter_ContainsXYPoints()
        {
            var visual   = MakeSampleVisual("SCATTER");
            var renderer = new ChartJsRenderer();
            var config   = renderer.Render(visual)!;

            Assert.Contains("\"x\"", config);
            Assert.Contains("\"y\"", config);
        }

        // ── MarkdownRenderer ─────────────────────────────────────────────────

        [Fact]
        public void MarkdownRenderer_Render_ContainsReportTitle()
        {
            var manifest = MakeSampleManifest("my_report.rptsql");
            var renderer = new MarkdownRenderer();

            var md = renderer.Render(manifest);

            Assert.Contains("# Report", md);
            Assert.Contains("my_report", md);
        }

        [Fact]
        public void MarkdownRenderer_Render_EmbedsCHARTCommentForBarVisual()
        {
            var manifest = new ReportManifest
            {
                Source   = "test.rptsql",
                BuiltAt  = DateTime.UtcNow,
                Visuals  = new List<VisualManifest>
                {
                    new VisualManifest
                    {
                        Name        = "SalesChart",
                        VisualType  = "BAR",
                        ChartConfig = "{\"type\":\"bar\",\"data\":{}}",
                        Columns     = new List<string> { "Month", "Revenue" },
                        Rows        = new List<List<string?>> { new List<string?> { "Jan", "1000" } }
                    }
                }
            };

            var md = new MarkdownRenderer().Render(manifest);

            Assert.Contains("<!-- ECHART:", md);
            Assert.Contains("\"type\":\"bar\"", md);
        }

        [Fact]
        public void MarkdownRenderer_Render_ProducesGfmTableForTableVisual()
        {
            var manifest = new ReportManifest
            {
                Source  = "test.rptsql",
                BuiltAt = DateTime.UtcNow,
                Visuals = new List<VisualManifest>
                {
                    new VisualManifest
                    {
                        Name       = "SummaryTable",
                        VisualType = "TABLE",
                        Columns    = new List<string> { "Name", "Value" },
                        Rows       = new List<List<string?>>
                        {
                            new List<string?> { "Alpha", "42" },
                            new List<string?> { "Beta",  "99" }
                        }
                    }
                }
            };

            var md = new MarkdownRenderer().Render(manifest);

            // GFM table: header row with |, separator row with ---, data rows
            Assert.Contains("| Name | Value |", md);
            Assert.Contains("| --- | --- |", md);
            Assert.Contains("| Alpha | 42 |", md);
        }

        [Fact]
        public void MarkdownRenderer_Render_EmitsCardBlockquote()
        {
            var manifest = new ReportManifest
            {
                Source  = "test.rptsql",
                BuiltAt = DateTime.UtcNow,
                Visuals = new List<VisualManifest>
                {
                    new VisualManifest
                    {
                        Name       = "TotalRevenue",
                        VisualType = "CARD",
                        Columns    = new List<string> { "Total" },
                        Rows       = new List<List<string?>> { new List<string?> { "1,234,567" } }
                    }
                }
            };

            var md = new MarkdownRenderer().Render(manifest);

            Assert.Contains(">", md);
            Assert.Contains("1,234,567", md);
        }

        // ── SnapshotStore ────────────────────────────────────────────────────

        [Fact]
        public async Task SnapshotStore_SaveAndLoad_RoundTrips()
        {
            var manifest = MakeSampleManifest("sample.rptsql");
            manifest.Visuals.Add(MakeSampleVisual("BAR"));

            var path  = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"snapshot_{Guid.NewGuid()}.json");
            var store = new SnapshotStore();

            try
            {
                await store.SaveAsync(manifest, path);
                var loaded = await store.LoadAsync(path);

                Assert.NotNull(loaded);
                Assert.Equal(manifest.Source, loaded!.Source);
                Assert.Single(loaded.Visuals);
                Assert.Equal("BAR", loaded.Visuals[0].VisualType);
            }
            finally
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        [Fact]
        public async Task SnapshotStore_LoadAsync_ReturnsNull_WhenFileNotFound()
        {
            var store = new SnapshotStore();
            var result = await store.LoadAsync("/non/existent/file.json");
            Assert.Null(result);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static VisualManifest MakeSampleVisual(string visualType) => new VisualManifest
        {
            Name       = "SampleVisual",
            VisualType = visualType,
            Columns    = new List<string> { "x", "y" },
            Rows       = new List<List<string?>>
            {
                new List<string?> { "A", "10" },
                new List<string?> { "B", "20" },
                new List<string?> { "C", "30" }
            },
            Options = new Dictionary<string, string>
            {
                { "mapping:x", "x" },
                { "mapping:y", "y" }
            }
        };

        private static ReportManifest MakeSampleManifest(string source) => new ReportManifest
        {
            Source  = source,
            BuiltAt = DateTime.UtcNow
        };
    }
}
