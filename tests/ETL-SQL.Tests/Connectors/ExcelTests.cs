using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
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

        [Theory]
        [Trait("Connector", "EXCEL")]
        [InlineData("SHEET_INDEX", "1", true)]
        [InlineData("SHEET_INDEX", "1", false)]
        [InlineData("START_ROW", "2", true)]
        [InlineData("START_ROW", "2", false)]
        public async Task TestExcelReadOnlyOptions_RejectWritesAndLeaveTheWorkbookUnchanged(string option, string value, bool append)
        {
            // The writer names its sheet from SHEET only, so a write through SHEET_INDEX used to
            // move the selected sheet's rows into a new "Sheet1", and START_ROW was ignored on append.
            string outPath = Path.Combine(Path.GetTempPath(), $"ExcelTest_ReadOnlyOpt_{Guid.NewGuid():N}.xlsx");
            try
            {
                var original = new Dictionary<string, object>
                {
                    ["First"] = new[] { new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "one" } }
                };
                MiniExcelLibs.MiniExcel.SaveAs(outPath, original);
                var before = await File.ReadAllBytesAsync(outPath);

                var ds = new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { option, value },
                    { "HEADER", "ON" }
                });
                var dt = new DataTable();
                dt.SetColumns(new[] { "Id", "Name" });
                var row = dt.NewRow(); row["Id"] = 2; row["Name"] = "two"; await dt.AddRowAsync(row);

                var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.WriteBatches(new[] { dt }.ToAsyncEnumerable(), append));
                Assert.Contains(option, ex.Message);
                Assert.Contains("read", ex.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(before, await File.ReadAllBytesAsync(outPath));
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        [Trait("Connector", "EXCEL")]
        public async Task TestExcelWithTable_PreservesOtherSheetsAndSupportsOverwriteWorkbook()
        {
            string outPath = Path.Combine(Path.GetTempPath(), $"ExcelTest_MultiSheet_{Guid.NewGuid():N}.xlsx");
            try
            {
                var baseDs = new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "HEADER", "ON" }
                });

                // Write Summary and Detail through a single connection via WithTable (xl_conn.Summary / xl_conn.Detail)
                var summaryDt = new DataTable();
                summaryDt.SetColumns(new[] { "Region", "Total" });
                var sr1 = summaryDt.NewRow(); sr1["Region"] = "North"; sr1["Total"] = 100m; await summaryDt.AddRowAsync(sr1);
                var sr2 = summaryDt.NewRow(); sr2["Region"] = "South"; sr2["Total"] = 200m; await summaryDt.AddRowAsync(sr2);

                var detailDt = new DataTable();
                detailDt.SetColumns(new[] { "OrderId", "Item" });
                var dr1 = detailDt.NewRow(); dr1["OrderId"] = 1m; dr1["Item"] = "Widget"; await detailDt.AddRowAsync(dr1);

                await baseDs.WithTable("Summary").WriteBatches(new[] { summaryDt }.ToAsyncEnumerable(), append: false);
                await baseDs.WithTable("Detail").WriteBatches(new[] { detailDt }.ToAsyncEnumerable(), append: false);

                var tables = (await baseDs.GetTablesAsync()).ToList();
                Assert.Equal(new[] { "Summary", "Detail" }, tables);

                // Read each sheet independently via WithTable
                var summaryRead = await baseDs.WithTable("Summary").ReadBatches().ToListAsync();
                Assert.Single(summaryRead);
                Assert.Equal(2, summaryRead[0].Rows.Count);
                Assert.Equal("North", summaryRead[0].Rows[0]["Region"]?.ToString());
                Assert.Equal("100", summaryRead[0].Rows[0]["Total"]?.ToString());

                var detailRead = await baseDs.WithTable("Detail").ReadBatches().ToListAsync();
                Assert.Single(detailRead);
                Assert.Single(detailRead[0].Rows);
                Assert.Equal("Widget", detailRead[0].Rows[0]["Item"]?.ToString());

                // WithTable("FILE") keeps the connection's default sheet binding
                Assert.Same(baseDs, baseDs.WithTable("FILE"));

                // Replacing Summary with OVERWRITE_WORKBOOK = OFF (default) preserves Detail
                var summaryV2 = new DataTable();
                summaryV2.SetColumns(new[] { "Region", "Total" });
                var sr3 = summaryV2.NewRow(); sr3["Region"] = "West"; sr3["Total"] = 300m; await summaryV2.AddRowAsync(sr3);
                await baseDs.WithTable("Summary").WriteBatches(new[] { summaryV2 }.ToAsyncEnumerable(), append: false);

                Assert.Equal(new[] { "Summary", "Detail" }, (await baseDs.GetTablesAsync()).ToList());
                var summaryV2Read = await baseDs.WithTable("Summary").ReadBatches().ToListAsync();
                Assert.Single(summaryV2Read[0].Rows);
                Assert.Equal("West", summaryV2Read[0].Rows[0]["Region"]?.ToString());
                var detailStillThere = await baseDs.WithTable("Detail").ReadBatches().ToListAsync();
                Assert.Single(detailStillThere[0].Rows);
                Assert.Equal("Widget", detailStillThere[0].Rows[0]["Item"]?.ToString());

                // OVERWRITE_WORKBOOK = ON replaces the entire workbook with only the written sheet
                var overwriteDs = new ExcelDataSource(SystemExecutionContext.Instance, outPath, new Dictionary<string, string>
                {
                    { "HEADER", "ON" },
                    { "OVERWRITE_WORKBOOK", "ON" }
                });
                await overwriteDs.WithTable("OnlySheet").WriteBatches(new[] { detailDt }.ToAsyncEnumerable(), append: false);
                Assert.Equal(new[] { "OnlySheet" }, (await overwriteDs.GetTablesAsync()).ToList());
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }

        [Fact]
        [Trait("Connector", "EXCEL")]
        public async Task TestExcelSheetDiscovery_FunctionMetadataViewAndForeachIngest()
        {
            string outPath = Path.Combine(Path.GetTempPath(), $"ExcelTest_SheetList_{Guid.NewGuid():N}.xlsx");
            try
            {
                var sheets = new Dictionary<string, object>
                {
                    ["Jan"] = new[]
                    {
                        new Dictionary<string, object?> { ["Item"] = "A", ["Qty"] = 10 }
                    },
                    ["Feb"] = new[]
                    {
                        new Dictionary<string, object?> { ["Item"] = "B", ["Qty"] = 20 },
                        new Dictionary<string, object?> { ["Item"] = "C", ["Qty"] = 30 }
                    }
                };
                MiniExcelLibs.MiniExcel.SaveAs(outPath, sheets);

                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                var sql = $"""
                    SELECT SheetIndex, SheetName FROM EXCEL_SHEET_LIST('{outPath}') ORDER BY SheetIndex;
                    """;
                await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());

                Assert.NotNull(eval.LastResult);
                Assert.Equal(2, eval.LastResult!.Rows.Count);
                Assert.Equal(1m, Convert.ToDecimal(eval.LastResult.Rows[0]["SheetIndex"]));
                Assert.Equal("Jan", eval.LastResult.Rows[0]["SheetName"]);
                Assert.Equal(2m, Convert.ToDecimal(eval.LastResult.Rows[1]["SheetIndex"]));
                Assert.Equal("Feb", eval.LastResult.Rows[1]["SheetName"]);

                // Query _sheets metadata view and ingest all sheets in a FOREACH loop
                var ingestSql = $"""
                    CREATE CONNECTION xl_book AS EXCEL(PATH = '{outPath}', HEADER = ON);
                    CREATE TABLE #all_items (SheetName VARCHAR, Item VARCHAR, Qty INT);
                    SELECT SheetIndex, SheetName INTO #tabs FROM xl_book._sheets ORDER BY SheetIndex;

                    FOREACH @tab IN (SELECT SheetName FROM #tabs)
                    BEGIN
                        ALTER CONNECTION xl_book WITH (SHEET = @tab.SheetName);
                        INSERT INTO #all_items (SheetName, Item, Qty)
                        SELECT @tab.SheetName, Item, Qty FROM xl_book.FILE;
                    END;

                    SELECT SheetName, Item, Qty FROM #all_items ORDER BY Item;
                    """;
                await eval.Evaluate(new Parser(new Lexer(ingestSql).Tokenize()).Parse());

                Assert.NotNull(eval.LastResult);
                Assert.Equal(3, eval.LastResult!.Rows.Count);
                Assert.Equal("Jan", eval.LastResult.Rows[0]["SheetName"]);
                Assert.Equal("A", eval.LastResult.Rows[0]["Item"]);
                Assert.Equal(10m, Convert.ToDecimal(eval.LastResult.Rows[0]["Qty"]));
                Assert.Equal("Feb", eval.LastResult.Rows[1]["SheetName"]);
                Assert.Equal("B", eval.LastResult.Rows[1]["Item"]);
                Assert.Equal("Feb", eval.LastResult.Rows[2]["SheetName"]);
                Assert.Equal("C", eval.LastResult.Rows[2]["Item"]);

                Assert.Contains("EXCEL_SHEET_LIST", LanguageMetadata.Functions);
            }
            finally { if (File.Exists(outPath)) File.Delete(outPath); }
        }
    }
}
