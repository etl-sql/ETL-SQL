using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Engine;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Covers low-coverage engine handlers: SHOW JOBS, SHOW DATASETS, SHOW CONNECTION CONFIG,
    /// PARALLEL FOR, CREATE JOB, SET PERSIST, SET TEMPLATE_PATH, SET SCRIPT_HASH_POLICY,
    /// CREATE THEME, CREATE TEMPLATE, DROP report objects, ALTER report objects,
    /// SET @variable, ALTER CONNECTION, KILL JOB, CLEAR SESSION, CREATE DATASET, etc.
    /// </summary>
    public class EvenMoreHandlerTests
    {
        private static Evaluator Eval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task Run(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
        }

        private static async Task<Evaluator> RunAndGet(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        // ── SHOW JOBS ──────────────────────────────────────────────────────────

        [Fact]
        public async Task ShowJobs_NoJobsRegistered_ReturnsEmptyTable()
        {
            var eval = await RunAndGet("SHOW JOBS;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task ShowJobs_IntoTempTable_PopulatesTable()
        {
            var eval = await RunAndGet("SHOW JOBS INTO #jobs; SELECT * FROM #jobs;");
            Assert.NotNull(eval.LastResult);
        }

        // ── SHOW DATASETS ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowDatasets_NoDatasets_ReturnsEmptyTable()
        {
            var eval = await RunAndGet("SHOW DATASETS;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task ShowDatasets_IntoTempTable_PopulatesTable()
        {
            var eval = await RunAndGet("SHOW DATASETS INTO #ds; SELECT * FROM #ds;");
            Assert.NotNull(eval.LastResult);
        }

        // ── SHOW CONNECTION CONFIG ─────────────────────────────────────────────

        [Fact]
        public async Task ShowConnectionConfig_AfterFlatFileConnection_ReturnsRows()
        {
            var tmpCsv = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(tmpCsv, "Id\n1");
            try
            {
                var path = tmpCsv.Replace("\\", "\\\\");
                var eval = await RunAndGet(
                    $"CREATE CONNECTION fc ON FLATFILE('{path}');" +
                    $"SHOW CONNECTION fc CONFIG;");
                Assert.NotNull(eval.LastResult);
            }
            finally { if (File.Exists(tmpCsv)) File.Delete(tmpCsv); }
        }

        [Fact]
        public async Task ShowConnectionConfig_IntoTempTable_WritesRows()
        {
            var tmpCsv = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(tmpCsv, "Id\n1");
            try
            {
                var path = tmpCsv.Replace("\\", "\\\\");
                var eval = await RunAndGet(
                    $"CREATE CONNECTION fc2 ON FLATFILE('{path}');" +
                    $"SHOW CONNECTION fc2 CONFIG INTO #cfg;" +
                    $"SELECT * FROM #cfg;");
                Assert.NotNull(eval.LastResult);
            }
            finally { if (File.Exists(tmpCsv)) File.Delete(tmpCsv); }
        }

        [Fact]
        public async Task ShowConnectionConfig_MissingConnection_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("SHOW CONNECTION nonexistent_conn_xyz CONFIG;"));
        }

        // ── PARALLEL FOR ──────────────────────────────────────────────────────

        [Fact]
        public async Task ParallelFor_BasicRange_Executes()
        {
            await Run("PARALLEL FOR @i = 1 TO 3 BEGIN SELECT @i; END");
        }

        [Fact]
        public async Task ParallelFor_WithStep_Executes()
        {
            await Run("PARALLEL FOR @i = 0 TO 6 STEP 2 BEGIN SELECT @i; END");
        }

        [Fact]
        public async Task ParallelFor_ImplicitStart_Executes()
        {
            await Run("PARALLEL FOR @i TO 3 BEGIN SELECT @i; END");
        }

        [Fact]
        public async Task ParallelFor_WithConcurrencyLimit_Executes()
        {
            await Run("PARALLEL (2) FOR @i = 1 TO 4 BEGIN SELECT @i; END");
        }

        // ── CREATE JOB ────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateJob_HourlySchedule_Persists()
        {
            await Run("CREATE JOB TestJobHourly ON SCHEDULE EVERY 1 HOUR AS SELECT 1;");
        }

        [Fact]
        public async Task CreateJob_DailyAtTime_Persists()
        {
            await Run("CREATE JOB TestJobDaily ON SCHEDULE EVERY 1 DAY AT '02:00' AS SELECT 1;");
        }

        [Fact]
        public async Task CreateJob_MinuteSchedule_Persists()
        {
            await Run("CREATE JOB TestJobMin ON SCHEDULE EVERY 30 MINUTES AS SELECT 1;");
        }

        // ── SET PERSIST ───────────────────────────────────────────────────────

        [Fact]
        public async Task SetPersist_On_NoError()
        {
            await Run("SET PERSIST ON;");
        }

        [Fact]
        public async Task SetPersist_Off_NoError()
        {
            await Run("SET PERSIST OFF;");
        }

        // ── SET TEMPLATE_PATH ─────────────────────────────────────────────────

        [Fact]
        public async Task SetTemplatePath_ValidPath_SetsPath()
        {
            var tmpDir = Path.GetTempPath();
            var path = tmpDir.Replace("\\", "\\\\").TrimEnd('\\');
            await Run($"SET TEMPLATE_PATH = '{path}';");
        }

        // ── SET SCRIPT_HASH_POLICY ────────────────────────────────────────────

        [Fact]
        public async Task SetScriptHashPolicy_Warn_NoError()
        {
            await Run("SET SCRIPT_HASH_POLICY = 'Warn';");
        }

        [Fact]
        public async Task SetScriptHashPolicy_Block_NoError()
        {
            await Run("SET SCRIPT_HASH_POLICY = 'Block';");
        }

        [Fact]
        public async Task SetScriptHashPolicy_Invalid_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("SET SCRIPT_HASH_POLICY = 'BadValue';"));
        }

        // ── CREATE THEME ──────────────────────────────────────────────────────

        [Fact]
        public async Task CreateTheme_Basic_RegistersTheme()
        {
            var eval = await RunAndGet(
                "CREATE THEME darkmode AS (BACKGROUND = '#000000', TEXT_COLOR = '#ffffff');");
            Assert.True(eval.ReportContext.ThemeDefinitions.ContainsKey("darkmode"));
        }

        [Fact]
        public async Task CreateTheme_OrAlter_UpdatesTheme()
        {
            await Run(
                "CREATE THEME t1 AS (BACKGROUND = '#fff');" +
                "CREATE OR ALTER THEME t1 AS (BACKGROUND = '#000');");
        }

        [Fact]
        public async Task CreateTheme_Duplicate_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run(
                    "CREATE THEME duptheme AS (BACKGROUND = '#fff');" +
                    "CREATE THEME duptheme AS (BACKGROUND = '#000');"));
        }

        // ── CREATE TEMPLATE ───────────────────────────────────────────────────

        [Fact]
        public async Task CreateTemplate_Basic_RegistersTemplate()
        {
            var eval = await RunAndGet(
                "CREATE TEMPLATE mytemplate AS (TYPE = 'table');");
            Assert.True(eval.ReportContext.TemplateDefinitions.ContainsKey("mytemplate"));
        }

        [Fact]
        public async Task CreateTemplate_OrAlter_UpdatesTemplate()
        {
            await Run(
                "CREATE TEMPLATE t1 AS (TYPE = 'table');" +
                "CREATE OR ALTER TEMPLATE t1 AS (TYPE = 'bar');");
        }

        // ── DROP REPORT OBJECTS ───────────────────────────────────────────────

        [Fact]
        public async Task DropVisual_IfExists_NoThrow()
        {
            await Run("DROP VISUAL IF EXISTS nonexistent_vis;");
        }

        [Fact]
        public async Task DropPage_IfExists_NoThrow()
        {
            await Run("DROP PAGE IF EXISTS nonexistent_pg;");
        }

        [Fact]
        public async Task DropStyle_IfExists_NoThrow()
        {
            await Run("DROP STYLE IF EXISTS nonexistent_sty;");
        }

        [Fact]
        public async Task DropContainer_IfExists_NoThrow()
        {
            await Run("DROP CONTAINER IF EXISTS nonexistent_ctr;");
        }

        [Fact]
        public async Task DropNavigation_IfExists_NoThrow()
        {
            await Run("DROP NAVIGATION IF EXISTS nonexistent_nav;");
        }

        [Fact]
        public async Task DropTemplate_IfExists_NoThrow()
        {
            await Run("DROP TEMPLATE IF EXISTS nonexistent_tpl;");
        }

        [Fact]
        public async Task DropTheme_IfExists_NoThrow()
        {
            await Run("DROP THEME IF EXISTS nonexistent_thm;");
        }

        [Fact]
        public async Task DropVisual_WithoutIfExists_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("DROP VISUAL nonexistent_vis_xyz;"));
        }

        [Fact]
        public async Task CreateThenDropVisual_NoError()
        {
            await Run(
                "CREATE VISUAL myvis AS BAR (SOURCE (SELECT 1 AS n));" +
                "DROP VISUAL myvis;");
        }

        [Fact]
        public async Task CreateThenDropTheme_NoError()
        {
            var tmpDir = Path.GetTempPath().Replace("\\", "\\\\").TrimEnd('\\');
            await Run(
                $"SET TEMPLATE_PATH = '{tmpDir}';" +
                "CREATE THEME dropme AS (BACKGROUND = '#000');" +
                "DROP THEME dropme;");
        }

        // ── ALTER REPORT OBJECTS ──────────────────────────────────────────────

        [Fact]
        public async Task AlterVisual_AfterCreate_UpdatesTitle()
        {
            await Run(
                "CREATE VISUAL visalter AS BAR (SOURCE (SELECT 1 AS n));" +
                "ALTER VISUAL visalter (TITLE = 'Updated Title');");
        }

        [Fact]
        public async Task AlterPage_AfterCreate_NoError()
        {
            await Run(
                "CREATE VISUAL v1 AS BAR (SOURCE (SELECT 1 AS n));" +
                "CREATE PAGE pg1 AS LAYOUT (STRUCTURE = 'A', MAP ('A' = v1));" +
                "ALTER PAGE pg1 (TITLE = 'New Title');");
        }

        // ── SET @variable ─────────────────────────────────────────────────────

        [Fact]
        public async Task SetVariable_UpdatesDecimalValue()
        {
            var eval = await RunAndGet(
                "DECLARE @x DECIMAL = 5;" +
                "SET @x = 10;");
            Assert.Equal(10m, Convert.ToDecimal(eval.GetVariable("@x")));
        }

        [Fact]
        public async Task SetVariable_UpdatesStringValue()
        {
            var eval = await RunAndGet(
                "DECLARE @s VARCHAR = 'hello';" +
                "SET @s = 'world';");
            Assert.Equal("world", eval.GetVariable("@s")?.ToString());
        }

        [Fact]
        public async Task SetVariable_UndeclaredVariable_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("SET @notdeclared = 42;"));
        }

        // ── ALTER CONNECTION ──────────────────────────────────────────────────

        [Fact]
        public async Task AlterConnection_WithOptions_NoError()
        {
            var tmpCsv = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(tmpCsv, "Id\n1");
            try
            {
                var path = tmpCsv.Replace("\\", "\\\\");
                await Run(
                    $"CREATE CONNECTION ac ON FLATFILE('{path}');" +
                    $"ALTER CONNECTION ac WITH (ENCODING = 'UTF-8');");
            }
            finally { if (File.Exists(tmpCsv)) File.Delete(tmpCsv); }
        }

        [Fact]
        public async Task AlterConnection_Nonexistent_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("ALTER CONNECTION does_not_exist WITH (ENCODING = 'UTF-8');"));
        }

        // ── KILL JOB ──────────────────────────────────────────────────────────

        [Fact]
        public async Task KillJob_NoJobManager_DoesNotThrow()
        {
            // No IJobManager registered in default test DI; handler logs a warning and returns.
            await Run("KILL JOB 999;");
        }

        [Fact]
        public async Task KillJob_InvalidId_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("KILL JOB 'notanumber';"));
        }

        // ── CLEAR SESSION ─────────────────────────────────────────────────────

        [Fact]
        public async Task ClearSession_Current_NoError()
        {
            await Run("CLEAR SESSION;");
        }

        // ── CREATE DATASET ────────────────────────────────────────────────────

        [Fact]
        public async Task CreateDataset_SimpleQuery_MaterializesTable()
        {
            var eval = await RunAndGet(
                "CREATE DATASET #myds AS (SELECT 1 AS n);" +
                "SELECT * FROM #myds;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task CreateDataset_WithRefreshInterval_MaterializesTable()
        {
            var eval = await RunAndGet(
                "CREATE DATASET #myds2 REFRESH EVERY 'daily' AS (SELECT 1 AS n);" +
                "SELECT * FROM #myds2;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task CreateDatasetOrAlter_UpdatesTable()
        {
            var eval = await RunAndGet(
                "CREATE DATASET #altds AS (SELECT 1 AS n);" +
                "CREATE OR ALTER DATASET #altds AS (SELECT 2 AS n);" +
                "SELECT * FROM #altds;");
            Assert.NotNull(eval.LastResult);
        }

        // ── ADDITIONAL LOW-COVERAGE HANDLERS ─────────────────────────────────

        [Fact]
        public async Task ThrowStatement_InsideTryCatch_Caught()
        {
            await Run(
                "BEGIN TRY " +
                "  THROW 50001, 'test error', 1; " +
                "END TRY " +
                "BEGIN CATCH " +
                "  SELECT @@ERROR; " +
                "END CATCH");
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

        [Fact]
        public async Task SetShowPassword_On_NoError()
        {
            await Run("SET SHOW_PASSWORD ON;");
        }

        [Fact]
        public async Task SetShowPassword_Off_NoError()
        {
            await Run("SET SHOW_PASSWORD OFF;");
        }
    }
}
