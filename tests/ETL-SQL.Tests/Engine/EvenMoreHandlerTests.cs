using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Handler effects for eng.jobs, eng.tables, eng.connection_config, PARALLEL FOR, CREATE JOB,
    /// the session SET options, report-object create/alter/drop, SET @variable, ALTER CONNECTION,
    /// KILL JOB, CLEAR SESSION, CREATE DATASET, THROW and TRUNCATE. Each test asserts what the
    /// statement changed, not only that it ran.
    /// </summary>
    public sealed class EvenMoreHandlerTests : IDisposable
    {
        // Jobs persist to the Orchestrator store; a private database keeps eng.jobs to this test.
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"evenmore-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }

        private Evaluator Eval()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider(
                new Dictionary<string, string?> { ["Orchestrator:DatabasePath"] = _dbPath })
                .GetRequiredService<Evaluator>();
            eval.RedirectOutput = true;
            return eval;
        }

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private async Task<Evaluator> RunAndGet(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        private Task Run(string sql) => RunAndGet(sql);

        private static string Output(Evaluator eval) => string.Join("\n", eval.Messages.Select(m => m.Message));

        private static List<decimal> Column(Evaluator eval, string column) =>
            eval.LastResult!.Rows.Select(r => Convert.ToDecimal(r[column])).OrderBy(v => v).ToList();

        private static string TempCsv()
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(path, "Id\n1");
            return path;
        }

        // ── eng.jobs / eng.tables ─────────────────────────────────────────────

        [Fact]
        public async Task EngJobs_NoJobsRegistered_ReturnsNoRowsWithThePublishedColumns()
        {
            var eval = await RunAndGet("SELECT * FROM eng.jobs;");
            Assert.Empty(eval.LastResult!.Rows);
            Assert.Contains("schedule", eval.LastResult.ColumnNames, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task EngJobs_IntoTempTable_CopiesTheJobRows()
        {
            var eval = await RunAndGet(
                "CREATE JOB CopiedJob FOR SCRIPT 'jobs/copied.etlsql';" +
                "SELECT * INTO #jobs FROM eng.jobs; SELECT name FROM #jobs;");
            Assert.Equal("CopiedJob", Assert.Single(eval.LastResult!.Rows)["name"]?.ToString());
        }

        [Fact]
        public async Task EngTables_ListsEachConnectionsTables()
        {
            var csv = TempCsv();
            try
            {
                // A flat-file connection exposes its file as the single table FILE.
                var eval = await RunAndGet(
                    $"CREATE CONNECTION listed AS FLATFILE('{csv.Replace("\\", "\\\\")}');" +
                    "SELECT * INTO #ds FROM eng.tables; SELECT * FROM #ds WHERE connection_name = 'listed';");
                Assert.Equal("FILE", Assert.Single(eval.LastResult!.Rows)["table_name"]?.ToString());
            }
            finally { File.Delete(csv); }
        }

        // ── eng.connection_config ─────────────────────────────────────────────

        [Fact]
        public async Task ConnectionConfig_AfterFlatFileConnection_ListsItsOptions()
        {
            var csv = TempCsv();
            try
            {
                var eval = await RunAndGet(
                    $"CREATE CONNECTION fc AS FLATFILE(PATH='{csv.Replace("\\", "\\\\")}', DELIMITER='PIPE');" +
                    "SELECT * INTO #cfg FROM eng.connection_config WHERE connection_name = 'fc';" +
                    "SELECT * FROM #cfg;");
                Assert.NotEmpty(eval.LastResult!.Rows);
                Assert.All(eval.LastResult.Rows, r => Assert.Equal("fc", r["connection_name"]?.ToString(), ignoreCase: true));
            }
            finally { File.Delete(csv); }
        }

        [Fact]
        public async Task ConnectionConfig_MissingConnection_ReturnsNoRows()
        {
            var eval = await RunAndGet("SELECT * FROM eng.connection_config WHERE connection_name = 'nonexistent_conn_xyz';");
            Assert.Empty(eval.LastResult!.Rows);
        }

        // ── PARALLEL FOR ──────────────────────────────────────────────────────

        [Theory]
        [InlineData("PARALLEL FOR @i = 1 TO 3", new[] { 1, 2, 3 })]
        [InlineData("PARALLEL FOR @i = 0 TO 6 STEP 2", new[] { 0, 2, 4, 6 })]
        [InlineData("PARALLEL FOR @i TO 3", new[] { 1, 2, 3 })]
        [InlineData("PARALLEL (2) FOR @i = 1 TO 4", new[] { 1, 2, 3, 4 })]
        public async Task ParallelFor_RunsTheBodyOncePerValue(string loop, int[] expected)
        {
            var eval = await RunAndGet(
                "CREATE TABLE #seen (i INT);" +
                $"{loop} BEGIN INSERT INTO #seen VALUES (@i); END;" +
                "SELECT i FROM #seen;");
            Assert.Equal(expected.Select(v => (decimal)v), Column(eval, "i"));
        }

        // ── CREATE JOB ────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateJob_WithoutASchedule_IsListedAsManual()
        {
            var eval = await RunAndGet(
                "CREATE JOB TestJob FOR SCRIPT 'jobs/test.etlsql';" +
                "SELECT name, schedule, next_run FROM eng.jobs;");
            var row = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("TestJob", row["name"]?.ToString());
            Assert.Equal("MANUAL", row["schedule"]?.ToString());
            Assert.True(row["next_run"] is null or DBNull, $"next_run was {row["next_run"]}");
        }

        // ── Session SET options ───────────────────────────────────────────────

        [Theory]
        [InlineData("ON", true)]
        [InlineData("OFF", false)]
        public async Task SetPersist_SetsThePersistentSessionFlag(string value, bool expected)
        {
            var eval = await RunAndGet($"SET PERSIST {value};");
            Assert.Equal(expected, eval.IsPersistentSession);
        }

        [Fact]
        public async Task SetTemplatePath_SetsTheReportTemplatePath()
        {
            var dir = Path.GetTempPath().TrimEnd('\\', '/');
            var eval = await RunAndGet($"SET TEMPLATE_PATH = '{dir.Replace("\\", "\\\\")}';");
            Assert.Equal(dir, eval.ReportContext.TemplatePath.TrimEnd('\\', '/'));
        }

        [Theory]
        [InlineData("Warn")]
        [InlineData("Block")]
        public async Task SetScriptHashPolicy_SetsThePolicy(string policy)
        {
            var eval = await RunAndGet($"SET SCRIPT_HASH_POLICY = '{policy}';");
            Assert.Equal(policy, eval.ScriptHashPolicy, ignoreCase: true);
        }

        [Fact]
        public async Task SetScriptHashPolicy_Invalid_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("SET SCRIPT_HASH_POLICY = 'BadValue';"));
        }

        [Theory]
        [InlineData("SET SHOW_PASSWORD ON;", true)]
        [InlineData("SET SHOW_PASSWORD ON; SET SHOW_PASSWORD OFF;", false)]
        [InlineData("SET SHOW_SECRETS ON;", true)]
        [InlineData("SET SHOW_SECRETS ON; SET SHOW_SECRETS OFF;", false)]
        public async Task SetShowPassword_AndItsShowSecretsAlias_SetTheFlag(string sql, bool expected)
        {
            var eval = await RunAndGet(sql);
            Assert.Equal(expected, eval.ShowPassword);
        }

        // ── CREATE THEME / TEMPLATE ───────────────────────────────────────────

        private static string? Option(IDictionary<string, string> options, string key) =>
            options.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;

        [Fact]
        public async Task CreateTheme_RegistersTheTheme()
        {
            var eval = await RunAndGet("CREATE THEME darkmode AS (BACKGROUND = '#000000', TEXT_COLOR = '#ffffff');");
            Assert.Contains("#000000", Option(eval.ReportContext.ThemeDefinitions["darkmode"].Properties, "BACKGROUND"));
        }

        [Fact]
        public async Task CreateOrAlterTheme_ReplacesTheProperties()
        {
            var eval = await RunAndGet(
                "CREATE THEME t1 AS (BACKGROUND = '#fff');" +
                "CREATE OR ALTER THEME t1 AS (BACKGROUND = '#000');");
            Assert.Contains("#000", Option(eval.ReportContext.ThemeDefinitions["t1"].Properties, "BACKGROUND"));
        }

        [Fact]
        public async Task CreateTheme_Duplicate_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run(
                "CREATE THEME duptheme AS (BACKGROUND = '#fff');" +
                "CREATE THEME duptheme AS (BACKGROUND = '#000');"));
        }

        [Fact]
        public async Task CreateTemplate_RegistersTheTemplate()
        {
            var eval = await RunAndGet("CREATE TEMPLATE mytemplate AS (TYPE = 'table');");
            Assert.Contains("table", Option(eval.ReportContext.TemplateDefinitions["mytemplate"].Options, "TYPE"));
        }

        [Fact]
        public async Task CreateOrAlterTemplate_ReplacesTheOptions()
        {
            var eval = await RunAndGet(
                "CREATE TEMPLATE t1 AS (TYPE = 'table');" +
                "CREATE OR ALTER TEMPLATE t1 AS (TYPE = 'bar');");
            Assert.Contains("bar", Option(eval.ReportContext.TemplateDefinitions["t1"].Options, "TYPE"));
        }

        // ── CREATE OR REPLACE report objects ──────────────────────────────────

        [Fact]
        public async Task CreateOrReplaceVisual_ReplacesTheDefinition()
        {
            var eval = await RunAndGet(
                "CREATE VISUAL V AS TABLE (SOURCE = (SELECT 1 AS id));" +
                "CREATE OR REPLACE VISUAL V AS CARD (SOURCE = (SELECT 2 AS id), MAPPINGS (VALUE = id));");
            Assert.Equal("Card", eval.ReportContext.VisualDefinitions["V"].VisualType.ToString(), ignoreCase: true);
        }

        [Fact]
        public async Task CreateOrReplaceStyle_ReplacesTheStyles()
        {
            var eval = await RunAndGet(
                "CREATE STYLE S AS (COLOR = 'red');" +
                "CREATE OR REPLACE STYLE S AS (COLOR = 'blue');");
            Assert.Contains("blue", Option(eval.ReportContext.StyleDefinitions["S"].Styles, "COLOR"));
        }

        [Fact]
        public async Task CreateOrReplaceButton_ReplacesTheTitle()
        {
            var eval = await RunAndGet(
                "CREATE BUTTON B AS (TITLE = 'Run', ACTIONS (ON_CLICK = APPLY_PARAMETERS));" +
                "CREATE OR REPLACE BUTTON B AS (TITLE = 'Reset', ACTIONS (ON_CLICK = APPLY_PARAMETERS));");
            Assert.Contains("Reset", eval.ReportContext.ButtonDefinitions["B"].Title!.ToSql());
        }

        [Theory]
        [InlineData("CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = V));CREATE OR REPLACE PAGE P AS DASHBOARD (STRUCTURE = 'B', MAP ('B' = V));")]
        [InlineData("CREATE CONTAINER P AS BOX (LAYOUT (STRUCTURE = 'A', MAP ('A' = V)));CREATE OR REPLACE CONTAINER P AS BOX (LAYOUT (STRUCTURE = 'B', MAP ('B' = V)));")]
        public async Task CreateOrReplaceLayout_ReplacesTheSlotMap(string sql)
        {
            var eval = await RunAndGet("CREATE VISUAL V AS TABLE (SOURCE = (SELECT 1 AS id));" + sql);
            var slots = eval.ReportContext.PageDefinitions.TryGetValue("P", out var page)
                ? page.SlotMap
                : eval.ReportContext.ContainerDefinitions["P"].SlotMap;
            Assert.Equal("B", Assert.Single(slots).Key);
        }

        [Fact]
        public async Task CreateOrReplaceNavigation_KeepsOneDefinition()
        {
            var eval = await RunAndGet(
                "CREATE VISUAL V AS TABLE (SOURCE = (SELECT 1 AS id));" +
                "CREATE PAGE P AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = V));" +
                "CREATE PAGE Q AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = V));" +
                "CREATE NAVIGATION N AS TAB (PAGES (P));" +
                "CREATE OR REPLACE NAVIGATION N AS TAB (PAGES (Q));");
            Assert.Equal("Q", Assert.Single(eval.ReportContext.NavigationDefinitions["N"].Pages));
        }

        [Fact]
        public async Task CreateOrReplaceDataset_ReplacesTheRows()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &D AS (SELECT 1 AS id);" +
                "CREATE OR REPLACE DATASET &D AS (SELECT 2 AS id);" +
                "SELECT id FROM &D;");
            Assert.Equal([2m], Column(eval, "id"));
        }

        // ── DROP report objects ───────────────────────────────────────────────

        [Theory]
        [InlineData("VISUAL")]
        [InlineData("PAGE")]
        [InlineData("STYLE")]
        [InlineData("CONTAINER")]
        [InlineData("NAVIGATION")]
        [InlineData("TEMPLATE")]
        [InlineData("THEME")]
        public async Task DropIfExists_OnAMissingObject_DoesNotThrow(string kind)
        {
            var eval = await RunAndGet($"DROP {kind} IF EXISTS nonexistent_object;");
            Assert.Null(eval.LastResult);
        }

        [Fact]
        public async Task DropVisual_WithoutIfExists_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("DROP VISUAL nonexistent_vis_xyz;"));
        }

        [Fact]
        public async Task DropVisual_RemovesTheDefinition()
        {
            var eval = await RunAndGet(
                "CREATE VISUAL myvis AS BAR (SOURCE (SELECT 1 AS n));" +
                "DROP VISUAL myvis;");
            Assert.False(eval.ReportContext.VisualDefinitions.ContainsKey("myvis"));
        }

        [Fact]
        public async Task DropTheme_RemovesTheDefinition()
        {
            var eval = await RunAndGet(
                "CREATE THEME dropme AS (BACKGROUND = '#000');" +
                "DROP THEME dropme;");
            Assert.False(eval.ReportContext.ThemeDefinitions.ContainsKey("dropme"));
        }

        // ── ALTER report objects ──────────────────────────────────────────────

        [Fact]
        public async Task AlterVisual_UpdatesTheTitle()
        {
            var eval = await RunAndGet(
                "CREATE VISUAL visalter AS BAR (SOURCE (SELECT 1 AS n));" +
                "ALTER VISUAL visalter (TITLE = 'Updated Title');");
            Assert.Contains("Updated Title", eval.ReportContext.VisualDefinitions["visalter"].Title!.ToSql());
        }

        [Fact]
        public async Task AlterPage_UpdatesTheTitle()
        {
            var eval = await RunAndGet(
                "CREATE VISUAL v1 AS BAR (SOURCE (SELECT 1 AS n));" +
                "CREATE PAGE pg1 AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = v1));" +
                "ALTER PAGE pg1 (TITLE = 'New Title');");
            Assert.Contains("New Title", eval.ReportContext.PageDefinitions["pg1"].Title!.ToSql());
        }

        // ── SET @variable ─────────────────────────────────────────────────────

        [Fact]
        public async Task SetVariable_UpdatesDecimalValue()
        {
            var eval = await RunAndGet("DECLARE @x DECIMAL = 5; SET @x = 10;");
            Assert.Equal(10m, Convert.ToDecimal(eval.GetVariable("@x")));
        }

        [Fact]
        public async Task SetVariable_UpdatesStringValue()
        {
            var eval = await RunAndGet("DECLARE @s VARCHAR = 'hello'; SET @s = 'world';");
            Assert.Equal("world", eval.GetVariable("@s")?.ToString());
        }

        [Fact]
        public async Task SetVariable_UndeclaredVariable_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("SET @notdeclared = 42;"));
        }

        // ── ALTER CONNECTION ──────────────────────────────────────────────────

        [Fact]
        public async Task AlterConnection_RecordsTheNewOption()
        {
            var csv = TempCsv();
            try
            {
                var eval = await RunAndGet(
                    $"CREATE CONNECTION ac AS FLATFILE('{csv.Replace("\\", "\\\\")}');" +
                    "ALTER CONNECTION ac WITH (ENCODING = 'UTF-16');" +
                    "SELECT * FROM eng.connection_config WHERE connection_name = 'ac';");
                Assert.Contains(eval.LastResult!.Rows, r =>
                    string.Equals(r["option"]?.ToString(), "ENCODING", StringComparison.OrdinalIgnoreCase) &&
                    r["value"]?.ToString()?.Contains("UTF-16", StringComparison.OrdinalIgnoreCase) == true);
            }
            finally { File.Delete(csv); }
        }

        [Fact]
        public async Task AlterConnection_Nonexistent_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("ALTER CONNECTION does_not_exist WITH (ENCODING = 'UTF-8');"));
        }

        // ── KILL JOB ──────────────────────────────────────────────────────────

        [Fact]
        public async Task KillJob_NoJobManager_ReportsThatNothingWasKilled()
        {
            // No IJobManager is registered in the default test DI; the handler says so and returns.
            var eval = await RunAndGet("KILL JOB 999;");
            Assert.NotEmpty(Output(eval));
        }

        [Fact]
        public async Task KillJob_InvalidId_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("KILL JOB 'notanumber';"));
        }

        // ── CREATE DATASET ────────────────────────────────────────────────────

        [Theory]
        [InlineData("CREATE DATASET &myds AS (SELECT 1 AS n);")]
        [InlineData("CREATE DATASET &myds TTL = '1h' AS (SELECT 1 AS n);")]
        public async Task CreateDataset_MaterializesTheQuery(string create)
        {
            var eval = await RunAndGet(create + "SELECT n FROM &myds;");
            Assert.Equal([1m], Column(eval, "n"));
            Assert.True(eval.ReportContext.DatasetDefinitions.ContainsKey("&myds") || eval.ReportContext.DatasetDefinitions.ContainsKey("myds"));
        }

        [Fact]
        public async Task CreateOrAlterDataset_ReplacesTheRows()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &altds AS (SELECT 1 AS n);" +
                "CREATE OR ALTER DATASET &altds AS (SELECT 2 AS n);" +
                "SELECT n FROM &altds;");
            Assert.Equal([2m], Column(eval, "n"));
        }

        [Fact]
        public async Task CreateDataset_RecordsReportObjectLineage()
        {
            var eval = await RunAndGet(
                "SELECT 1 AS amount INTO #orders;" +
                "CREATE DATASET &daily_sales AS (SELECT amount FROM #orders);");

            var entries = eval.LineageTracker.GetFullLineage().ToList();
            Assert.Contains(entries, e =>
                e.Operation == "CREATE DATASET" &&
                e.TargetTable == "dataset:&daily_sales" &&
                e.SourceTables.Contains("#orders", StringComparer.OrdinalIgnoreCase));

            // Column-level lineage is keyed to the dataset target (not "RESULTSET")
            // so it persists and resolves by dataset name across scripts.
            Assert.DoesNotContain(entries, e => e.TargetTable == "RESULTSET");
            Assert.Contains(entries, e =>
                e.TargetTable == "dataset:&daily_sales" &&
                e.TargetColumn == "amount" &&
                e.SourceColumns.Contains("amount", StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task CreateVisual_RecordsReportObjectLineage()
        {
            var eval = await RunAndGet(
                "SELECT 1 AS amount INTO #sales;" +
                "CREATE VISUAL SalesChart AS BAR (SOURCE = #sales, MAPPINGS (x = amount, y = amount));");

            var entries = eval.LineageTracker.GetFullLineage().ToList();
            Assert.Contains(entries, e =>
                e.Operation == "CREATE VISUAL" &&
                e.TargetTable == "report:SalesChart" &&
                e.SourceTables.Contains("#sales", StringComparer.OrdinalIgnoreCase));
        }

        // ── THROW / TRUNCATE ──────────────────────────────────────────────────

        [Fact]
        public async Task Throw_InsideTryCatch_ExposesTheErrorToTheCatchBlock()
        {
            var eval = await RunAndGet(
                "BEGIN TRY THROW 50001, 'test error', 1; END TRY " +
                "BEGIN CATCH SELECT @@ERROR AS err, ERROR_MESSAGE() AS msg; END CATCH");
            var row = Assert.Single(eval.LastResult!.Rows);
            Assert.Equal(50001m, Convert.ToDecimal(row["err"]));
            Assert.Equal("test error", row["msg"]?.ToString());
        }

        [Fact]
        public async Task TruncateTable_AfterInsert_ClearsRows()
        {
            var eval = await RunAndGet(
                "CREATE TABLE #t (Id INT);" +
                "INSERT INTO #t VALUES (1), (2);" +
                "TRUNCATE TABLE #t;" +
                "SELECT COUNT(*) AS n FROM #t;");
            Assert.Equal(0m, Convert.ToDecimal(eval.LastResult?.Rows[0]["n"]));
        }
    }
}
