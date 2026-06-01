using System.Collections.Generic;
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
    }
}
