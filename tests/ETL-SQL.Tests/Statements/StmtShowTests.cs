using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Statements.Statements
{
    /// <summary>
    /// CQ-T2: Coverage for SHOW TABLES, SHOW COLUMNS, SHOW CONNECTIONS, SHOW JOB HISTORY handlers.
    /// </summary>
    public class ShowStatementTests
    {
        private static Evaluator NewEval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        // ── SHOW TABLES ────────────────────────────────────────────────────────

        [Fact]
        public async Task ShowTables_WithNoConnections_ReturnsExpectedColumns()
        {
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse("SHOW TABLES;"));

            Assert.NotNull(eval.LastResult);
            var cols = eval.LastResult!.ColumnNames.ToArray();
            Assert.Contains("TableName", cols);
            Assert.Contains("Type", cols);
        }

        [Fact]
        public async Task ShowTables_WithFlatFile_ListsTables()
        {
            // Use a CSV file so SHOW TABLES has a real source to query
            var csvPath = Path.Combine(Path.GetTempPath(), $"show_tables_test_{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, "id,name\n1,Alice\n2,Bob");

            try
            {
                var script = $@"
CREATE CONNECTION mycsv AS FLATFILE('{csvPath}');
SHOW TABLES ON mycsv;";
                var eval = NewEval();
                await eval.Evaluate(TestHelpers.Parse(script));

                Assert.NotNull(eval.LastResult);
                // FlatFile returns the file name as the single table
                Assert.True(eval.LastResult!.Rows.Count >= 1,
                    "SHOW TABLES for a flat file connection should return at least one entry");
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task ShowTables_IntoTable_CompletesWithoutError()
        {
            var eval = NewEval();
            // SHOW TABLES INTO #result — may produce 0 rows if no connections, but should not throw
            await eval.Evaluate(TestHelpers.Parse("SHOW TABLES INTO #result;"));
            // Can now select from #result
            await eval.Evaluate(TestHelpers.Parse("SELECT * FROM #result;"));
            Assert.NotNull(eval.LastResult);
        }

        // ── eng.columns ────────────────────────────────────────────────────────

        [Fact]
        public async Task EngColumns_ForTempTable_ReturnsColumnNames()
        {
            var script = @"
CREATE TABLE #employees (id INT, name VARCHAR(100), salary DECIMAL);
SELECT * FROM eng.columns WHERE table_name = '#employees';";
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse(script));

            Assert.NotNull(eval.LastResult);
            var colNames = eval.LastResult!.Rows.Select(r => r["column_name"]?.ToString()).ToList();
            Assert.Contains("id", colNames);
            Assert.Contains("name", colNames);
            Assert.Contains("salary", colNames);
        }

        [Fact]
        public async Task EngColumns_ForTempTable_ReturnsColumnsAndMetadata()
        {
            var script = @"
CREATE TABLE #products (id INT, name VARCHAR(100), price DECIMAL);
SELECT * FROM eng.columns WHERE table_name = '#products';";
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse(script));

            Assert.NotNull(eval.LastResult);
            var cols = eval.LastResult!.ColumnNames.ToArray();
            Assert.Contains("column_name", cols);
            Assert.Contains("data_type", cols);
            Assert.Contains("is_nullable", cols);
            Assert.Contains("tags", cols);

            var colNames = eval.LastResult!.Rows.Select(r => r["column_name"]?.ToString()).ToList();
            Assert.Contains("id", colNames);
            Assert.Contains("name", colNames);
            Assert.Contains("price", colNames);
        }

        [Fact]
        public async Task EngColumns_ForFlatFileConnection_ReturnsHeaderColumns()
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"show_schema_file_{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, "id,name,last_modified\n1,Alice,2026-07-22");

            try
            {
                var script = $@"
CREATE CONNECTION mycsv AS FLATFILE('{csvPath}');
SELECT * FROM eng.columns WHERE table_name LIKE 'mycsv.%';";
                var eval = NewEval();
                await eval.Evaluate(TestHelpers.Parse(script));

                Assert.NotNull(eval.LastResult);
                var colNames = eval.LastResult!.Rows.Select(r => r["column_name"]?.ToString()).ToList();
                Assert.Contains("id", colNames);
                Assert.Contains("name", colNames);
                Assert.Contains("last_modified", colNames);
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task EngColumns_ReturnsExpectedSchema()
        {
            var script = @"
CREATE TABLE #t (a INT);
SELECT * FROM eng.columns WHERE table_name = '#t';";
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse(script));

            Assert.NotNull(eval.LastResult);
            var cols = eval.LastResult!.ColumnNames.ToArray();
            Assert.Contains("column_name", cols);
            Assert.Contains("data_type", cols);
        }

        [Theory]
        [InlineData("SHOW COLUMNS FOR #t;")]
        [InlineData("SHOW SCHEMA FOR #t;")]
        [InlineData("DESCRIBE #t;")]
        public void RetiredColumnInspectionSyntax_IsRejected(string sql)
        {
            var ex = Assert.Throws<ETL_SQL.Core.Common.Exceptions.SyntaxException>(() => TestHelpers.Parse(sql));
            Assert.Contains("eng.columns", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── SHOW CONNECTIONS ───────────────────────────────────────────────────

        [Fact]
        public async Task ShowConnections_WithNoConnections_ReturnsExpectedColumns()
        {
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse("SHOW CONNECTIONS;"));

            Assert.NotNull(eval.LastResult);
            Assert.Equal(new[] { "Name", "Type", "Details" },
                eval.LastResult!.ColumnNames.ToArray());
        }

        [Fact]
        public async Task ShowConnections_AfterCreateConnection_ReturnsConnectionRow()
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"showconn_{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, "x,y\n1,2");

            try
            {
                var script = $@"
CREATE CONNECTION myconn AS FLATFILE('{csvPath}');
SHOW CONNECTIONS;";
                var eval = NewEval();
                await eval.Evaluate(TestHelpers.Parse(script));

                Assert.NotNull(eval.LastResult);
                var connNames = eval.LastResult!.Rows.Select(r => r["Name"]?.ToString()).ToList();
                Assert.Contains("myconn", connNames);
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task ShowConnections_IntoTable_PopulatesDestination()
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"showconn2_{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, "x\n1");

            try
            {
                var script = $@"
CREATE CONNECTION c1 AS FLATFILE('{csvPath}');
SHOW CONNECTIONS INTO #conn_list;
SELECT * FROM #conn_list;";
                var eval = NewEval();
                await eval.Evaluate(TestHelpers.Parse(script));

                Assert.NotNull(eval.LastResult);
                Assert.True(eval.LastResult!.Rows.Count >= 1);
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        // ── SHOW JOB HISTORY ───────────────────────────────────────────────────

        [Fact]
        public async Task ShowJobHistory_WithMockStore_ReturnsHistoryRows()
        {
            var entries = new List<JobHistoryEntry>
            {
                new(1L, "Job1", new DateTime(2026, 1, 1), new DateTime(2026, 1, 1, 0, 1, 0), "SUCCESS", null, 100),
                new(2L, "Job1", new DateTime(2026, 1, 2), null, "RUNNING", null, 0),
            };

            var mockStore = new Mock<IJobHistoryStore>();
            // The stmt will have JobName="Job1" parsed from: SHOW JOB HISTORY 'Job1'
            mockStore.Setup(s => s.GetHistoryAsync("Job1", It.IsAny<int>())).ReturnsAsync(entries);

            var handler = new ShowJobHistoryStatementHandler(mockStore.Object);

            // Correct syntax: no FOR keyword
            var stmt = (ShowJobHistoryStatement)TestHelpers.Parse("SHOW JOB HISTORY 'Job1';").Statements[0];

            var eval = NewEval();
            await handler.Execute(stmt, eval);

            Assert.NotNull(eval.LastResult);
            Assert.Equal(2, eval.LastResult!.Rows.Count);
            Assert.Equal("SUCCESS", eval.LastResult.Rows[0]["Status"]?.ToString());
            Assert.Equal("RUNNING", eval.LastResult.Rows[1]["Status"]?.ToString());
        }

        [Fact]
        public async Task ShowJobHistory_EmptyStore_ReturnsNoRows()
        {
            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.GetHistoryAsync(null, It.IsAny<int>()))
                     .ReturnsAsync(Array.Empty<JobHistoryEntry>());

            var handler = new ShowJobHistoryStatementHandler(mockStore.Object);
            var stmt = (ShowJobHistoryStatement)TestHelpers.Parse("SHOW JOB HISTORY;").Statements[0];

            var eval = NewEval();
            await handler.Execute(stmt, eval);

            Assert.NotNull(eval.LastResult);
            Assert.Empty(eval.LastResult!.Rows);
        }

        [Fact]
        public async Task ShowJobHistory_ColumnsAreCorrect()
        {
            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.GetHistoryAsync(null, It.IsAny<int>()))
                     .ReturnsAsync(Array.Empty<JobHistoryEntry>());

            var handler = new ShowJobHistoryStatementHandler(mockStore.Object);
            var stmt = (ShowJobHistoryStatement)TestHelpers.Parse("SHOW JOB HISTORY;").Statements[0];

            var eval = NewEval();
            await handler.Execute(stmt, eval);

            var expectedCols = new[] { "Id", "JobName", "StartTime", "EndTime", "Status", "RowsProcessed", "PeakRAM_MB", "CPUTime_s", "ErrorMessage" };
            Assert.Equal(expectedCols, eval.LastResult!.ColumnNames.ToArray());
        }

        [Fact]
        public void ShowJobHistory_JobNameParsedCorrectly()
        {
            // Verify the parser picks up the job name correctly
            var stmt = (ShowJobHistoryStatement)TestHelpers.Parse("SHOW JOB HISTORY 'MyJob';").Statements[0];
            Assert.Equal("MyJob", stmt.JobName);
        }

        [Fact]
        public void ShowJobHistory_WithoutJobName_ParsesAsNull()
        {
            var stmt = (ShowJobHistoryStatement)TestHelpers.Parse("SHOW JOB HISTORY;").Statements[0];
            Assert.Null(stmt.JobName);
        }
    }
}
