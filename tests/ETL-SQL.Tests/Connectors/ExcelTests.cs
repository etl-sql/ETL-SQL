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

        [Fact]
        [Trait("Connector", "EXCEL")]
        public async Task TestExcelSchemaResilience_MapByHeaderName_DuplicateHeaderThrows()
        {
            string outPath = Path.Combine(Path.GetTempPath(), "ExcelTest_Resilient_Duplicate_Header.xlsx");
            try
            {
                var dsWrite = new ExcelDataSource(SystemExecutionContext.Instance, outPath);
                var dtWrite = new ETL_SQL.Data.DataTable();
                dtWrite.SetColumns(new[] { "ID", "id", "name" });
                var row = dtWrite.NewRow();
                row["ID"] = 1;
                row["id"] = 2;
                row["name"] = "Alpha";
                await dtWrite.AddRowAsync(row);
                await dsWrite.WriteBatches(new[] { dtWrite }.ToAsyncEnumerable());

                var templateSchema = new List<ColumnDefinition>
                {
                    new ColumnDefinition("id", "INT", false),
                    new ColumnDefinition("name", "VARCHAR", false)
                };

                var options = new Dictionary<string, string>
                {
                    { "MAP_BY_HEADER_NAME", "ON" },
                    { "IGNORE_EXTRA_COLUMNS", "ON" }
                };

                var dsRead = new ExcelDataSource(SystemExecutionContext.Instance, outPath, options, templateSchema);

                var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
                {
                    await dsRead.ReadBatches().ToListAsync();
                });

                Assert.Contains("duplicate column", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        [Trait("Connector", "EXCEL")]
        public async Task TestExcelStartRow_SkipsBannerRowsWithAndWithoutHeader()
        {
            string outPath = Path.Combine(Path.GetTempPath(), $"ExcelTest_StartRow_{Guid.NewGuid():N}.xlsx");
            try
            {
                var sheets = new Dictionary<string, object>
                {
                    ["Report"] = new[]
                    {
                        new Dictionary<string, object?> { ["C1"] = "Quarterly Finance Report", ["C2"] = null },
                        new Dictionary<string, object?> { ["C1"] = "Confidential", ["C2"] = null },
                        new Dictionary<string, object?> { ["C1"] = "Generated 2026-11-23", ["C2"] = null },
                        new Dictionary<string, object?> { ["C1"] = "Id", ["C2"] = "Name" },
                        new Dictionary<string, object?> { ["C1"] = 10, ["C2"] = "Alpha" },
                        new Dictionary<string, object?> { ["C1"] = 20, ["C2"] = "Beta" }
                    }
                };
                MiniExcelLibs.MiniExcel.SaveAs(outPath, sheets, printHeader: false);

                var withHeader = new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "START_ROW", "4" },
                    { "HEADER", "ON" }
                });
                Assert.Equal(new[] { "Id", "Name" }, (await withHeader.GetColumnsAsync()).ToArray());
                var batches = await withHeader.ReadBatches().ToListAsync();
                Assert.Single(batches);
                Assert.Equal(new[] { "Id", "Name" }, batches[0].ColumnNames);
                Assert.Equal(2, batches[0].Rows.Count);
                Assert.Equal("10", batches[0].Rows[0]["Id"]?.ToString());
                Assert.Equal("Alpha", batches[0].Rows[0]["Name"]?.ToString());
                Assert.Equal("20", batches[0].Rows[1]["Id"]?.ToString());
                Assert.Equal("Beta", batches[0].Rows[1]["Name"]?.ToString());

                var withoutHeader = new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "START_ROW", "5" },
                    { "HEADER", "OFF" }
                });
                var noHeaderBatches = await withoutHeader.ReadBatches().ToListAsync();
                Assert.Single(noHeaderBatches);
                Assert.Equal(new[] { "Column1", "Column2" }, noHeaderBatches[0].ColumnNames);
                Assert.Equal(2, noHeaderBatches[0].Rows.Count);
                Assert.Equal("10", noHeaderBatches[0].Rows[0]["Column1"]?.ToString());
                Assert.Equal("Alpha", noHeaderBatches[0].Rows[0]["Column2"]?.ToString());

                var pastEnd = new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "START_ROW", "99" },
                    { "HEADER", "ON" }
                });
                Assert.Empty(await pastEnd.GetColumnsAsync());
                Assert.Empty(await pastEnd.ReadBatches().ToListAsync());
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        [Trait("Connector", "EXCEL")]
        public async Task TestExcelSheetIndex_ReadsTargetWorksheetAndValidatesOptions()
        {
            string outPath = Path.Combine(Path.GetTempPath(), $"ExcelTest_SheetIndex_{Guid.NewGuid():N}.xlsx");
            try
            {
                var sheets = new Dictionary<string, object>
                {
                    ["FirstTab"] = new[]
                    {
                        new Dictionary<string, object?> { ["C1"] = "Key", ["C2"] = "Val" },
                        new Dictionary<string, object?> { ["C1"] = "K1", ["C2"] = 1 }
                    },
                    ["SecondTab"] = new[]
                    {
                        new Dictionary<string, object?> { ["C1"] = "Title Banner", ["C2"] = null },
                        new Dictionary<string, object?> { ["C1"] = "Code", ["C2"] = "Amount" },
                        new Dictionary<string, object?> { ["C1"] = "X1", ["C2"] = 99 },
                        new Dictionary<string, object?> { ["C1"] = "X2", ["C2"] = 150 }
                    }
                };
                MiniExcelLibs.MiniExcel.SaveAs(outPath, sheets, printHeader: false);

                var connector = new ExcelConnector();
                var supported = connector.GetSupportedOptions();
                Assert.True(supported.ContainsKey("SHEET_INDEX"));
                Assert.True(supported.ContainsKey("START_ROW"));

                var secondSheet = new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "SHEET_INDEX", "2" },
                    { "START_ROW", "2" },
                    { "HEADER", "ON" }
                });
                Assert.Equal(new[] { "Code", "Amount" }, (await secondSheet.GetColumnsAsync()).ToArray());
                var batches = await secondSheet.ReadBatches().ToListAsync();
                Assert.Single(batches);
                Assert.Equal(2, batches[0].Rows.Count);
                Assert.Equal("X1", batches[0].Rows[0]["Code"]?.ToString());
                Assert.Equal("99", batches[0].Rows[0]["Amount"]?.ToString());
                Assert.Equal("X2", batches[0].Rows[1]["Code"]?.ToString());
                Assert.Equal("150", batches[0].Rows[1]["Amount"]?.ToString());

                var outOfBounds = new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "SHEET_INDEX", "3" }
                });
                Assert.Empty(await outOfBounds.ReadBatches().ToListAsync());

                Assert.Throws<ExecutionException>(() => new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "SHEET_INDEX", "0" }
                }));
                Assert.Throws<ExecutionException>(() => new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "START_ROW", "0" }
                }));
                Assert.Throws<ExecutionException>(() => new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "SHEET", "FirstTab" },
                    { "SHEET_INDEX", "2" }
                }));
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }
    }
}
