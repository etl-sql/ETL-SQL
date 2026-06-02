using System.Collections.Generic;
using System.Reflection;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests
{
    /// <summary>
    /// Exercises the PDF exporter's font path. Without the custom IFontResolver this
    /// throws "The font 'Courier New' cannot be resolved" on any host lacking the
    /// Windows fonts; with it, a valid PDF is produced from an available OS font.
    /// </summary>
    public class PdfExportTests
    {
        [Fact]
        public void PdfExporter_RendersTableManifest_AsValidPdf()
        {
            var manifest = new ReportManifest
            {
                Title = "Sales",
                Source = "sales.rptsql",
                Visuals = new List<VisualManifest>
                {
                    new() { Name = "Summary", VisualType = "TABLE",
                            Columns = new List<string> { "region", "revenue" },
                            Rows = new List<List<string?>> { new() { "North", "1,000" }, new() { "South", "2,000" } } },
                }
            };

            var bytes = new PdfExporter().Export(manifest);

            Assert.NotNull(bytes);
            Assert.True(bytes.Length > 100, "PDF output is implausibly small");
            // PDF files start with the "%PDF" magic number.
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
        }

        [Fact]
        public void PdfExporter_RendersChart_ViaEChartsSsr()
        {
            var manifest = new ReportManifest
            {
                Title = "Charts",
                Source = "charts.rptsql",
                Visuals = new List<VisualManifest>
                {
                    new() { Name = "Bars", VisualType = "BAR",
                            ChartConfig = "{\"xAxis\":{\"type\":\"category\",\"data\":[\"A\",\"B\",\"C\"]},\"yAxis\":{\"type\":\"value\"},\"series\":[{\"type\":\"bar\",\"data\":[5,20,36]}]}" },
                }
            };

            var bytes = new PdfExporter().Export(manifest);

            Assert.True(bytes.Length > 100);
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
        }

        [Fact]
        public void PdfExporter_RendersFilterSelection_AndFormatsCells()
        {
            var manifest = new ReportManifest
            {
                Title = "Filtered",
                Source = "f.rptsql",
                Parameters = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["@region"] = "West",
                },
                Visuals = new List<VisualManifest>
                {
                    new() { Name = "Region", VisualType = "SLICER",
                            Actions = new List<VisualActionManifest> { new() { Type = "SET_PARAMETER", ParameterName = "@region" } } },
                    new() { Name = "Rows", VisualType = "TABLE",
                            Columns = new List<string> { "label", "amount" },
                            Rows = new List<List<string?>> { new() { "x", "581.12099471939179253810846960" } } },
                }
            };

            var bytes = new PdfExporter().Export(manifest);

            Assert.True(bytes.Length > 100);
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
        }

        [Fact]
        public void ReportPdfExporter_DefaultsToStaticPdf()
        {
            var manifest = new ReportManifest
            {
                Title = "Default PDF",
                Visuals = new List<VisualManifest>
                {
                    new() { Name = "Summary", VisualType = "CARD",
                            Columns = new List<string> { "metric" },
                            Rows = new List<List<string?>> { new() { "42" } } },
                }
            };

            var bytes = new ReportPdfExporter().Export(manifest);

            Assert.True(bytes.Length > 100);
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
        }

        [Fact]
        public void ReportPdfExporter_AutoFallsBackToStaticWithWarning()
        {
            string? warning = null;
            var manifest = new ReportManifest
            {
                Title = "Auto PDF",
                Visuals = new List<VisualManifest>
                {
                    new() { Name = "Summary", VisualType = "CARD",
                            Columns = new List<string> { "metric" },
                            Rows = new List<List<string?>> { new() { "42" } } },
                }
            };

            var bytes = new ReportPdfExporter().Export(
                manifest,
                new PdfExportOptions { Mode = PdfExportMode.Auto, Warn = message => warning = message });

            Assert.True(bytes.Length > 100);
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
            Assert.Contains("falling back to STATIC", warning);
        }

        [Theory]
        [InlineData(PdfExportMode.Hosted)]
        [InlineData(PdfExportMode.Browser)]
        public void ReportPdfExporter_ExplicitHighFidelityModesUseConfiguredExporter(PdfExportMode mode)
        {
            var manifest = new ReportManifest { Title = "Explicit PDF" };
            var expected = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
            var exporter = new ReportPdfExporter(highFidelityExporter: new StubReportPdfExporter(expected));

            var bytes = exporter.Export(manifest, new PdfExportOptions { Mode = mode, Host = "http://localhost/report" });

            Assert.Same(expected, bytes);
        }

        [Fact]
        public void ReportPdfExporter_AutoTriesConfiguredHighFidelityExporter()
        {
            var manifest = new ReportManifest { Title = "Auto PDF" };
            var expected = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D };
            var exporter = new ReportPdfExporter(highFidelityExporter: new StubReportPdfExporter(expected));

            var bytes = exporter.Export(manifest, new PdfExportOptions
            {
                Mode = PdfExportMode.Auto,
                Host = "http://localhost/report"
            });

            Assert.Same(expected, bytes);
        }

        [Fact]
        public void ReportPdfExporter_AutoFallsBackWhenHighFidelityExporterFails()
        {
            string? warning = null;
            var manifest = new ReportManifest
            {
                Title = "Auto PDF",
                Visuals = new List<VisualManifest>
                {
                    new() { Name = "Summary", VisualType = "CARD",
                            Columns = new List<string> { "metric" },
                            Rows = new List<List<string?>> { new() { "42" } } },
                }
            };
            var exporter = new ReportPdfExporter(highFidelityExporter: new ThrowingReportPdfExporter());

            var bytes = exporter.Export(manifest, new PdfExportOptions
            {
                Mode = PdfExportMode.Auto,
                Host = "http://localhost/report",
                Warn = message => warning = message
            });

            Assert.True(bytes.Length > 100);
            Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, bytes[..4]);
            Assert.Contains("falling back to STATIC", warning);
        }

        [Fact]
        public void PdfExporter_ResolvesTextVisualContentOption()
        {
            var visual = new VisualManifest
            {
                Name = "Narrative",
                VisualType = "TEXT",
                Options = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["CONTENT"] = "Revenue grew **12%**.",
                },
            };

            Assert.Equal("Revenue grew **12%**.", ResolvePdfTextContent(visual));
        }

        [Fact]
        public void PdfExporter_ResolvesMappedTextVisualContent()
        {
            var visual = new VisualManifest
            {
                Name = "Narrative",
                VisualType = "TEXT",
                Columns = new List<string> { "content", "other" },
                Rows = new List<List<string?>> { new() { "Revenue grew 12%.", "ignored" } },
                Options = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["mapping:content"] = "content",
                },
            };

            Assert.Equal("Revenue grew 12%.", ResolvePdfTextContent(visual));
        }

        [Fact]
        public void MarkdownRenderer_PreservesLongTableCellValues()
        {
            var longValue = "Customer note with enough detail to exceed the PDF table cell safety limit";
            var manifest = new ReportManifest
            {
                Title = "Markdown",
                Source = "markdown.rptsql",
                Visuals = new List<VisualManifest>
                {
                    new() { Name = "Rows", VisualType = "TABLE",
                            Columns = new List<string> { "note" },
                            Rows = new List<List<string?>> { new() { longValue } } },
                }
            };

            var markdown = new MarkdownRenderer().Render(manifest);

            Assert.Contains(longValue, markdown);
        }

        private static string? ResolvePdfTextContent(VisualManifest visual)
        {
            var method = typeof(PdfExporter).GetMethod(
                "ResolveTextContent",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            return (string?)method!.Invoke(null, new object[] { visual });
        }

        private sealed class StubReportPdfExporter(byte[] bytes) : IReportPdfExporter
        {
            public byte[] Export(ReportManifest manifest, PdfExportOptions? options = null) => bytes;
        }

        private sealed class ThrowingReportPdfExporter : IReportPdfExporter
        {
            public byte[] Export(ReportManifest manifest, PdfExportOptions? options = null) =>
                throw new System.InvalidOperationException("boom");
        }
    }
}
