using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Planning;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Exercises IF/ELSE, SET @var, RAISERROR, WAITFOR, GENERATE, file/directory ops,
    /// system variables, and other low-coverage statement handlers.
    /// </summary>
    public class MoreHandlerTests
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
            eval.RedirectOutput = true;
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        private static string Output(Evaluator eval) => string.Join("\n", eval.Messages.Select(m => m.Message));

        // ── IF / ELSE IF / ELSE ───────────────────────────────────────────────

        [Fact]
        public async Task If_TrueCondition_ExecutesThenBranch()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @x INT = 0; IF 1 = 1 SET @x = 10;"));
            Assert.Equal(10m, eval.GetVariable("@x"));
        }

        [Fact]
        public async Task If_FalseCondition_SkipsThenBranch()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @x INT = 0; IF 1 = 2 SET @x = 10;"));
            Assert.Equal(0m, eval.GetVariable("@x"));
        }

        [Fact]
        public async Task If_FalseCondition_ExecutesElseBranch()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @x INT = 0; IF 1 = 2 SET @x = 10; ELSE SET @x = 99;"));
            Assert.Equal(99m, eval.GetVariable("@x"));
        }

        [Fact]
        public async Task If_ElseIf_MatchesCorrectBranch()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @v INT = 0;
                DECLARE @score INT = 75;
                IF @score >= 90 SET @v = 1;
                ELSE IF @score >= 70 SET @v = 2;
                ELSE SET @v = 3;
            "));
            Assert.Equal(2m, eval.GetVariable("@v"));
        }

        [Fact]
        public async Task If_ElseIf_FallsToElse()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @v INT = 0;
                DECLARE @score INT = 50;
                IF @score >= 90 SET @v = 1;
                ELSE IF @score >= 70 SET @v = 2;
                ELSE SET @v = 3;
            "));
            Assert.Equal(3m, eval.GetVariable("@v"));
        }

        [Fact]
        public async Task If_WithBlock_ExecutesBlock()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @a INT = 0;
                DECLARE @b INT = 0;
                IF 1 = 1 BEGIN
                    SET @a = 5;
                    SET @b = 10;
                END
            "));
            Assert.Equal(5m, eval.GetVariable("@a"));
            Assert.Equal(10m, eval.GetVariable("@b"));
        }

        // ── SET @variable ─────────────────────────────────────────────────────

        [Fact]
        public async Task SetVariable_AssignsIntegerValue()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @n INT = 0; SET @n = 42;"));
            Assert.Equal(42m, eval.GetVariable("@n"));
        }

        [Fact]
        public async Task SetVariable_AssignsStringValue()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @s STRING = ''; SET @s = 'hello';"));
            Assert.Equal("hello", eval.GetVariable("@s"));
        }

        [Fact]
        public async Task SetVariable_AssignsExpressionResult()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @x INT = 5; SET @x = @x * 2 + 1;"));
            Assert.Equal(11m, eval.GetVariable("@x"));
        }

        [Fact]
        public async Task SetVariable_UndeclaredVariable_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("SET @undeclared = 1;"));
        }

        [Fact]
        public async Task SetVariable_AssignsNullValue()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @x INT = 5; SET @x = NULL;"));
            Assert.Null(eval.GetVariable("@x"));
        }

        // ── RAISERROR ─────────────────────────────────────────────────────────

        [Fact]
        public async Task RaiseError_Severity1_LogsWithoutThrowing()
        {
            var eval = await RunAndGet("RAISERROR('Info message', 1); SELECT 1 AS after_raise;");
            Assert.Contains("Info message", Output(eval));
            Assert.Equal(1m, Convert.ToDecimal(eval.LastResult!.Rows[0]["after_raise"]));
        }

        [Fact]
        public async Task RaiseError_Severity4_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("RAISERROR('Error message', 4);"));
        }

        [Fact]
        public async Task RaiseError_Severity10_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("RAISERROR('Critical error', 10);"));
        }

        [Fact]
        public async Task RaiseError_CaughtInTryCatch_RunsTheCatchBlock()
        {
            var eval = await RunAndGet(@"
                BEGIN TRY
                    RAISERROR('Test', 5);
                END
                BEGIN CATCH
                    PRINT 'Caught raiserror';
                END
            ");
            Assert.Contains("Caught raiserror", Output(eval));
        }

        [Fact]
        public async Task RaiseError_InfoSeverity_MessageContainsINFO()
        {
            var eval = await RunAndGet("RAISERROR('Low severity', 2);");
            Assert.Contains("[INFO] Low severity", Output(eval));
        }

        // ── WAITFOR DELAY ─────────────────────────────────────────────────────

        [Fact]
        public async Task WaitFor_ZeroDelay_ContinuesTheScript()
        {
            var eval = await RunAndGet("WAITFOR DELAY '00:00:00'; SELECT 1 AS after_wait;");
            Assert.Equal(1m, Convert.ToDecimal(eval.LastResult!.Rows[0]["after_wait"]));
        }

        [Fact]
        public async Task WaitFor_InvalidFormat_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("WAITFOR DELAY 'not-a-time';"));
        }

        // ── GENERATE ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Generate_BasicSequence_ProducesRows()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                GENERATE 5 ROWS INTO #Gen AS (
                    Id = 'SEQUENCE(1,1)',
                    Name = 'NAME'
                );
                SELECT COUNT(*) AS N FROM #Gen;
            "));
            Assert.Equal(5m, eval.LastResult?.Rows[0]["N"]);
        }

        [Fact]
        public async Task Generate_WithSeed_ProducesConsistentData()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                GENERATE 3 ROWS INTO #G WITH (SEED = 42) AS (
                    Id = 'SEQUENCE(1,1)',
                    Val = 'INTEGER(1,100)'
                );
                SELECT COUNT(*) AS N FROM #G;
            "));
            Assert.Equal(3m, eval.LastResult?.Rows[0]["N"]);
        }

        [Fact]
        public async Task Generate_ZeroRows_ProducesEmptyTable()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                GENERATE 0 ROWS INTO #Empty AS (
                    Id = 'SEQUENCE(1,1)'
                );
                SELECT COUNT(*) AS N FROM #Empty;
            "));
            Assert.Equal(0m, eval.LastResult?.Rows[0]["N"]);
        }

        [Fact]
        public async Task Generate_ExceedsMaxRows_ThrowsWhenLimited()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run(@"
                SET MAX_GENERATE_ROWS = 10;
                GENERATE 100 ROWS INTO #T AS (Id = 'SEQUENCE(1,1)');
            "));
        }

        // ── DIRECTORY operations ──────────────────────────────────────────────

        [Fact]
        public async Task CreateDirectory_CreatesDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etlsql_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                await Run($"CREATE DIRECTORY '{dir}';");
                Assert.True(Directory.Exists(dir));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Fact]
        public async Task DeleteDirectory_DeletesDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etlsql_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            await Run($"DELETE DIRECTORY '{dir}';");
            Assert.False(Directory.Exists(dir));
        }

        [Fact]
        public async Task DeleteDirectory_IfExists_NonExistent_ContinuesTheScript()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etlsql_test_nonexistent_" + Guid.NewGuid().ToString("N"));
            var eval = await RunAndGet($"DELETE DIRECTORY IF EXISTS '{dir}'; SELECT 1 AS after_delete;");
            Assert.Equal(1m, Convert.ToDecimal(eval.LastResult!.Rows[0]["after_delete"]));
            Assert.False(Directory.Exists(dir));
        }

        [Fact]
        public async Task CopyDirectory_CopiesContents()
        {
            var src = Path.Combine(Path.GetTempPath(), "etlsql_src_" + Guid.NewGuid().ToString("N"));
            var dst = Path.Combine(Path.GetTempPath(), "etlsql_dst_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(src);
                File.WriteAllText(Path.Combine(src, "a.txt"), "hello");
                await Run($"COPY DIRECTORY '{src}' TO '{dst}';");
                Assert.True(Directory.Exists(dst));
            }
            finally
            {
                if (Directory.Exists(src)) Directory.Delete(src, true);
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
            }
        }

        // ── FILE operations ───────────────────────────────────────────────────

        [Fact]
        public async Task DeleteFile_ExistingFile_DeletesIt()
        {
            var f = Path.Combine(Path.GetTempPath(), "etlsql_test_" + Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(f, "Id,Name\n1,Alice");
            try
            {
                await Run($"DELETE FILE '{f.Replace("\\", "\\\\")}';");
                Assert.False(File.Exists(f));
            }
            finally
            {
                if (File.Exists(f)) File.Delete(f);
            }
        }

        [Fact]
        public async Task DeleteFile_IfExists_NonExistent_ContinuesTheScript()
        {
            var f = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N") + ".txt");
            var eval = await RunAndGet($"DELETE FILE IF EXISTS '{f}'; SELECT 1 AS after_delete;");
            Assert.Equal(1m, Convert.ToDecimal(eval.LastResult!.Rows[0]["after_delete"]));
            Assert.False(File.Exists(f));
        }

        [Fact]
        public async Task CopyFile_CopiesSourceToDestination()
        {
            var src = Path.Combine(Path.GetTempPath(), "etlsql_src_" + Guid.NewGuid().ToString("N") + ".txt");
            var dst = Path.Combine(Path.GetTempPath(), "etlsql_dst_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(src, "test content");
                await Run($"COPY FILE '{src}' TO '{dst}';");
                Assert.True(File.Exists(dst));
                Assert.Equal("test content", File.ReadAllText(dst));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task MoveFile_MovesSourceToDestination()
        {
            var src = Path.Combine(Path.GetTempPath(), "etlsql_src_" + Guid.NewGuid().ToString("N") + ".txt");
            var dst = Path.Combine(Path.GetTempPath(), "etlsql_moved_" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(src, "data");
                await Run($"MOVE FILE '{src}' TO '{dst}';");
                Assert.False(File.Exists(src));
                Assert.True(File.Exists(dst));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(dst)) File.Delete(dst);
            }
        }

        [Fact]
        public async Task CompressFile_CreatesArchive()
        {
            var src = Path.Combine(Path.GetTempPath(), "etlsql_src_" + Guid.NewGuid().ToString("N") + ".txt");
            var archive = Path.Combine(Path.GetTempPath(), "etlsql_archive_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                File.WriteAllText(src, "compress me");
                await Run($"COMPRESS FILE '{src}' TO '{archive}';");
                Assert.True(File.Exists(archive));
            }
            finally
            {
                if (File.Exists(src)) File.Delete(src);
                if (File.Exists(archive)) File.Delete(archive);
            }
        }

        // ── SYSTEM VARIABLES (@@) ─────────────────────────────────────────────

        [Fact]
        public async Task SystemVar_Version_ReturnsString()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @v STRING = @@VERSION;"));
            var version = Assert.IsType<string>(eval.GetVariable("@v"));
            Assert.False(string.IsNullOrWhiteSpace(version));
            Assert.Contains(".", version);
        }

        [Fact]
        public async Task SystemVar_Rowcount_AfterSelect_ReturnsCount()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS N INTO #T;
                INSERT INTO #T VALUES (2);
                SELECT * FROM #T;
                DECLARE @r INT = @@ROWCOUNT;
            "));
            Assert.Equal(2m, Convert.ToDecimal(eval.GetVariable("@r")));
        }

        [Fact]
        public async Task SystemVar_TranCount_DefaultIsZero()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @t INT = @@TRANCOUNT;"));
            Assert.Equal(0m, eval.GetVariable("@t"));
        }

        [Fact]
        public async Task SystemVar_Error_DefaultIsZero()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @e INT = @@ERROR;"));
            Assert.Equal(0m, eval.GetVariable("@e"));
        }

        // ── RETURN from procedure ─────────────────────────────────────────────

        [Fact]
        public async Task Return_FromProcedure_ExitsEarly()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @r INT = 0;
                CREATE PROCEDURE EarlyReturn AS BEGIN
                    RETURN;
                    SET @r = 99;
                END;
                EXEC EarlyReturn;
            "));
            Assert.Equal(0m, eval.GetVariable("@r"));
        }

        [Fact]
        public async Task Return_WithValue_EndsTheProcedure()
        {
            var eval = await RunAndGet(@"
                DECLARE @r INT = 0;
                CREATE PROCEDURE ReturnCode AS BEGIN
                    RETURN 5;
                    SET @r = 99;
                END;
                EXEC ReturnCode;
            ");
            Assert.Equal(0m, Convert.ToDecimal(eval.GetVariable("@r")));
        }

        // ── EXEC / CALL procedure ─────────────────────────────────────────────

        [Fact]
        public async Task Exec_SimpleProc_RunsItsBody()
        {
            var eval = await RunAndGet(@"
                CREATE PROCEDURE Greet AS BEGIN
                    PRINT 'Hello';
                END;
                EXEC Greet;
            ");
            Assert.Contains("Hello", Output(eval));
        }

        [Fact]
        public async Task Exec_ProcWithParam_PassesValue()
        {
            var eval = await RunAndGet(@"
                CREATE PROCEDURE Double(@n INT) AS BEGIN
                    SELECT @n * 2 AS Result INTO #Out;
                END;
                EXEC Double 5;
                SELECT * FROM #Out;
            ");
            Assert.Equal(10m, Convert.ToDecimal(eval.LastResult!.Rows[0]["Result"]));
        }

        // ── SET WHAT_IF ───────────────────────────────────────────────────────

        [Fact]
        public async Task SetWhatIf_On_EnablesWhatIfMode()
        {
            var eval = await RunAndGet("SET WHAT_IF ON;");
            Assert.True(eval.IsWhatIf);
        }

        [Fact]
        public async Task SetWhatIf_Off_DisablesWhatIfMode()
        {
            var eval = await RunAndGet("SET WHAT_IF ON; SET WHAT_IF OFF;");
            Assert.False(eval.IsWhatIf);
        }

        // ── SET WEEK_START ────────────────────────────────────────────────────

        [Fact]
        public async Task SetWeekStart_SetsTheFirstDayOfTheWeek()
        {
            var eval = await RunAndGet("SET WEEK_START_DAY = 'SUNDAY';");
            Assert.Equal(DayOfWeek.Sunday, eval.WeekStartDay);
            await eval.Evaluate(Parse("SET WEEK_START_DAY = 'MONDAY';"));
            Assert.Equal(DayOfWeek.Monday, eval.WeekStartDay);
        }

        // ── eng.columns ───────────────────────────────────────────────────────

        [Fact]
        public async Task EngColumns_AfterCreate_ReturnsColumns()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS Id, 'Alice' AS Name INTO #Emp;
                SELECT * FROM eng.columns WHERE table_name = '#Emp';
            "));
            Assert.NotNull(eval.LastResult);
            var cols = eval.LastResult!.Rows.Select(r => r["column_name"]?.ToString()).ToList();
            Assert.Equal(new[] { "Id", "Name" }, cols);
        }

        // ── SHOW TABLES ───────────────────────────────────────────────────────

        [Fact]
        public async Task ShowTables_ReturnsTableList()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                CREATE CONNECTION m AS MOCKDB();
                SELECT * FROM eng.tables WHERE connection_name = 'm';
            "));
            Assert.NotNull(eval.LastResult);
            var tables = eval.LastResult!.Rows.Select(r => r["table_name"]?.ToString()).ToList();
            Assert.Contains("Users", tables);
            Assert.Contains("Employee", tables);
        }

        // ── SHOW PROFILE ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowProfile_ProducesResult()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("SELECT 1 AS N INTO #T; SELECT * FROM eng.profile;"));
            Assert.NotNull(eval.LastResult);
            Assert.Contains("statement", eval.LastResult!.ColumnNames);
            Assert.Contains("rows_processed", eval.LastResult!.ColumnNames);
            Assert.Contains("duration_ms", eval.LastResult!.ColumnNames);
        }

        /// <summary>
        /// A run's log marks its last measured statement as the one that failed. The statement
        /// that throws must therefore be measured, or the log blames the one before it.
        /// </summary>
        [Theory]
        [InlineData("SELECT 1 AS N INTO #T; ASSERT 1 = 0, 'boom';")]
        [InlineData("IF 1 = 1 BEGIN SELECT 1 AS N INTO #T; ASSERT 1 = 0, 'boom'; END")]
        public async Task Profiling_MeasuresTheStatementThatFailed_AsTheLastOne(string sql)
        {
            var eval = Eval();
            eval.Telemetry.IsProfiling = true;

            await Assert.ThrowsAnyAsync<Exception>(() => eval.Evaluate(Parse(sql)));

            var failed = ETL_SQL.Core.Profiling.StatementMetricsPayload
                .FromRun(eval.Telemetry.ProfileMetrics, runFailed: true)
                .Single(statement => statement.Failed);
            Assert.StartsWith("ASSERT", failed.Statement, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ShowProfile_IncludesPlanDecisionSummary()
        {
            var eval = Eval();
            eval.Telemetry.ProfileMetrics.Add(new ExecutionMetrics
            {
                Sql = "SELECT UPPER(Name) FROM #t",
                DurationMs = 12,
                RowsProcessed = 3
            });
            eval.Telemetry.RecordPlanDecision(new PlanDecision(
                QueryId: "q1",
                OperatorId: "select.projection",
                CandidatePath: "ColumnarProjection",
                Outcome: PlanDecisionOutcome.Fallback,
                ReasonCode: PlanDecisionReasonCodes.UnsupportedExpression,
                Message: "Columnar projection candidate replayed through the row streaming path.",
                Attributes: new Dictionary<string, string>()));
            eval.Telemetry.RecordPlanDecision(new PlanDecision(
                QueryId: "q1",
                OperatorId: "select.aggregate",
                CandidatePath: "ColumnarAggregate",
                Outcome: PlanDecisionOutcome.Accepted,
                ReasonCode: PlanDecisionReasonCodes.SemanticGuard,
                Message: "Columnar aggregate path accepted.",
                Attributes: new Dictionary<string, string>()));

            var dataSource = new ETL_SQL.Engine.Storage.ProfileDataSource(eval);
            DataTable? table = null;
            await foreach (var batch in dataSource.ReadBatches())
            {
                table = batch;
            }

            Assert.NotNull(table);
            var row = Assert.Single(table.Rows);
            Assert.Equal(2, Convert.ToInt32(row["plan_decisions"]));
            Assert.Equal(1, Convert.ToInt32(row["plan_accepted"]));
            Assert.Equal(1, Convert.ToInt32(row["plan_fallbacks"]));
            Assert.Equal("ColumnarProjection:UnsupportedExpression=1", row["plan_fallback_summary"]);
        }

        // ── SET PROFILING ─────────────────────────────────────────────────────

        [Fact]
        public async Task SetProfiling_On_EnablesProfiling()
        {
            var eval = await RunAndGet("SET PROFILING OFF; SET PROFILING ON;");
            Assert.True(eval.IsProfiling);
        }

        [Fact]
        public async Task SetProfiling_Off_DisablesProfiling()
        {
            var eval = await RunAndGet("SET PROFILING OFF;");
            Assert.False(eval.IsProfiling);
        }

        // ── SHOW JOB HISTORY ──────────────────────────────────────────────────

        [Fact]
        public async Task JobHistory_HasAStatusColumn()
        {
            var eval = await RunAndGet("SELECT * FROM eng.job_history;");
            Assert.Contains("status", eval.LastResult!.ColumnNames);
        }

        // ── eng.tags script metadata ──────────────────────────────────────────

        [Fact]
        public async Task EngTagsScriptScope_ProducesResult()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("/* @owner: Platform */ SELECT tag_name, tag_value FROM eng.tags WHERE scope = 'script';"));
            Assert.NotNull(eval.LastResult);
            Assert.Contains(eval.LastResult!.Rows, r =>
                r["tag_name"]?.ToString() == "owner" && r["tag_value"]?.ToString() == "Platform");
        }

        // ── USE PASSWORD ──────────────────────────────────────────────────────

        [Fact]
        public async Task UsePassword_SetsActivePassword()
        {
            var eval = await RunAndGet("USE PASSWORD = 'mypassword';");
            Assert.Equal("mypassword", eval.ScriptPassword);
        }

        [Fact]
        public async Task UsePasswordPrompt_UsesProvidedMasterPassword()
        {
            var eval = Eval();
            eval.MasterPassword = "prompt-password";

            await eval.Evaluate(Parse("USE PASSWORD PROMPT;"));

            Assert.Equal("prompt-password", eval.ScriptPassword);
        }

        // ── FILE_EXISTS / DIRECTORY_EXISTS functions ──────────────────────────

        [Fact]
        public async Task FileExists_ExistingFile_ReturnsTrue()
        {
            var f = Path.GetTempFileName();
            try
            {
                var eval = Eval();
                await eval.Evaluate(Parse($"DECLARE @r BIT = FILE_EXISTS('{f.Replace("\\", "\\\\")}');"));
                Assert.Equal(true, eval.GetVariable("@r"));
            }
            finally
            {
                File.Delete(f);
            }
        }

        [Fact]
        public async Task FileExists_NonExistentFile_ReturnsFalse()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @r BIT = FILE_EXISTS('/nonexistent/path/xyz.txt');"));
            Assert.Equal(false, eval.GetVariable("@r"));
        }

        [Fact]
        public async Task DirectoryExists_TempDir_ReturnsTrue()
        {
            var dir = Path.GetTempPath().Replace("\\", "\\\\");
            var eval = Eval();
            await eval.Evaluate(Parse($"DECLARE @r BIT = DIRECTORY_EXISTS('{dir}');"));
            Assert.Equal(true, eval.GetVariable("@r"));
        }
    }
}
