using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Reporting
{
    /// <summary>
    /// Phase 9B: Unit tests for EChartsRenderer and MarkdownRenderer.
    /// These tests work directly on pre-built manifests — no engine evaluation required.
    /// </summary>
    public class ReportBuilderTests
    {
        // ── EChartsRenderer ──────────────────────────────────────────────────

        [Theory]
        [InlineData("BAR")]
        [InlineData("LINE")]
        [InlineData("SCATTER")]
        [InlineData("PIE")]
        public void EChartsRenderer_ProducesValidConfig_ForChartVisualTypes(string visualType)
        {
            var visual = MakeSampleVisual(visualType);
            var renderer = new EChartsRenderer();

            var config = renderer.Render(visual);

            Assert.NotNull(config);
            Assert.Contains(visualType.ToLowerInvariant() == "pie" ? "radius" : "series", config);
            Assert.Contains("\"type\"", config);
        }

        [Theory]
        [InlineData("TABLE")]
        [InlineData("CARD")]
        [InlineData("SLICER")]
        public void EChartsRenderer_ReturnsNull_ForNonChartVisualTypes(string visualType)
        {
            var visual = MakeSampleVisual(visualType);
            var renderer = new EChartsRenderer();

            Assert.Null(renderer.Render(visual));
        }

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public void EChartsRenderer_Bar_ContainsCorrectChartType()
        {
            var visual = MakeSampleVisual("BAR");
            var renderer = new EChartsRenderer();
            var config = renderer.Render(visual)!;

            Assert.Contains("\"type\":\"bar\"", config);
        }

        [Fact]
        public void EChartsRenderer_Pie_ContainsBackgroundColors()
        {
            // ECharts uses itemStyle.color for pie slices
            var visual = MakeSampleVisual("PIE");
            var renderer = new EChartsRenderer();
            var config = renderer.Render(visual)!;

            Assert.Contains("color", config);
        }

        [Fact]
        public void EChartsRenderer_Scatter_ContainsSeries()
        {
            var visual = MakeSampleVisual("SCATTER");
            var renderer = new EChartsRenderer();
            var config = renderer.Render(visual)!;

            Assert.Contains("\"type\":\"scatter\"", config);
            Assert.Contains("\"data\"", config);
        }

        [Fact]
        public void EChartsRenderer_Bar_YAxisLabel_AppearsInJson()
        {
            // VisualBuilder writes axis label as "axis:y:label" (lowercase computed key).
            // Regression guard: earlier code read "AXIS:Y:LABEL" (uppercase) and silently dropped labels.
            var visual = MakeSampleVisual("BAR");
            visual.Options["axis:y:label"] = "Revenue";
            var renderer = new EChartsRenderer();

            var config = renderer.Render(visual)!;

            Assert.Contains("\"name\":\"Revenue\"", config);
        }

        [Fact]
        public void EChartsRenderer_Title_AppearsInJson()
        {
            // VisualBuilder writes the title as lowercase "title"; EChartsRenderer must read it the same way.
            // Regression guard: earlier code read "TITLE" (uppercase) and fell back to visual.Name.
            var visual = MakeSampleVisual("BAR");
            visual.Options["title"] = "My Report Title";
            var renderer = new EChartsRenderer();

            var config = renderer.Render(visual)!;

            Assert.Contains("\"text\":\"My Report Title\"", config);
            Assert.DoesNotContain("SampleVisual", config.Split("\"text\":")[1].Split("\"")[1]);
        }

        [Fact]
        public void EChartsRenderer_Combo_DualYAxis_WhenBarAndLineMixed()
        {
            // A COMBO with exactly 2 series of different types (BAR + LINE) must produce
            // a dual Y-axis array so the two scales don't crush each other.
            var visual = new VisualManifest
            {
                Name = "ComboChart",
                VisualType = "COMBO",
                Columns = new List<string> { "Month", "Revenue", "ReturnRate" },
                Rows = new List<List<string?>>
                {
                    new List<string?> { "Jan", "100000", "5.2" },
                    new List<string?> { "Feb", "120000", "4.8" }
                },
                SeriesDefs = new List<SeriesDefManifest>
                {
                    new SeriesDefManifest { Column = "Revenue",    SeriesType = "BAR"  },
                    new SeriesDefManifest { Column = "ReturnRate", SeriesType = "LINE" }
                },
                Options = new Dictionary<string, string>
                {
                    { "mapping:x", "Month" }
                }
            };

            var config = new EChartsRenderer().Render(visual)!;

            // Dual Y-axis: "yAxis" must be a JSON array, not a single object.
            Assert.Contains("\"yAxis\":[", config);
            // Both series must be present
            Assert.Contains("\"Revenue\"", config);
            Assert.Contains("\"ReturnRate\"", config);
        }

        [Fact]
        public void EChartsRenderer_Waterfall_CustomColors_AppearedInDeltas()
        {
            // GetColor reads "COLOR:POSITIVE" / "COLOR:NEGATIVE" from Options.
            // Regression guard: earlier case-insensitive lookup issue could drop colors.
            var visual = new VisualManifest
            {
                Name = "WaterfallChart",
                VisualType = "WATERFALL",
                Columns = new List<string> { "Step", "Amount" },
                Rows = new List<List<string?>>
                {
                    new List<string?> { "Start",    "1000" },
                    new List<string?> { "Increase",  "500" },
                    new List<string?> { "Decrease", "-200" }
                },
                Options = new Dictionary<string, string>
                {
                    { "COLOR:POSITIVE", "#00cc44" },
                    { "COLOR:NEGATIVE", "#cc0044" }
                }
            };

            var config = new EChartsRenderer().Render(visual)!;

            Assert.Contains("#00cc44", config);
            Assert.Contains("#cc0044", config);
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
                Source = "test.rptsql",
                BuiltAt = DateTime.UtcNow,
                Visuals = new List<VisualManifest>
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
                Source = "test.rptsql",
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
                Source = "test.rptsql",
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

        // ── CsvRenderer ─────────────────────────────────────────────────────

        [Fact]
        public void CsvRenderer_Render_EscapesQuotedAndDelimitedFields()
        {
            var manifest = new ReportManifest
            {
                Source = "test.rptsql",
                Visuals = new List<VisualManifest>
                {
                    new VisualManifest
                    {
                        Name = "CsvTable",
                        VisualType = "TABLE",
                        Columns = new List<string> { "Name", "Notes" },
                        Rows = new List<List<string?>>
                        {
                            new List<string?> { "Alpha, Inc.", "Said \"hello\"" }
                        }
                    }
                }
            };

            var csv = new CsvRenderer().Render(manifest);

            Assert.Contains("Name,Notes", csv);
            Assert.Contains("\"Alpha, Inc.\",\"Said \"\"hello\"\"\"", csv);
        }

        [Fact]
        public void CsvRenderer_Render_LabelsMultiplePortalTables()
        {
            var manifest = new ReportManifest
            {
                Source = "test.rptsql",
                Visuals = new List<VisualManifest>
                {
                    new VisualManifest
                    {
                        Name = "FirstTable",
                        VisualType = "TABLE",
                        Columns = new List<string> { "Name" },
                        Rows = new List<List<string?>> { new List<string?> { "Alpha" } }
                    },
                    new VisualManifest
                    {
                        Name = "SecondTable",
                        VisualType = "TABLE",
                        Columns = new List<string> { "Name" },
                        Rows = new List<List<string?>> { new List<string?> { "Beta" } }
                    }
                }
            };

            var csv = new CsvRenderer().Render(manifest, visualName: null, includeVisualNamesWhenMultiple: true);

            Assert.Contains("FirstTable", csv);
            Assert.Contains("SecondTable", csv);
            Assert.Contains("Alpha", csv);
            Assert.Contains("Beta", csv);
        }

        // ── SnapshotStore ────────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Smoke.Reporting")]
        public async Task SnapshotStore_SaveAndLoad_RoundTrips()
        {
            var manifest = MakeSampleManifest("sample.rptsql");
            manifest.Visuals.Add(MakeSampleVisual("BAR"));

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"snapshot_{Guid.NewGuid()}.json");
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
        [Trait("Category", "Smoke.Reporting")]
        public async Task SnapshotStore_SaveAndLoad_EtlSnap_RoundTrips()
        {
            var manifest = MakeSampleManifest("sample.rptsql");
            manifest.Visuals.Add(MakeSampleVisual("BAR"));

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"snapshot_{Guid.NewGuid()}.etlsnap");
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
            Name = "SampleVisual",
            VisualType = visualType,
            Columns = new List<string> { "x", "y" },
            Rows = new List<List<string?>>
            {
                new List<string?> { "A", "10" },
                new List<string?> { "B", "20" },
                new List<string?> { "C", "30" }
            },
            Options = new Dictionary<string, string>
            {
                { "MAPPING:X", "x" },
                { "MAPPING:Y", "y" },
                { "COLOR:A",   "red" }
            }
        };

        private static ReportManifest MakeSampleManifest(string source) => new ReportManifest
        {
            Source = source,
            BuiltAt = DateTime.UtcNow
        };
    }
}
