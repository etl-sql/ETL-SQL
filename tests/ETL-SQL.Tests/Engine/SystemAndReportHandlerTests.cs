using System;
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

        private static decimal Scalar(Evaluator eval, string column) =>
            Convert.ToDecimal(eval.LastResult!.Rows[0][column]);

        private static ETL_SQL.Common.MinMaxValue MinMax(Evaluator eval) =>
            Assert.IsType<ETL_SQL.Common.MinMaxValue>(eval.GetVariable("@range"));

        // ── System Variables (@@) ─────────────────────────────────────────────

        [Fact]
        public async Task SystemVar_TranCount_IsZeroBeforeTransaction()
        {
            var eval = await RunAndGet("SELECT @@TRANCOUNT AS tc;");
            Assert.Equal(0m, Scalar(eval, "tc"));
        }

        [Fact]
        public async Task SystemVar_Version_ReturnsString()
        {
            var eval = await RunAndGet("SELECT @@VERSION AS ver;");
            Assert.False(string.IsNullOrWhiteSpace(eval.LastResult!.Rows[0]["ver"]?.ToString()));
        }

        [Fact]
        public async Task SystemVar_RowCount_AfterSelect()
        {
            var eval = await RunAndGet(
                "SELECT 1 AS n UNION ALL SELECT 2 AS n;" +
                "SELECT @@ROWCOUNT AS rc;");
            Assert.Equal(2m, Scalar(eval, "rc"));
        }

        [Fact]
        public async Task SystemVar_Error_DefaultZero()
        {
            var eval = await RunAndGet("SELECT @@ERROR AS err;");
            Assert.Equal(0m, Scalar(eval, "err"));
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
                "BEGIN TRY RAISERROR('oops', 16, 1); END TRY BEGIN CATCH SELECT @@ERROR AS err, ERROR_MESSAGE() AS msg; END CATCH");
            Assert.NotEqual(0m, Scalar(eval, "err"));
            Assert.Equal("oops", eval.LastResult!.Rows[0]["msg"]?.ToString());
        }

        // ── SET @var MinMax member access ─────────────────────────────────────

        [Fact]
        public async Task SetVariable_MinMax_SetsMin()
        {
            var eval = await RunAndGet(
                "DECLARE @range MINMAX = 0;" +
                "SET @range.MIN = 5;");
            Assert.Equal(5m, Convert.ToDecimal(MinMax(eval).Min));
        }

        [Fact]
        public async Task SetVariable_MinMax_SetsMax()
        {
            var eval = await RunAndGet(
                "DECLARE @range MINMAX = 0;" +
                "SET @range.MAX = 100;");
            Assert.Equal(100m, Convert.ToDecimal(MinMax(eval).Max));
        }

        [Fact]
        public async Task SetVariable_MinMax_SetsBoth()
        {
            var eval = await RunAndGet(
                "DECLARE @range MINMAX = 0;" +
                "SET @range.MIN = 10;" +
                "SET @range.MAX = 20;");
            var range = MinMax(eval);
            Assert.Equal(10m, Convert.ToDecimal(range.Min));
            Assert.Equal(20m, Convert.ToDecimal(range.Max));
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
        public async Task ExecuteProcedure_ReturnsItsResult()
        {
            var eval = await RunAndGet(
                "CREATE PROCEDURE do_select AS BEGIN SELECT 42 AS result; END;" +
                "EXECUTE do_select;");
            Assert.Equal(42m, Scalar(eval, "result"));
        }

        [Fact]
        public async Task ExecuteProcedure_WithOutputParam_WritesTheCallerVariable()
        {
            var eval = await RunAndGet(
                "CREATE PROCEDURE get_val (@n INT) AS BEGIN SET @n = 99; END;" +
                "DECLARE @out INT = 0;" +
                "EXECUTE get_val @out OUTPUT;");
            Assert.Equal(99m, Convert.ToDecimal(eval.GetVariable("@out")));
        }

        [Fact]
        public async Task ExecuteProcedure_NamedOutputParam_WritesTheCallerVariable_AndInputsStayPut()
        {
            var eval = await RunAndGet(
                "CREATE PROCEDURE add_one (@x INT, @n INT) AS BEGIN SET @x = 0; SET @n = @x + 1; RETURN; END;" +
                "DECLARE @in INT = 5;" +
                "DECLARE @out INT = 0;" +
                "EXECUTE add_one @x = @in, @n = @out OUTPUT;");
            Assert.Equal(1m, Convert.ToDecimal(eval.GetVariable("@out")));
            Assert.Equal(5m, Convert.ToDecimal(eval.GetVariable("@in")));
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
            var eval = Eval();

            await Assert.ThrowsAsync<ExecutionException>(() =>
                eval.Evaluate(Parse("EXECUTE proc_does_not_exist_xyz;")));

            Assert.Equal(0, eval.CurrentRecursiveDepth);
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
                "SELECT * FROM &enc;");
            Assert.Single(eval.LastResult!.Rows);
        }

        // ── ALTER CONTAINER ───────────────────────────────────────────────────

        [Fact]
        public async Task AlterContainer_AfterCreate_UpdatesTitle()
        {
            var eval = await RunAndGet(
                "CREATE CONTAINER mycontainer AS BOX ();" +
                "ALTER CONTAINER mycontainer (TITLE = 'My Container');");
            Assert.Contains("My Container", eval.ReportContext.ContainerDefinitions["mycontainer"].Title!.ToSql());
        }

        [Fact]
        public async Task AlterContainer_Nonexistent_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("ALTER CONTAINER no_such_container (TITLE = 'X');"));
        }

        // ── ALTER TEMPLATE ────────────────────────────────────────────────────

        [Fact]
        public async Task AlterTemplate_AfterCreate_PatchesOptions()
        {
            var eval = await RunAndGet(
                "CREATE TEMPLATE mytempl AS (TYPE = 'table');" +
                "ALTER TEMPLATE mytempl (OPTIONS (TYPE = 'card'));");
            var options = eval.ReportContext.TemplateDefinitions["mytempl"].Options;
            Assert.Contains("card", options.First(o => o.Key.Equals("TYPE", StringComparison.OrdinalIgnoreCase)).Value);
        }

        /// <summary>
        /// This test used to be named "UpdatesTitle" and passed a TITLE clause, but a template is an
        /// option bag with no title: the handler patches <c>Options</c> and nothing else, so the
        /// clause was parsed and silently discarded. It asserted only that no exception was thrown,
        /// which is exactly what a statement that does nothing achieves. The parser now refuses it.
        /// </summary>
        [Fact]
        public async Task AlterTemplate_Title_IsRefused_NotSilentlyDiscarded()
        {
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(
                "CREATE TEMPLATE mytempl2 AS (TYPE = 'table');" +
                "ALTER TEMPLATE mytempl2 (TITLE = 'Updated Title');"));

            Assert.Contains("ALTER TEMPLATE does not support TITLE", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task AlterTemplate_Nonexistent_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("ALTER TEMPLATE no_such_template (TITLE = 'X');"));
        }

        // ── DROP DATASET ──────────────────────────────────────────────────────

        [Fact]
        public async Task DropDataset_IfExists_OnAMissingDataset_DoesNotThrow()
        {
            var eval = await RunAndGet("DROP DATASET IF EXISTS &nonexistent_ds;");
            Assert.Empty(eval.ReportContext.DatasetDefinitions);
        }

        [Fact]
        public async Task DropDataset_WithoutIfExists_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("DROP DATASET &totally_nonexistent_ds_xyz;"));
        }

        [Fact]
        public async Task DropDataset_AfterCreate_RemovesTheDataset()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &myds AS (SELECT 1 AS v);" +
                "DROP DATASET IF EXISTS &myds;");
            Assert.Empty(eval.ReportContext.DatasetDefinitions);
            await Assert.ThrowsAsync<ExecutionException>(() => eval.Evaluate(Parse("SELECT * FROM &myds;")));
        }

        // ── EXECUTE SCRIPT ────────────────────────────────────────────────────

        [Fact]
        public async Task ExecuteScript_NonexistentFile_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(() =>
                Run("EXECUTE 'nonexistent_script_xyz.etlsql';"));
        }

        [Fact]
        public async Task ExecuteScript_ValidTempFile_ReturnsItsResult()
        {
            var tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".etlsql");
            File.WriteAllText(tmpFile, "SELECT 42 AS result;");
            try
            {
                var path = tmpFile.Replace("\\", "\\\\");
                var eval = await RunAndGet($"EXECUTE '{path}';");
                Assert.Equal(42m, Scalar(eval, "result"));
            }
            finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
        }

        [Fact]
        public async Task ExecuteScript_RelativePath_ResolvesFromCurrentScriptPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etlsql_exec_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var mainPath = Path.Combine(dir, "main.etlsql");
            var subPath = Path.Combine(dir, "sub.etlsql");
            File.WriteAllText(mainPath, "EXECUTE 'sub.etlsql';");
            File.WriteAllText(subPath, "SELECT 99 AS result;");

            try
            {
                var eval = Eval();
                eval.CurrentScriptPath = mainPath;
                await eval.Evaluate(Parse("EXECUTE 'sub.etlsql';"));

                Assert.NotNull(eval.LastResult);
                Assert.Equal(99m, Convert.ToDecimal(eval.LastResult!.Rows[0]["result"]));
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ExecuteScript_NestedRelativePath_ResolvesFromExecutedScriptPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etlsql_exec_" + Guid.NewGuid().ToString("N"));
            var nestedDir = Path.Combine(dir, "nested");
            Directory.CreateDirectory(nestedDir);
            var mainPath = Path.Combine(dir, "main.etlsql");
            var nestedPath = Path.Combine(nestedDir, "nested.etlsql");
            var leafPath = Path.Combine(nestedDir, "leaf.etlsql");
            File.WriteAllText(mainPath, "EXECUTE 'nested/nested.etlsql';");
            File.WriteAllText(nestedPath, "EXECUTE 'leaf.etlsql';");
            File.WriteAllText(leafPath, "SELECT 101 AS result;");

            try
            {
                var eval = Eval();
                eval.CurrentScriptPath = mainPath;
                await eval.Evaluate(Parse("EXECUTE 'nested/nested.etlsql';"));

                Assert.NotNull(eval.LastResult);
                Assert.Equal(101m, Convert.ToDecimal(eval.LastResult!.Rows[0]["result"]));
                Assert.Equal(mainPath, eval.CurrentScriptPath);
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        // ── eng.datasets ──────────────────────────────────────────────────────

        [Fact]
        public async Task EngDatasets_AfterCreateDataset_ListsIt()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &myds AS (SELECT 1 AS v);" +
                "SELECT * FROM eng.datasets;");
            Assert.Contains(eval.LastResult!.Rows, r => r["name"]?.ToString()?.Contains("myds", StringComparison.OrdinalIgnoreCase) == true);
        }

        [Fact]
        public async Task EngDatasets_IntoTempTable_ListsIt()
        {
            var eval = await RunAndGet(
                "CREATE DATASET &myds AS (SELECT 1 AS v);" +
                "SELECT * INTO #dsList FROM eng.datasets;" +
                "SELECT * FROM #dsList;");
            Assert.Contains(eval.LastResult!.Rows, r => r["name"]?.ToString()?.Contains("myds", StringComparison.OrdinalIgnoreCase) == true);
        }

        // ── UsePasswordStatementHandler ───────────────────────────────────────

        [Fact]
        public async Task UsePassword_LiteralString_SetsTheScriptPassword()
        {
            var eval = await RunAndGet("USE PASSWORD = 'test_password';");
            Assert.Equal("test_password", eval.ScriptPassword);
        }

        // ── SetThresholdStatementHandler ──────────────────────────────────────

        [Fact]
        public async Task SetThreshold_BatchSize_SetsIt()
        {
            var eval = await RunAndGet("SET BATCHSIZE = 500;");
            Assert.Equal(500, eval.BatchSize);
        }

        [Fact]
        public async Task SetThreshold_JoinSpill_SetsIt()
        {
            var eval = await RunAndGet("SET JOIN_SPILL_THRESHOLD = 100000;");
            Assert.Equal(100000, eval.JoinSpillThreshold);
        }

        [Fact]
        public async Task SetThreshold_WindowSpill_SetsIt()
        {
            var eval = await RunAndGet("SET WINDOW_SPILL_THRESHOLD = 50000;");
            Assert.Equal(50000, eval.WindowSpillThreshold);
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
        public async Task SafeZones_HasThePublishedColumns()
        {
            var eval = await RunAndGet("SELECT * FROM eng.safe_zones;");
            Assert.Equal(["path", "is_system_path", "resolution"], eval.LastResult!.ColumnNames);
        }

        // ── ShowJobHistory ────────────────────────────────────────────────────

        [Fact]
        public async Task JobHistory_HasAStatusColumn()
        {
            var eval = await RunAndGet("SELECT * FROM eng.job_history;");
            Assert.Contains("status", eval.LastResult!.ColumnNames);
        }

        // ── DROP FUNCTION / PROCEDURE ─────────────────────────────────────────

        [Fact]
        public async Task DropFunction_AfterCreate_RemovesIt()
        {
            var eval = await RunAndGet(
                "CREATE FUNCTION myfunc (@x INT) RETURNS INT AS BEGIN RETURN @x + 1; END;" +
                "DROP FUNCTION myfunc;");
            Assert.False(eval.TryGetFunction("myfunc", out _));
        }

        [Fact]
        public async Task DropProcedure_AfterCreate_RemovesIt()
        {
            var eval = await RunAndGet(
                "CREATE PROCEDURE myproc AS BEGIN SELECT 1 AS n; END;" +
                "DROP PROCEDURE myproc;");
            Assert.False(eval.TryGetProcedure("myproc", out _));
        }

        [Fact]
        public async Task DropFunction_IfExists_OnAMissingFunction_DoesNotThrow()
        {
            var eval = await RunAndGet("DROP FUNCTION IF EXISTS nonexistent_func_xyz;");
            Assert.False(eval.TryGetFunction("nonexistent_func_xyz", out _));
        }

        [Fact]
        public async Task DropProcedure_IfExists_OnAMissingProcedure_DoesNotThrow()
        {
            var eval = await RunAndGet("DROP PROCEDURE IF EXISTS nonexistent_proc_xyz;");
            Assert.False(eval.TryGetProcedure("nonexistent_proc_xyz", out _));
        }

        // ── eng.columns ───────────────────────────────────────────────────────

        [Fact]
        public async Task EngColumns_AfterCreateTable_ReturnsColumns()
        {
            var eval = await RunAndGet(
                "CREATE TABLE #t (Id INT, Name VARCHAR);" +
                "SELECT * FROM eng.columns WHERE table_name = '#t';");
            Assert.Equal(["Id", "Name"], eval.LastResult!.Rows.Select(r => r["column_name"]?.ToString()).Order(StringComparer.Ordinal));
        }

        // ── SHOW SESSIONS ─────────────────────────────────────────────────────

        [Fact]
        public async Task Sessions_HasASessionIdColumn()
        {
            var eval = await RunAndGet("SELECT * FROM eng.sessions;");
            Assert.Contains("session_id", eval.LastResult!.ColumnNames);
        }

        // ── SHOW LOCKS ────────────────────────────────────────────────────────

        [Fact]
        public async Task Locks_HasAJobNameColumn()
        {
            var eval = await RunAndGet("SELECT * FROM eng.locks;");
            Assert.Contains("job_name", eval.LastResult!.ColumnNames);
        }

        // ── SHOW PROFILE ──────────────────────────────────────────────────────

        [Fact]
        public async Task Profile_AfterSelect_HasRows()
        {
            var eval = await RunAndGet(
                "SELECT 1 AS n;" +
                "SELECT * FROM eng.profile;");
            Assert.NotEmpty(eval.LastResult!.Rows);
        }

        // ── SHOW VARIABLES ────────────────────────────────────────────────────

        [Fact]
        public async Task ShowVariables_AfterDeclare_ShowsVariable()
        {
            var eval = await RunAndGet(
                "DECLARE @x INT = 42;" +
                "SELECT * FROM eng.variables;");
            var row = Assert.Single(eval.LastResult!.Rows, r => r["variable_name"]?.ToString()?.TrimStart('@') == "x");
            Assert.Equal("42", row["value"]?.ToString());
        }

        // ── SET SECURITY_OVERRIDE ─────────────────────────────────────────────

        [Fact]
        public async Task SetSecurityOverride_LargeStringResults_On_SetsIt()
        {
            var eval = await RunAndGet("SET ALLOW_LARGE_STRING_RESULTS ON;");
            Assert.True(eval.AllowLargeStringResults);
        }

        [Fact]
        public async Task SetSecurityOverride_LargeStringResults_Off_ClearsIt()
        {
            var eval = await RunAndGet("SET ALLOW_LARGE_STRING_RESULTS ON; SET ALLOW_LARGE_STRING_RESULTS OFF;");
            Assert.False(eval.AllowLargeStringResults);
        }

        [Fact]
        public async Task SetSecurityOverride_FileTypeAccess_On_SetsIt()
        {
            var eval = await RunAndGet("SET ALLOW_FILE_TYPE_ACCESS ON;");
            Assert.True(eval.AllowUnknownFileTypes);
        }

        // ── WAIT FOR ──────────────────────────────────────────────────────────

        [Fact]
        public async Task WaitFor_ZeroDelay_ContinuesTheScript()
        {
            var eval = await RunAndGet("WAITFOR DELAY '00:00:00'; SELECT 1 AS after_wait;");
            Assert.Equal(1m, Scalar(eval, "after_wait"));
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
        public async Task TruncateTable_EmptyTable_StaysEmpty()
        {
            var eval = await RunAndGet(
                "CREATE TABLE #t (Id INT);" +
                "TRUNCATE TABLE #t;" +
                "SELECT COUNT(*) AS n FROM #t;");
            Assert.Equal(0m, Scalar(eval, "n"));
        }

        // ── ClearSession ──────────────────────────────────────────────────────

        [Fact]
        public async Task ClearSession_AfterDeclaringVars_ClearsVars()
        {
            var eval = await RunAndGet(
                "DECLARE @x INT = 5;" +
                "CLEAR SESSION;" +
                "SELECT * FROM eng.variables;");
            Assert.DoesNotContain(eval.LastResult!.Rows, r => r["variable_name"]?.ToString()?.TrimStart('@') == "x");
        }
    }
}
