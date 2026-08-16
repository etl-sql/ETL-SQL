using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Orchestrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Statements.Statements
{
    /// <summary>
    /// CQ-T2: Coverage for inspection virtual tables and remaining SHOW JOB HISTORY handlers.
    /// </summary>
    public class ShowStatementTests
    {
        private static Evaluator NewEval(IJobHistoryStore? mockStore = null, ILineageCatalogStore? lineageStore = null)
        {
            var services = new ServiceCollection();

            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables();
            var configuration = builder.Build();

            services.AddSingleton<IConfiguration>(configuration);
            var loggerService = new LoggerService();
            services.AddSingleton<LoggerService>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILogger>(loggerService);
            services.AddSingleton<ETL_SQL.Common.ILoggerService>(loggerService);
            services.AddLogging();

            services.AddEtlSqlEngine(configuration);

            if (mockStore != null)
            {
                var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IJobHistoryStore));
                if (descriptor != null) services.Remove(descriptor);
                services.AddSingleton<IJobHistoryStore>(mockStore);
            }
            if (lineageStore != null)
            {
                var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ILineageCatalogStore));
                if (descriptor != null) services.Remove(descriptor);
                services.AddSingleton<ILineageCatalogStore>(lineageStore);
            }

            return services.BuildServiceProvider().GetRequiredService<Evaluator>();
        }

        // ── eng.tables ────────────────────────────────────────────────────────

        [Fact]
        public async Task EngTables_WithFlatFile_ListsTables()
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"show_tables_test_{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, "id,name\n1,Alice\n2,Bob");

            try
            {
                var script = $@"
CREATE CONNECTION mycsv AS FLATFILE('{csvPath}');
SELECT table_name, connection_name FROM eng.tables WHERE connection_name = 'mycsv';";
                var eval = NewEval();
                await eval.Evaluate(TestHelpers.Parse(script));

                Assert.NotNull(eval.LastResult);
                Assert.True(eval.LastResult!.Rows.Count >= 1,
                    "eng.tables for a flat file connection should return at least one entry");
                Assert.All(eval.LastResult!.Rows, row => Assert.Equal("mycsv", row["connection_name"]?.ToString()));
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task EngTables_IntoTable_CompletesWithoutError()
        {
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse("SELECT * INTO #result FROM eng.tables;"));
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
            var script = TestHelpers.Parse(sql);
            var diagnostic = Assert.Single(script.Diagnostics, d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
            Assert.Contains("eng.columns", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── eng.connections ───────────────────────────────────────────────────

        [Fact]
        public async Task EngConnections_AfterCreateConnection_ReturnsConnectionRow()
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"showconn_{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, "x,y\n1,2");

            try
            {
                var script = $@"
CREATE CONNECTION myconn AS FLATFILE('{csvPath}');
SELECT connection_name, connector_type FROM eng.connections WHERE connection_name = 'myconn';";
                var eval = NewEval();
                await eval.Evaluate(TestHelpers.Parse(script));

                Assert.NotNull(eval.LastResult);
                var connNames = eval.LastResult!.Rows.Select(r => r["connection_name"]?.ToString()).ToList();
                Assert.Contains("myconn", connNames);
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Fact]
        public async Task EngConnections_IntoTable_PopulatesDestination()
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"showconn2_{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, "x\n1");

            try
            {
                var script = $@"
CREATE CONNECTION c1 AS FLATFILE('{csvPath}');
SELECT * INTO #conn_list FROM eng.connections;
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

        [Fact]
        public async Task EngVariables_MasksSensitiveValues()
        {
            var script = @"
DECLARE @plain INT = 42;
DECLARE @secret SECRET = 'topsecret';
SELECT variable_name, value, is_sensitive FROM eng.variables WHERE variable_name IN ('@plain', '@secret') ORDER BY variable_name;";
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse(script));

            Assert.NotNull(eval.LastResult);
            var rows = eval.LastResult!.Rows.ToDictionary(r => r["variable_name"]?.ToString() ?? "");
            Assert.Equal("42", rows["@plain"]["value"]?.ToString());
            Assert.Equal("*******", rows["@secret"]["value"]);
            Assert.Equal(true, rows["@secret"]["is_sensitive"]);
        }

        [Fact]
        public async Task EngViews_ReturnsSessionViewDefinitions()
        {
            var script = @"
CREATE VIEW ActiveValues AS SELECT 1 AS Id;
SELECT view_name, query FROM eng.views WHERE view_name = 'ActiveValues';";
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse(script));

            Assert.NotNull(eval.LastResult);
            var row = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("ActiveValues", row["view_name"]);
            Assert.Contains("SELECT", row["query"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task EngVersion_ReturnsEngineVersion()
        {
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse("SELECT component, version FROM eng.version;"));

            Assert.NotNull(eval.LastResult);
            var row = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("ETL-SQL Engine", row["component"]);
            Assert.False(string.IsNullOrWhiteSpace(row["version"]?.ToString()));
        }

        [Fact]
        public async Task EngSafeZones_ReturnsConfiguredZones()
        {
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse("SELECT path, resolution FROM eng.safe_zones;"));

            Assert.NotNull(eval.LastResult);
            Assert.Contains("path", eval.LastResult!.ColumnNames);
            Assert.Contains("resolution", eval.LastResult!.ColumnNames);
        }

        [Fact]
        public async Task EngProfile_ReturnsCapturedMetrics()
        {
            var script = @"
SET PROFILE ON;
SELECT 1 AS Id;
SELECT statement, rows_processed, duration_ms FROM eng.profile;";
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse(script));

            Assert.NotNull(eval.LastResult);
            Assert.True(eval.LastResult!.Rows.Count >= 1);
            Assert.Contains("statement", eval.LastResult!.ColumnNames);
            Assert.Contains("rows_processed", eval.LastResult!.ColumnNames);
        }

        [Fact]
        public async Task EngConnectionConfig_ReturnsConnectionOptions()
        {
            var csvPath = Path.Combine(Path.GetTempPath(), $"conn_config_{Guid.NewGuid():N}.csv");
            await File.WriteAllTextAsync(csvPath, "x\n1");

            try
            {
                var script = $@"
CREATE CONNECTION cfg_conn AS FLATFILE('{csvPath}');
SELECT connection_name, option, value
FROM eng.connection_config
WHERE connection_name = 'cfg_conn';";
                var eval = NewEval();
                await eval.Evaluate(TestHelpers.Parse(script));

                Assert.NotNull(eval.LastResult);
                Assert.Contains("connection_name", eval.LastResult!.ColumnNames);
                Assert.Contains("option", eval.LastResult!.ColumnNames);
                Assert.Contains("value", eval.LastResult!.ColumnNames);
                Assert.All(eval.LastResult!.Rows, row => Assert.Equal("cfg_conn", row["connection_name"]?.ToString()));
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
            }
        }

        [Theory]
        [InlineData("eng.jobs", "name")]
        [InlineData("eng.job_history", "job_name")]
        [InlineData("eng.data_quality_status", "failed_rule_count")]
        [InlineData("eng.data_quality_failures", "failure_count")]
        [InlineData("eng.stewardship_score", "definition_version")]
        [InlineData("eng.stewardship_gaps", "requirement")]
        [InlineData("eng.job_state", "state_key")]
        [InlineData("eng.host_metrics", "node_id")]
        [InlineData("eng.bundles", "bundle_name")]
        [InlineData("eng.bundle_files", "virtual_path")]
        [InlineData("eng.bundle_dependencies", "from_path")]
        public async Task EngOrchestratorCatalogTables_AreQueryable(string tableName, string expectedColumn)
        {
            var eval = NewEval();
            await eval.Evaluate(TestHelpers.Parse($"SELECT * FROM {tableName};"));

            Assert.NotNull(eval.LastResult);
            Assert.Contains(expectedColumn, eval.LastResult!.ColumnNames);
        }

        // ── eng.job_history ─────────────────────────────────────────────────────

        [Fact]
        public async Task EngJobHistory_WithMockStore_ReturnsHistoryRows()
        {
            var entries = new List<JobHistoryEntry>
            {
                new(1L, "Job1", new DateTime(2026, 1, 1), new DateTime(2026, 1, 1, 0, 1, 0), "SUCCESS", null, 100),
                new(2L, "Job1", new DateTime(2026, 1, 2), null, "RUNNING", null, 0),
            };

            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.GetHistoryForNameAsync(null, "Job1", It.IsAny<int>())).ReturnsAsync(entries);

            var eval = NewEval(mockStore.Object);
            await eval.Evaluate(TestHelpers.Parse("SELECT * FROM eng.job_history('Job1');"));

            Assert.NotNull(eval.LastResult);
            Assert.Equal(2, eval.LastResult!.Rows.Count);
            Assert.Equal("SUCCESS", eval.LastResult.Rows[0]["status"]?.ToString());
            Assert.Equal("RUNNING", eval.LastResult.Rows[1]["status"]?.ToString());
        }

        [Fact]
        public async Task EngJobHistory_EmptyStore_ReturnsNoRows()
        {
            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.GetHistoryAsync(It.IsAny<JobId>(), It.IsAny<int>()))
                     .ReturnsAsync(Array.Empty<JobHistoryEntry>());

            var eval = NewEval(mockStore.Object);
            await eval.Evaluate(TestHelpers.Parse("SELECT * FROM eng.job_history;"));

            Assert.NotNull(eval.LastResult);
            Assert.Empty(eval.LastResult!.Rows);
        }

        [Fact]
        public async Task EngJobHistory_ColumnsAreCorrect()
        {
            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.GetHistoryAsync(It.IsAny<JobId>(), It.IsAny<int>()))
                     .ReturnsAsync(Array.Empty<JobHistoryEntry>());

            var eval = NewEval(mockStore.Object);
            await eval.Evaluate(TestHelpers.Parse("SELECT * FROM eng.job_history;"));

            var expectedCols = new[] { "id", "job_name", "start_time", "end_time", "status", "rows_processed", "rows_warned", "rows_quarantined", "failed_rule_counts", "peak_ram_mb", "cpu_time_s", "error_message" };
            Assert.Equal(expectedCols, eval.LastResult!.ColumnNames.ToArray());
        }

        [Fact]
        public async Task EngDataQualityStatus_UsesCanonicalPersistedRunIdentityAndStatus()
        {
            var mockStore = new Mock<IJobHistoryStore>();
            mockStore.Setup(s => s.GetDataQualityStatusesAsync(It.IsAny<int>())).ReturnsAsync(
            [
                new JobDataQualityStatus("42", "customers", new DateTime(2026, 1, 1),
                    new DateTime(2026, 1, 1, 0, 1, 0), "FAILED", 100, 5, 2, 2, null,
                    "NOT_TRACKED", "sanitized")
            ]);
            var eval = NewEval(mockStore.Object);

            await eval.Evaluate(TestHelpers.Parse("SELECT * FROM eng.data_quality_status;"));

            var persisted = Assert.Single(eval.LastResult!.Rows, r => r["source"]?.ToString() == "ORCHESTRATOR");
            Assert.Equal("42", persisted["run_id"]?.ToString());
            Assert.Equal("FAILED", persisted["status"]?.ToString());
            Assert.Equal(5d, Convert.ToDouble(persisted["warn_percent"]));
        }

        [Fact]
        public async Task StewardshipScoreAndGaps_ReconcileFromTheSameCurrentLineage()
        {
            var lineageStore = new Mock<ILineageCatalogStore>();
            lineageStore.Setup(s => s.GetRecentLineageAsync(It.IsAny<int>()))
                .ReturnsAsync(Array.Empty<LineageHistoryEntry>());
            var eval = NewEval(lineageStore: lineageStore.Object);
            eval.CurrentScriptPath = Path.Combine(Path.GetTempPath(), "pipelines", "customers.etlsql");
            await eval.Evaluate(TestHelpers.Parse(@"
                CREATE TABLE #src (Email VARCHAR(100));
                SELECT Email /* @pii: true */ INTO #customers FROM #src;
                SELECT * FROM eng.stewardship_score;"));
            var scores = eval.LastResult!.Rows.Where(r => r["scope_type"]?.ToString() == "GLOBAL").ToList();

            await eval.Evaluate(TestHelpers.Parse("SELECT * FROM eng.stewardship_gaps;"));
            var gaps = eval.LastResult!.Rows.Where(r => r["scope_type"]?.ToString() == "GLOBAL").ToList();

            Assert.All(scores, score => Assert.Equal(
                Convert.ToInt32(score["denominator"]) - Convert.ToInt32(score["numerator"]),
                gaps.Count(g => g["component"]?.ToString() == score["component"]?.ToString())));
            Assert.All(gaps, gap =>
            {
                Assert.NotNull(gap["source_file"]);
                Assert.True(Convert.ToInt32(gap["line"]) > 0);
            });
        }

        [Fact]
        public void EngJobHistory_ParameterizedQuery_ParsesCorrectly()
        {
            var script = TestHelpers.Parse("SELECT * FROM eng.job_history('MyJob');");
            Assert.Single(script.Statements);
            Assert.Empty(script.Diagnostics);
        }

        [Fact]
        public void EngJobHistory_NonParameterizedQuery_ParsesCorrectly()
        {
            var script = TestHelpers.Parse("SELECT * FROM eng.job_history;");
            Assert.Single(script.Statements);
            Assert.Empty(script.Diagnostics);
        }
    }
}
