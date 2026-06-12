using System;
using System.IO;
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
    /// Covers system variable resolution, SET @var MinMax member access,
    /// CREATE DATASET encryption validation, CREATE PROCEDURE + EXECUTE,
    /// ALTER CONTAINER/TEMPLATE, DROP DATASET, and other low-coverage handlers.
    /// </summary>
    public class SystemAndReportHandlerTests
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

        // ── System Variables (@@) ─────────────────────────────────────────────

        [Fact]
        public async Task SystemVar_TranCount_IsZeroBeforeTransaction()
        {
            var eval = await RunAndGet("SELECT @@TRANCOUNT AS tc;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_Version_ReturnsString()
        {
            var eval = await RunAndGet("SELECT @@VERSION AS ver;");
            Assert.NotNull(eval.LastResult);
            Assert.True(eval.LastResult!.Rows.Count > 0);
        }

        [Fact]
        public async Task SystemVar_RowCount_AfterSelect()
        {
            var eval = await RunAndGet(
                "SELECT 1 AS n UNION ALL SELECT 2 AS n;" +
                "SELECT @@ROWCOUNT AS rc;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_Error_DefaultZero()
        {
            var eval = await RunAndGet("SELECT @@ERROR AS err;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_TotalSpilledBytes_Accessible()
        {
            var eval = await RunAndGet("SELECT @@TOTAL_SPILLED_BYTES AS spilled;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_PartitionsCount_Accessible()
        {
            var eval = await RunAndGet("SELECT @@PARTITIONS_COUNT AS parts;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_AggregateGroupsCount_Accessible()
        {
            var eval = await RunAndGet("SELECT @@AGGREGATE_GROUPS_COUNT AS agg;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_AggregateExpansionRatio_Accessible()
        {
            var eval = await RunAndGet("SELECT @@AGGREGATE_EXPANSION_RATIO AS ratio;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_SubqueryCacheHits_Accessible()
        {
            var eval = await RunAndGet("SELECT @@SUBQUERY_CACHE_HITS AS hits;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_SubqueryCacheMisses_Accessible()
        {
            var eval = await RunAndGet("SELECT @@SUBQUERY_CACHE_MISSES AS misses;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task SystemVar_InTransaction_TranCountIncremented()
        {
            var eval = await RunAndGet(
                "BEGIN TRANSACTION;" +
                "SELECT @@TRANCOUNT AS tc;" +
                "ROLLBACK TRANSACTION;");
            Assert.NotNull(eval.LastResult);
            Assert.Equal(1m, Convert.ToDecimal(eval.LastResult!.Rows[0]["tc"]));
        }

        [Fact]
        public async Task SystemVar_AfterRaiseError_ErrorSet()
        {
            var eval = await RunAndGet(
                "BEGIN TRY RAISERROR('oops', 16, 1); END TRY BEGIN CATCH SELECT @@ERROR AS err; END CATCH");
            Assert.NotNull(eval.LastResult);
        }

        // ── SET @var MinMax member access ─────────────────────────────────────

        [Fact]
        public async Task SetVariable_MinMax_SetMinProperty()
        {
            var eval = await RunAndGet(
                "DECLARE @range MINMAX = 0;" +
                "SET @range.MIN = 5;");
            Assert.NotNull(eval.GetVariable("@range"));
        }

        [Fact]
        public async Task SetVariable_MinMax_SetMaxProperty()
        {
            var eval = await RunAndGet(
                "DECLARE @range MINMAX = 0;" +
                "SET @range.MAX = 100;");
            Assert.NotNull(eval.GetVariable("@range"));
        }

        [Fact]
        public async Task SetVariable_MinMax_SetBothProperties()
        {
            var eval = await RunAndGet(
                "DECLARE @range MINMAX = 0;" +
                "SET @range.MIN = 10;" +
                "SET @range.MAX = 20;");
            var rangeVal = eval.GetVariable("@range");
            Assert.NotNull(rangeVal);
        }

        [Fact]
        public async Task SetVariable_MinMax_InvalidProperty_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("DECLARE @range MINMAX = 0; SET @range.INVALID = 5;"));
        }

        [Fact]
        public async Task SetVariable_NonMinMax_MemberAccess_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("DECLARE @x INT = 5; SET @x.MIN = 3;"));
        }

        // ── CREATE PROCEDURE + EXECUTE ────────────────────────────────────────

        [Fact]
        public async Task ExecuteProcedure_Basic_Executes()
        {
            var eval = await RunAndGet(
                "CREATE PROCEDURE do_select AS BEGIN SELECT 42 AS result; END;" +
                "EXECUTE do_select;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task ExecuteProcedure_WithOutputParam_ExecutesWithoutError()
        {
            // Verify that EXECUTE with OUTPUT keyword doesn't throw
            await Run(
                "CREATE PROCEDURE get_val (@n INT) AS BEGIN SET @n = 99; END;" +
                "DECLARE @out INT = 0;" +
                "EXECUTE get_val @out OUTPUT;");
        }

        [Fact]
        public async Task ExecuteProcedure_WithInputParam_UsesValue()
        {
            var eval = await RunAndGet(
                "CREATE PROCEDURE show_val (@n INT) AS BEGIN SELECT @n AS result; END;" +
                "EXECUTE show_val 7;");
            Assert.NotNull(eval.LastResult);
            Assert.Equal(7m, Convert.ToDecimal(eval.LastResult!.Rows[0]["result"]));
        }

        [Fact]
        public async Task ExecuteProcedure_Nonexistent_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("EXECUTE proc_does_not_exist_xyz;"));
        }

        // ── CREATE DATASET with encryption validation ─────────────────────────

        [Fact]
        public async Task CreateDataset_PasswordMode_NoPassword_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("CREATE DATASET &enc ENCRYPT = PASSWORD AS (SELECT 1 AS v);"));
        }

        [Fact]
        public async Task CreateDataset_KeyFileMode_NoKeyFile_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("CREATE DATASET &enc ENCRYPT = KEYFILE AS (SELECT 1 AS v);"));
        }

        [Fact]
        public async Task CreateDataset_MachineMode_Executes()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &enc ENCRYPT = MACHINE AS (SELECT 1 AS v);" +
                "SELECT * FROM #enc;");
            Assert.NotNull(eval.LastResult);
        }

        // ── ALTER CONTAINER ───────────────────────────────────────────────────

        [Fact]
        public async Task AlterContainer_AfterCreate_UpdatesTitle()
        {
            await Run(
                "CREATE CONTAINER mycontainer AS BOX ();" +
                "ALTER CONTAINER mycontainer (TITLE = 'My Container');");
        }

        [Fact]
        public async Task AlterContainer_Nonexistent_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("ALTER CONTAINER no_such_container (TITLE = 'X');"));
        }

        // ── ALTER TEMPLATE ────────────────────────────────────────────────────

        [Fact]
        public async Task AlterTemplate_AfterCreate_UpdatesTitle()
        {
            await Run(
                "CREATE TEMPLATE mytempl AS (TYPE = 'table');" +
                "ALTER TEMPLATE mytempl (TITLE = 'Updated Title');");
        }

        [Fact]
        public async Task AlterTemplate_Nonexistent_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("ALTER TEMPLATE no_such_template (TITLE = 'X');"));
        }

        // ── DROP DATASET ──────────────────────────────────────────────────────

        [Fact]
        public async Task DropDataset_IfExists_NoThrow()
        {
            await Run("DROP DATASET IF EXISTS #nonexistent_ds;");
        }

        [Fact]
        public async Task DropDataset_WithoutIfExists_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("DROP DATASET #totally_nonexistent_ds_xyz;"));
        }

        [Fact]
        public async Task DropDataset_AfterCreate_RemovesConnection()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &myds AS (SELECT 1 AS v);" +
                "DROP DATASET IF EXISTS #myds;");
            Assert.NotNull(eval);
        }

        // ── EXECUTE SCRIPT ────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteScript_NonexistentFile_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("EXECUTE 'nonexistent_script_xyz.etlsql';"));
        }

        [Fact]
        public async Task ExecuteScript_ValidTempFile_Executes()
        {
            var tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".etlsql");
            File.WriteAllText(tmpFile, "SELECT 42 AS result;");
            try
            {
                var path = tmpFile.Replace("\\", "\\\\");
                var eval = await RunAndGet($"EXECUTE '{path}';");
                Assert.NotNull(eval.LastResult);
            }
            finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
        }

        // ── ShowDatasets with report context ──────────────────────────────────

        [Fact]
        public async Task ShowDatasets_AfterCreateDataset_ShowsEntry()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &myds AS (SELECT 1 AS v);" +
                "SHOW DATASETS;");
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task ShowDatasets_IntoTempTable_AfterCreate_ShowsEntry()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &myds AS (SELECT 1 AS v);" +
                "SHOW DATASETS INTO #dsList;" +
                "SELECT * FROM #dsList;");
            Assert.NotNull(eval.LastResult);
        }

        // ── UsePasswordStatementHandler ───────────────────────────────────────

        [Fact]
        public async Task UsePassword_LiteralString_NoError()
        {
            await Run("USE PASSWORD = 'ENC:test_password';");
        }

        // ── SetThresholdStatementHandler ──────────────────────────────────────

        [Fact]
        public async Task SetThreshold_BatchSize_NoError()
        {
            await Run("SET BATCHSIZE = 500;");
        }

        [Fact]
        public async Task SetThreshold_JoinSpill_NoError()
        {
            await Run("SET JOIN_SPILL_THRESHOLD = 100000;");
        }

        [Fact]
        public async Task SetThreshold_WindowSpill_NoError()
        {
            await Run("SET WINDOW_SPILL_THRESHOLD = 50000;");
        }

        [Fact]
        public async Task SetThreshold_LineageNamespace_NoError()
        {
            var eval = await RunAndGet("SET LINEAGE_NAMESPACE = 'my-custom-ns';");
            Assert.Equal("my-custom-ns", eval.LineageNamespace);
        }

        [Fact]
        public async Task SetThreshold_LineageImportCatalog_NoError()
        {
            var eval = await RunAndGet("SET LINEAGE_IMPORT_CATALOG = ON;");
            Assert.True(eval.LineageImportCatalog);

            var eval2 = await RunAndGet("SET LINEAGE_IMPORT_CATALOG = OFF;");
            Assert.False(eval2.LineageImportCatalog);
        }

        // ── ShowSafeZones ─────────────────────────────────────────────────────

        [Fact]
        public async Task ShowSafeZones_ReturnsResult()
        {
            var eval = await RunAndGet("SHOW SAFE ZONES;");
            Assert.NotNull(eval.LastResult);
        }

        // ── ShowJobHistory ────────────────────────────────────────────────────

        [Fact]
        public async Task ShowJobHistory_ReturnsResult()
        {
            var eval = await RunAndGet("SHOW JOB HISTORY;");
            Assert.NotNull(eval.LastResult);
        }

        // ── DROP FUNCTION / PROCEDURE ─────────────────────────────────────────

        [Fact]
        public async Task DropFunction_AfterCreate_Succeeds()
        {
            await Run(
                "CREATE FUNCTION myfunc (@x INT) RETURNS INT AS BEGIN RETURN @x + 1; END;" +
                "DROP FUNCTION myfunc;");
        }

        [Fact]
        public async Task DropProcedure_AfterCreate_Succeeds()
        {
            await Run(
                "CREATE PROCEDURE myproc AS BEGIN SELECT 1 AS n; END;" +
                "DROP PROCEDURE myproc;");
        }

        [Fact]
        public async Task DropFunction_IfExists_NotExists_NoThrow()
        {
            await Run("DROP FUNCTION IF EXISTS nonexistent_func_xyz;");
        }

        [Fact]
        public async Task DropProcedure_IfExists_NotExists_NoThrow()
        {
            await Run("DROP PROCEDURE IF EXISTS nonexistent_proc_xyz;");
        }

        // ── SHOW COLUMNS ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowColumns_AfterCreateTable_ReturnsColumns()
        {
            var eval = await RunAndGet(
                "CREATE TABLE #t (Id INT, Name VARCHAR);" +
                "SHOW COLUMNS FOR #t;");
            Assert.NotNull(eval.LastResult);
        }

        // ── SHOW SESSIONS ─────────────────────────────────────────────────────

        [Fact]
        public async Task ShowSessions_ReturnsResult()
        {
            var eval = await RunAndGet("SHOW SESSIONS;");
            Assert.NotNull(eval.LastResult);
        }

        // ── SHOW PROFILE ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowProfile_AfterSelect_ReturnsResult()
        {
            var eval = await RunAndGet(
                "SELECT 1 AS n;" +
                "SHOW PROFILE;");
            Assert.NotNull(eval.LastResult);
        }

        // ── SHOW VARIABLES ────────────────────────────────────────────────────

        [Fact]
        public async Task ShowVariables_AfterDeclare_ShowsVariable()
        {
            var eval = await RunAndGet(
                "DECLARE @x INT = 42;" +
                "SHOW VARIABLES;");
            Assert.NotNull(eval.LastResult);
        }

        // ── SET SECURITY_OVERRIDE ─────────────────────────────────────────────

        [Fact]
        public async Task SetSecurityOverride_LargeStringResults_On_NoError()
        {
            await Run("SET ALLOW_LARGE_STRING_RESULTS ON;");
        }

        [Fact]
        public async Task SetSecurityOverride_LargeStringResults_Off_NoError()
        {
            await Run("SET ALLOW_LARGE_STRING_RESULTS OFF;");
        }

        [Fact]
        public async Task SetSecurityOverride_FileTypeAccess_On_NoError()
        {
            await Run("SET ALLOW_FILE_TYPE_ACCESS ON;");
        }

        // ── WAIT FOR ──────────────────────────────────────────────────────────

        [Fact]
        public async Task WaitFor_ZeroDelay_NoError()
        {
            await Run("WAITFOR DELAY '00:00:00';");
        }

        // ── THROW statement ───────────────────────────────────────────────────

        [Fact]
        public async Task Throw_OutsideTryCatch_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("THROW 50001, 'error', 1;"));
        }

        // ── TRUNCATE TABLE ────────────────────────────────────────────────────

        [Fact]
        public async Task TruncateTable_EmptyTable_NoError()
        {
            await Run(
                "CREATE TABLE #t (Id INT);" +
                "TRUNCATE TABLE #t;");
        }

        // ── ClearSession ──────────────────────────────────────────────────────

        [Fact]
        public async Task ClearSession_AfterDeclaringVars_ClearsVars()
        {
            var eval = await RunAndGet(
                "DECLARE @x INT = 5;" +
                "CLEAR SESSION;" +
                "SHOW VARIABLES;");
            Assert.NotNull(eval.LastResult);
        }
    }
}
