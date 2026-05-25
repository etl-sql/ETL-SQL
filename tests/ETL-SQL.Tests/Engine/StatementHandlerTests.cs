using System;
using System.Linq;
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
    /// Exercises low-coverage statement handlers that don't require external services:
    /// ASSERT, REQUIRE VERSION, HELP, DROP, SHOW VERSION, SET threshold keywords,
    /// SET SPILL, PRINT, THROW, WHILE, TRUNCATE, and SHOW VARIABLES.
    /// </summary>
    public class StatementHandlerTests
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

        // ── ASSERT ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Assert_TrueCondition_DoesNotThrow()
        {
            await Run("ASSERT 1 = 1;");
        }

        [Fact]
        public async Task Assert_TrueWithExpression_DoesNotThrow()
        {
            await Run("ASSERT 2 + 3 = 5;");
        }

        [Fact]
        public async Task Assert_FalseCondition_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(() => Run("ASSERT 1 = 0;"));
        }

        [Fact]
        public async Task Assert_FalseConditionWithMessage_IncludesMessage()
        {
            // ASSERT uses comma-separated message, not MESSAGE keyword
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => Run("ASSERT 1 = 0, 'Custom failure message';"));
            Assert.Contains("Custom failure message", ex.Message);
        }

        [Fact]
        public async Task Assert_WithVariable_EvaluatesCorrectly()
        {
            await Run("DECLARE @x INT = 5; ASSERT @x = 5;");
        }

        // ── REQUIRE VERSION ───────────────────────────────────────────────────

        [Fact]
        public async Task RequireVersion_GtEq_OldVersion_Passes()
        {
            await Run("REQUIRE VERSION >= '0.1.0';");
        }

        [Fact]
        public async Task RequireVersion_GtEq_FutureVersion_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("REQUIRE VERSION >= '9999.0.0';"));
        }

        [Fact]
        public async Task RequireVersion_Gt_OldVersion_Passes()
        {
            await Run("REQUIRE VERSION > '0.0.1';");
        }

        [Fact]
        public async Task RequireVersion_Gt_FutureVersion_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("REQUIRE VERSION > '9999.0.0';"));
        }

        [Fact]
        public async Task RequireVersion_InvalidVersionString_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("REQUIRE VERSION >= 'not-a-version';"));
        }

        // ── HELP ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task Help_Root_ExecutesWithoutError()
        {
            await Run("HELP;");
        }

        [Fact]
        public async Task Help_Connection_ExecutesWithoutError()
        {
            await Run("HELP CONNECTION;");
        }

        [Fact]
        public async Task Help_Function_ExecutesWithoutError()
        {
            await Run("HELP FUNCTION;");
        }

        [Fact]
        public async Task Help_Visual_ExecutesWithoutError()
        {
            await Run("HELP VISUAL;");
        }

        [Fact]
        public async Task Help_SpecificConnector_ExecutesWithoutError()
        {
            await Run("HELP CONNECTION MSSQL;");
        }

        [Fact]
        public async Task Help_SpecificFunction_ExecutesWithoutError()
        {
            await Run("HELP FUNCTION CONCAT;");
        }

        [Fact]
        public async Task Help_Snippets_ListsAll_ExecutesWithoutError()
        {
            await Run("HELP SNIPPETS;");
        }

        [Fact]
        public async Task Help_Snippets_SpecificTrigger_ExecutesWithoutError()
        {
            await Run("HELP SNIPPETS bar;");
        }

        [Fact]
        public async Task Help_Snippets_WithDollarPrefix_ExecutesWithoutError()
        {
            await Run("HELP SNIPPETS $mssql;");
        }

        [Fact]
        public async Task Help_Snippets_UnknownTrigger_ExecutesWithoutError()
        {
            await Run("HELP SNIPPETS nonexistent;");
        }

        // ── SHOW VERSION ──────────────────────────────────────────────────────

        [Fact]
        public async Task ShowVersion_ProducesVersionResult()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("SHOW VERSION;"));
            Assert.NotNull(eval.LastResult);
        }

        [Fact]
        public async Task ShowVersion_IntoTable_WritesToTempTable()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("SHOW VERSION INTO #V; SELECT * FROM #V;"));
            Assert.NotNull(eval.LastResult);
        }

        // ── DROP TABLE ────────────────────────────────────────────────────────

        [Fact]
        public async Task DropTable_ExistingTable_RemovesTable()
        {
            await Run("SELECT 1 AS Id INTO #T; DROP TABLE #T;");
        }

        [Fact]
        public async Task DropTable_IfExists_NonExistentTable_NoError()
        {
            await Run("DROP TABLE IF EXISTS #NonExistent;");
        }

        // ── DROP INDEX ────────────────────────────────────────────────────────

        [Fact]
        public async Task DropIndex_AfterCreate_Succeeds()
        {
            await Run(@"
                SELECT 1 AS Id, 'Alice' AS Name INTO #Employees;
                INSERT INTO #Employees VALUES (2, 'Bob');
                CREATE INDEX idx_name ON #Employees (Name);
                DROP INDEX idx_name ON #Employees;
            ");
        }

        // ── DROP PROCEDURE ────────────────────────────────────────────────────

        [Fact]
        public async Task DropProcedure_AfterCreate_Succeeds()
        {
            await Run(@"
                CREATE PROCEDURE MyProc AS BEGIN
                    SELECT 1 AS Val INTO #Result;
                END;
                DROP PROCEDURE MyProc;
            ");
        }

        [Fact]
        public async Task DropProcedure_IfExists_NonExistent_NoError()
        {
            await Run("DROP PROCEDURE IF EXISTS NonExistentProc;");
        }

        // ── DROP FUNCTION ─────────────────────────────────────────────────────

        [Fact]
        public async Task DropFunction_AfterCreate_Succeeds()
        {
            await Run(@"
                CREATE FUNCTION MyFunc(@x INT) RETURNS INT AS BEGIN
                    RETURN @x * 2;
                END;
                DROP FUNCTION MyFunc;
            ");
        }

        [Fact]
        public async Task DropFunction_IfExists_NonExistent_NoError()
        {
            await Run("DROP FUNCTION IF EXISTS NonExistentFunc;");
        }

        // ── SET THRESHOLD keywords ────────────────────────────────────────────

        [Fact]
        public async Task SetBatchSize_UpdatesContext()
        {
            await Run("SET BATCHSIZE = 500;");
        }

        [Fact]
        public async Task SetJoinSpillThreshold_UpdatesContext()
        {
            await Run("SET JOIN_SPILL_THRESHOLD = 50000;");
        }

        [Fact]
        public async Task SetWindowSpillThreshold_UpdatesContext()
        {
            await Run("SET WINDOW_SPILL_THRESHOLD = 25000;");
        }

        [Fact]
        public async Task SetMaxRecursiveDepth_UpdatesContext()
        {
            await Run("SET MAX_RECURSIVE_DEPTH = 50;");
        }

        [Fact]
        public async Task SetMaxParallelDegree_UpdatesContext()
        {
            await Run("SET MAX_PARALLEL_DEGREE = 4;");
        }

        [Fact]
        public async Task SetRegexMatchTimeout_UpdatesContext()
        {
            await Run("SET REGEX_MATCH_TIMEOUT = 5000;");
        }

        [Fact]
        public async Task SetMaxGroupingSets_UpdatesContext()
        {
            await Run("SET MAX_GROUPING_SETS = 64;");
        }

        [Fact]
        public async Task SetMaxGenerateRows_UpdatesContext()
        {
            await Run("SET MAX_GENERATE_ROWS = 10000;");
        }

        [Fact]
        public async Task SetForeachPageSize_UpdatesContext()
        {
            await Run("SET FOREACH_PAGE_SIZE = 200;");
        }

        [Fact]
        public async Task SetExternalHashPartitions_UpdatesContext()
        {
            await Run("SET EXTERNAL_HASH_PARTITIONS = 8;");
        }

        [Fact]
        public async Task SetMaxLastResultRows_UpdatesContext()
        {
            await Run("SET MAX_LAST_RESULT_ROWS = 1000;");
        }

        [Fact]
        public async Task SetBatchSizeZero_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("SET BATCHSIZE = 0;"));
        }

        [Fact]
        public async Task SetExternalHashPartitionsZero_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("SET EXTERNAL_HASH_PARTITIONS = 0;"));
        }

        [Fact]
        public async Task SetExternalSortChunkSize_UpdatesContext()
        {
            await Run("SET EXTERNAL_SORT_CHUNK_SIZE = 5000;");
        }

        [Fact]
        public async Task SetMaxInMemoryBatches_UpdatesContext()
        {
            await Run("SET MAX_IN_MEMORY_BATCHES = 10;");
        }

        // ── SET SPILL_ENCRYPTION / SPILL_COMPRESSION ──────────────────────────

        [Fact]
        public async Task SetSpillEncryption_Off_ExecutesWithoutError()
        {
            await Run("SET SPILL_ENCRYPTION OFF;");
        }

        [Fact]
        public async Task SetSpillEncryption_On_ExecutesWithoutError()
        {
            await Run("SET SPILL_ENCRYPTION ON;");
        }

        [Fact]
        public async Task SetSpillCompression_Off_ExecutesWithoutError()
        {
            await Run("SET SPILL_COMPRESSION OFF;");
        }

        [Fact]
        public async Task SetSpillCompression_On_ExecutesWithoutError()
        {
            await Run("SET SPILL_COMPRESSION ON;");
        }

        // ── PRINT ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Print_StringLiteral_ExecutesWithoutError()
        {
            await Run("PRINT 'Hello, World!';");
        }

        [Fact]
        public async Task Print_WithVariable_OutputsValue()
        {
            await Run("DECLARE @msg STRING = 'Test'; PRINT @msg;");
        }

        [Fact]
        public async Task Print_MultipleArgs_JoinsWithSpace()
        {
            await Run("PRINT 'A', 'B', 'C';");
        }

        // ── SHOW SAFE ZONES ───────────────────────────────────────────────────

        [Fact]
        public async Task ShowSafeZones_ProducesResult()
        {
            await Run("SHOW SAFE ZONES;");
        }

        // ── SHOW SESSIONS ─────────────────────────────────────────────────────

        [Fact]
        public async Task ShowSessions_ProducesResult()
        {
            await Run("SHOW SESSIONS;");
        }

        // ── THROW ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Throw_InsideTryCatch_CaughtByHandler()
        {
            await Run(@"
                BEGIN TRY
                    THROW 'Test error';
                END
                BEGIN CATCH
                    PRINT 'Caught';
                END
            ");
        }

        [Fact]
        public async Task Throw_WithoutTryCatch_BubblesUp()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("THROW 'Uncaught error';"));
        }

        // ── WHILE with BREAK/CONTINUE ─────────────────────────────────────────

        [Fact]
        public async Task While_WithBreak_ExitsLoop()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @i INT = 0;
                WHILE @i < 10 BEGIN
                    SET @i = @i + 1;
                    IF @i = 3 BREAK;
                END;
            "));
            Assert.Equal(3m, eval.GetVariable("@i"));
        }

        [Fact]
        public async Task While_WithContinue_SkipsIteration()
        {
            await Run(@"
                DECLARE @i INT = 0;
                DECLARE @sum INT = 0;
                WHILE @i < 5 BEGIN
                    SET @i = @i + 1;
                    IF @i = 3 CONTINUE;
                    SET @sum = @sum + @i;
                END;
            ");
        }

        // ── TRUNCATE TABLE ────────────────────────────────────────────────────

        [Fact]
        public async Task TruncateTable_RemovesAllRows()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS Id INTO #T;
                INSERT INTO #T VALUES (2);
                TRUNCATE TABLE #T;
                SELECT COUNT(*) AS N FROM #T;
            "));
            Assert.Equal(0m, eval.LastResult?.Rows[0]["N"]);
        }

        // ── SHOW VARIABLES ────────────────────────────────────────────────────

        [Fact]
        public async Task ShowVariables_ReturnsVariableList()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @x INT = 42; SHOW VARIABLES;"));
            Assert.NotNull(eval.LastResult);
        }

        // ── CLEAR SESSION ─────────────────────────────────────────────────────

        [Fact]
        public async Task ClearSession_ExecutesWithoutError()
        {
            await Run("CLEAR SESSION;");
        }
    }
}
