using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests.Reporting.Reporting
{
    /// <summary>
    /// Unit tests for native SVG and Markdown rendering.
    /// These tests work directly on pre-built manifests — no engine evaluation required.
    /// </summary>
    public class ReportBuilderTests
    {
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
        public void MarkdownRenderer_Render_EmbedsNativeSvgForBarVisual()
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
                        Columns     = new List<string> { "Month", "Revenue" },
                        Rows        = new List<List<string?>> { new List<string?> { "Jan", "1000" } }
                    }
                }
            };

            var md = new MarkdownRenderer().Render(manifest);

            Assert.Contains("data:image/svg+xml", md);
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
        public async Task SnapshotStore_DefaultPath_IsPartitionedAndLoadsLegacyFlatSnapshot()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snapshot_partition_" + Guid.NewGuid().ToString("N"));
            var scriptPath = System.IO.Path.Combine(root, "dashboards", "sales.rptsql");
            var manifest = MakeSampleManifest(scriptPath);
            manifest.Visuals.Add(MakeSampleVisual("TABLE"));
            var store = new SnapshotStore();

            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(scriptPath)!);
                await System.IO.File.WriteAllTextAsync(scriptPath, "REPORT sales;");

                var defaultPath = SnapshotStore.DefaultPath(scriptPath);
                var partitionRoot = System.IO.Directory.GetParent(defaultPath)!.Parent!.Parent!.FullName;
                Assert.Equal(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(scriptPath)!, ".etlsnap"), partitionRoot);
                Assert.Equal("sales.etlsnap", System.IO.Path.GetFileName(defaultPath));

                var legacyPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(scriptPath)!, "sales.etlsnap");
                await store.SaveAsync(manifest, legacyPath);

                var loaded = await store.LoadAsync(defaultPath);

                Assert.NotNull(loaded);
                Assert.Equal(manifest.Source, loaded!.Source);
                Assert.Single(loaded.Visuals);
            }
            finally
            {
                try { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true); } catch { }
            }
        }

        [Fact]
        public void SnapshotStore_CleanupOrphanedSnapshots_RecursesPartitionedDirectories()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "snapshot_cleanup_" + Guid.NewGuid().ToString("N"));
            try
            {
                var nested = System.IO.Path.Combine(root, ".etlsnap", "aa", "bb");
                System.IO.Directory.CreateDirectory(nested);
                var tmpJson = System.IO.Path.Combine(nested, "sales.snapshot.json.tmp.123");
                var tmpSnap = System.IO.Path.Combine(nested, "sales.etlsnap.tmp.123");
                var real = System.IO.Path.Combine(nested, "sales.etlsnap");
                System.IO.File.WriteAllText(tmpJson, "tmp");
                System.IO.File.WriteAllText(tmpSnap, "tmp");
                System.IO.File.WriteAllText(real, "real");

                SnapshotStore.CleanupOrphanedSnapshots(root);

                Assert.False(System.IO.File.Exists(tmpJson));
                Assert.False(System.IO.File.Exists(tmpSnap));
                Assert.True(System.IO.File.Exists(real));
            }
            finally
            {
                try { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, recursive: true); } catch { }
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
