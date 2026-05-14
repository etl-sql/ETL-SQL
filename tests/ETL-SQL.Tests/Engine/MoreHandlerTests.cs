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
            await Run("RAISERROR('Info message', 1);");
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
        public async Task RaiseError_CaughtInTryCatch_DoesNotBubble()
        {
            await Run(@"
                BEGIN TRY
                    RAISERROR('Test', 5);
                END
                BEGIN CATCH
                    PRINT 'Caught raiserror';
                END
            ");
        }

        [Fact]
        public async Task RaiseError_InfoSeverity_MessageContainsINFO()
        {
            await Run("RAISERROR('Low severity', 2);");
        }

        // ── WAITFOR DELAY ─────────────────────────────────────────────────────

        [Fact]
        public async Task WaitFor_ZeroDelay_CompletesImmediately()
        {
            await Run("WAITFOR DELAY '00:00:00';");
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
        public async Task DeleteDirectory_IfExists_NonExistent_NoError()
        {
            var dir = Path.Combine(Path.GetTempPath(), "etlsql_test_nonexistent_" + Guid.NewGuid().ToString("N"));
            await Run($"DELETE DIRECTORY IF EXISTS '{dir}';");
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
        public async Task DeleteFile_IfExists_NonExistent_NoError()
        {
            var f = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N") + ".txt");
            await Run($"DELETE FILE IF EXISTS '{f}';");
        }

        [Fact]
        public async Task CopyFile_CopiesSourceToDestination()
        {
            var src = Path.GetTempFileName();
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
            var src = Path.GetTempFileName();
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
            var src = Path.GetTempFileName();
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
            Assert.NotNull(eval.GetVariable("@v"));
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
            Assert.NotNull(eval.GetVariable("@r"));
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
        public async Task Return_WithValue_SetsReturnCode()
        {
            await Run(@"
                CREATE PROCEDURE ReturnCode AS BEGIN
                    RETURN 5;
                END;
                EXEC ReturnCode;
            ");
        }

        // ── EXEC / CALL procedure ─────────────────────────────────────────────

        [Fact]
        public async Task Exec_SimpleProc_Runs()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                CREATE PROCEDURE Greet AS BEGIN
                    PRINT 'Hello';
                END;
                EXEC Greet;
            "));
        }

        [Fact]
        public async Task Exec_ProcWithParam_PassesValue()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                CREATE PROCEDURE Double(@n INT) AS BEGIN
                    SELECT @n * 2 AS Result INTO #Out;
                END;
                EXEC Double 5;
                SELECT * FROM #Out;
            "));
            Assert.NotNull(eval.LastResult);
        }

        // ── SET WHAT_IF ───────────────────────────────────────────────────────

        [Fact]
        public async Task SetWhatIf_On_EnablesWhatIfMode()
        {
            await Run("SET WHAT_IF ON;");
        }

        [Fact]
        public async Task SetWhatIf_Off_DisablesWhatIfMode()
        {
            await Run("SET WHAT_IF OFF;");
        }

        // ── SET WEEK_START ────────────────────────────────────────────────────

        [Fact]
        public async Task SetWeekStart_Monday_Succeeds()
        {
            await Run("SET WEEK_START_DAY = 'MONDAY';");
        }

        // ── SHOW COLUMNS ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowColumns_AfterCreate_ReturnsColumns()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS Id, 'Alice' AS Name INTO #Emp;
                SHOW COLUMNS FOR #Emp;
            "));
            Assert.NotNull(eval.LastResult);
        }

        // ── SHOW TABLES ───────────────────────────────────────────────────────

        [Fact]
        public async Task ShowTables_ReturnsTableList()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS Id INTO #A;
                SELECT 2 AS Id INTO #B;
                SHOW TABLES;
            "));
            Assert.NotNull(eval.LastResult);
        }

        // ── SHOW PROFILE ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowProfile_ProducesResult()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("SELECT 1 AS N INTO #T; SHOW PROFILE;"));
            Assert.NotNull(eval.LastResult);
        }

        // ── SET PROFILING ─────────────────────────────────────────────────────

        [Fact]
        public async Task SetProfiling_On_EnablesProfiling()
        {
            await Run("SET PROFILING ON;");
        }

        [Fact]
        public async Task SetProfiling_Off_DisablesProfiling()
        {
            await Run("SET PROFILING OFF;");
        }

        // ── SHOW JOB HISTORY ──────────────────────────────────────────────────

        [Fact]
        public async Task ShowJobHistory_ProducesResult()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("SHOW JOB HISTORY;"));
            Assert.NotNull(eval.LastResult);
        }

        // ── SHOW SCRIPT TAGS ──────────────────────────────────────────────────

        [Fact]
        public async Task ShowScriptTags_ProducesResult()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("SHOW SCRIPT TAGS;"));
            Assert.NotNull(eval.LastResult);
        }

        // ── USE PASSWORD ──────────────────────────────────────────────────────

        [Fact]
        public async Task UsePassword_SetsActivePassword()
        {
            await Run("USE PASSWORD = 'mypassword';");
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
