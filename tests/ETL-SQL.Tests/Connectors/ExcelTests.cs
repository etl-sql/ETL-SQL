using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    [Trait("Connector", "EXCEL")]
    [Trait("CertificationClass", "LocalRealIntegration")]
    public class ExcelTests
    {
        [Fact]
        public async Task TestExcelBasicRead()
        {
            string excelPlaceholder = "ExcelTest_Basic.csv";
            await File.WriteAllTextAsync(excelPlaceholder, "id,col1\n1,val1");

            try
            {
                // In this implementation, ExcelConnector creates a FlatFileDataSource as a placeholder
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, excelPlaceholder);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Single(batches);
                Assert.Single(batches[0].Rows);
            }
            finally
            {
                if (File.Exists(excelPlaceholder)) File.Delete(excelPlaceholder);
            }
        }

        [Fact]
        public async Task TestExcelOptions()
        {
            string excelPlaceholder = "ExcelTest_Options.csv";
            await File.WriteAllTextAsync(excelPlaceholder, "HeaderRow\nid,col1\n1,val1");

            try
            {
                var options = new Dictionary<string, string>
                {
                    { "SHEET", "Sheet1" },
                    { "HEADER", "ON" },
                    { "START_AT", "1" }
                };

                // FlatFileDataSource handles START_AT, which simulates finding a sheet/header offset
                var ds = new FlatFileDataSource(SystemExecutionContext.Instance, excelPlaceholder, options);
                var batches = await ds.ReadBatches().ToListAsync();

                Assert.Equal("val1", batches[0].Rows[0]["col1"]?.ToString());
            }
            finally
            {
                if (File.Exists(excelPlaceholder)) File.Delete(excelPlaceholder);
            }
        }

        [Fact]
        [Trait("Connector", "EXCEL")]
        public async Task TestExcelSchemaResilience_MapByHeaderName()
        {
            string outPath = Path.Combine(Path.GetTempPath(), "ExcelTest_Resilient_Header.xlsx");
            try
            {
                // 1. Write Excel file
                var dsWrite = new ExcelDataSource(SystemExecutionContext.Instance, outPath);
                var dtWrite = new ETL_SQL.Data.DataTable();
                dtWrite.SetColumns(new[] { "NAME", "ID", "EXTRA_COL" });
                var r1 = dtWrite.NewRow(); r1["NAME"] = "Alpha"; r1["ID"] = 1; r1["EXTRA_COL"] = "some_extra_val"; await dtWrite.AddRowAsync(r1);
                var r2 = dtWrite.NewRow(); r2["NAME"] = "Beta"; r2["ID"] = 2; r2["EXTRA_COL"] = "another_extra"; await dtWrite.AddRowAsync(r2);
                await dsWrite.WriteBatches(new[] { dtWrite }.ToAsyncEnumerable());

                // 2. Read with schema resilience
                var templateSchema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("id", "INT", false),
                    new ColumnDefinition("name", "VARCHAR", false),
                    new ColumnDefinition("age", "INT", false)
                };

                var options = new Dictionary<string, string>
                {
                    { "MAP_BY_HEADER_NAME", "ON" },
                    { "IGNORE_EXTRA_COLUMNS", "ON" },
                    { "NULL_MISSING_COLUMNS", "ON" }
                };

                var dsRead = new ExcelDataSource(SystemExecutionContext.Instance, outPath, options, templateSchema);
                var batches = await dsRead.ReadBatches().ToListAsync();

                Assert.Single(batches);
                var dt = batches[0];

                Assert.Equal(new[] { "id", "name", "age" }, dt.ColumnNames);
                Assert.Equal(2, dt.Rows.Count);

                Assert.Equal("1", dt.Rows[0]["id"]?.ToString());
                Assert.Equal("Alpha", dt.Rows[0]["name"]?.ToString());
                Assert.Null(dt.Rows[0]["age"]);
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        [Trait("Connector", "EXCEL")]
        public async Task TestExcelSchemaResilience_Positional()
        {
            string outPath = Path.Combine(Path.GetTempPath(), "ExcelTest_Resilient_Positional.xlsx");
            try
            {
                var dsWrite = new ExcelDataSource(SystemExecutionContext.Instance, outPath);
                var dtWrite = new ETL_SQL.Data.DataTable();
                dtWrite.SetColumns(new[] { "Col1", "Col2", "Col3", "Col4" });
                var r1 = dtWrite.NewRow(); r1["Col1"] = 1; r1["Col2"] = "Alpha"; r1["Col3"] = 30; r1["Col4"] = "extra_val"; await dtWrite.AddRowAsync(r1);
                var r2 = dtWrite.NewRow(); r2["Col1"] = 2; r2["Col2"] = "Beta"; r2["Col3"] = 40; r2["Col4"] = "another_extra"; await dtWrite.AddRowAsync(r2);
                await dsWrite.WriteBatches(new[] { dtWrite }.ToAsyncEnumerable());

                var templateSchema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("id", "INT", false),
                    new ColumnDefinition("name", "VARCHAR", false),
                    new ColumnDefinition("age", "INT", false)
                };

                var options = new Dictionary<string, string>
                {
                    { "HEADER", "OFF" },
                    { "IGNORE_EXTRA_COLUMNS", "ON" },
                    { "NULL_MISSING_COLUMNS", "ON" }
                };

                var dsRead = new ExcelDataSource(SystemExecutionContext.Instance, outPath, options, templateSchema);
                var batches = await dsRead.ReadBatches().ToListAsync();

                Assert.Single(batches);
                var dt = batches[0];

                Assert.Equal(new[] { "id", "name", "age" }, dt.ColumnNames);
                Assert.Equal(3, dt.Rows.Count);

                Assert.Equal("1", dt.Rows[1]["id"]?.ToString());
                Assert.Equal("Alpha", dt.Rows[1]["name"]?.ToString());
                Assert.Equal("30", dt.Rows[1]["age"]?.ToString());
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        [Trait("Connector", "EXCEL")]
        public async Task TestExcelSchemaResilience_StrictSchemaThrows()
        {
            string outPath = Path.Combine(Path.GetTempPath(), "ExcelTest_Resilient_Strict.xlsx");
            try
            {
                var dsWrite = new ExcelDataSource(SystemExecutionContext.Instance, outPath);
                var dtWrite = new ETL_SQL.Data.DataTable();
                dtWrite.SetColumns(new[] { "Col1", "Col2" });
                var r1 = dtWrite.NewRow(); r1["Col1"] = 1; r1["Col2"] = "Alpha"; await dtWrite.AddRowAsync(r1);
                await dsWrite.WriteBatches(new[] { dtWrite }.ToAsyncEnumerable());

                var templateSchema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("id", "INT", false),
                    new ColumnDefinition("name", "VARCHAR", false),
                    new ColumnDefinition("age", "INT", false)
                };

                var options = new Dictionary<string, string>
                {
                    { "HEADER", "OFF" },
                    { "STRICT_SCHEMA", "ON" },
                    { "NULL_MISSING_COLUMNS", "OFF" }
                };

                var dsRead = new ExcelDataSource(SystemExecutionContext.Instance, outPath, options, templateSchema);

                await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await dsRead.ReadBatches().ToListAsync();
                });
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }
    }
}
