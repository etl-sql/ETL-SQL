using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using ExcelDataReader;
using ETL_SQL.Reporting;
using Xunit;

namespace ETL_SQL.Tests
{
    /// <summary>
    /// Round-trips the native .xlsx export back through ExcelDataReader (already a
    /// repo dependency) to prove the writer emits a valid workbook with TYPED cells
    /// — the whole reason for native xlsx over CSV-opened-in-Excel.
    /// </summary>
    public class XlsxExportTests
    {
        static XlsxExportTests()
        {
            // ExcelDataReader needs the legacy code-page provider (1252) registered,
            // the same as the production Excel connector (ExcelDataSource.cs).
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        private static DataSet ReadWorkbook(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var reader = ExcelReaderFactory.CreateReader(ms);
            return reader.AsDataSet(new ExcelDataSetConfiguration
            {
                ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = true }
            });
        }

        [Fact]
        public async Task XlsxWriter_EmitsTypedCells_AndRoundTrips()
        {
            var columns = new List<XlsxWriter.Column>
            {
                new("region",     "NVARCHAR"),
                new("units",      "INT"),
                new("revenue",    "DECIMAL(18,2)"),
                new("order_date", "DATE"),
            };
            var rows = new List<IDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["region"] = "North", ["units"] = 299, ["revenue"] = 5971.21m, ["order_date"] = new DateTime(2026, 1, 1) },
                new Dictionary<string, object?> { ["region"] = "South", ["units"] = 337, ["revenue"] = 9829.66m, ["order_date"] = new DateTime(2026, 2, 2) },
                new Dictionary<string, object?> { ["region"] = "East",  ["units"] = null, ["revenue"] = null,    ["order_date"] = null },
            };

            using var ms = new MemoryStream();
            await XlsxWriter.WriteAsync(ms, columns, rows, "Data");

            var table = ReadWorkbook(ms.ToArray()).Tables["Data"];
            Assert.NotNull(table);

            // Header + shape
            Assert.Equal(new[] { "region", "units", "revenue", "order_date" },
                         new[] { table!.Columns[0].ColumnName, table.Columns[1].ColumnName, table.Columns[2].ColumnName, table.Columns[3].ColumnName });
            Assert.Equal(3, table.Rows.Count);

            // Typed cells: numbers come back as numbers, dates as dates (NOT text).
            Assert.IsType<double>(table.Rows[0]["units"]);
            Assert.Equal(299d, Convert.ToDouble(table.Rows[0]["units"]));
            Assert.IsType<double>(table.Rows[0]["revenue"]);
            Assert.Equal(5971.21d, Convert.ToDouble(table.Rows[0]["revenue"]), 2);
            Assert.IsType<DateTime>(table.Rows[1]["order_date"]);
            Assert.Equal(new DateTime(2026, 2, 2), (DateTime)table.Rows[1]["order_date"]);

            // Nulls stay empty (not the string "null").
            Assert.True(table.Rows[2].IsNull("units"));
            Assert.Equal("East", table.Rows[2]["region"]);
        }

        [Fact]
        public async Task XlsxExporter_WritesOneSheetPerTableVisual()
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
                    new() { Name = "Detail", VisualType = "TABLE",
                            Columns = new List<string> { "id", "amount" },
                            Rows = new List<List<string?>> { new() { "1", "10" } } },
                    // Non-table visuals are skipped by SelectExportVisuals.
                    new() { Name = "Chart", VisualType = "BAR",
                            Columns = new List<string> { "x", "y" },
                            Rows = new List<List<string?>> { new() { "a", "1" } } },
                }
            };

            var visuals = new CsvRenderer().SelectExportVisuals(manifest, visualName: null);
            var bytes = await new XlsxExporter().ExportAsync(visuals);
            var ds = ReadWorkbook(bytes);

            Assert.Equal(2, ds.Tables.Count);
            Assert.NotNull(ds.Tables["Summary"]);
            Assert.NotNull(ds.Tables["Detail"]);
            Assert.Equal(2, ds.Tables["Summary"]!.Rows.Count);
            Assert.Equal("North", ds.Tables["Summary"]!.Rows[0]["region"]);
            // Display strings preserved verbatim (the formatted "1,000" is not re-coerced).
            Assert.Equal("1,000", ds.Tables["Summary"]!.Rows[0]["revenue"]);
        }
    }
}
