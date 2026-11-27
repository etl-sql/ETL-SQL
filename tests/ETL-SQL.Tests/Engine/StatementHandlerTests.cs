using System;
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

        private static async Task<Evaluator> RunEval(string sql)
        {
            var eval = Eval();
            eval.RedirectOutput = true;
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        private static async Task<string> Output(string sql)
        {
            var eval = await RunEval(sql);
            return string.Join("\n", eval.Messages.Select(m => m.Message));
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
        public async Task RequireVersion_LtEq_FutureVersion_Passes()
        {
            await Run("REQUIRE VERSION <= '9999.0.0';");
        }

        [Fact]
        public async Task RequireVersion_LtEq_OldVersion_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("REQUIRE VERSION <= '0.0.1';"));
        }

        [Fact]
        public async Task RequireVersion_Lt_FutureVersion_Passes()
        {
            await Run("REQUIRE VERSION < '9999.0.0';");
        }

        [Fact]
        public async Task RequireVersion_Lt_OldVersion_Throws()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("REQUIRE VERSION < '0.0.1';"));
        }

        [Fact]
        public async Task RequireVersion_InvalidVersionString_ThrowsExecutionException()
        {
            await Assert.ThrowsAsync<ExecutionException>(
                () => Run("REQUIRE VERSION >= 'not-a-version';"));
        }

        // ── HELP ──────────────────────────────────────────────────────────────

        [Fact]
        public async Task Help_Root_PrintsTopic()
        {
            var output = await Output("HELP;");
            Assert.Contains("ETL-SQL Help System", output);
            Assert.Contains("HELP SNIPPETS", output);
        }

        [Fact]
        public async Task Help_Connection_PrintsTopic()
        {
            var output = await Output("HELP CONNECTION;");
            Assert.Contains("MSSQL", output);
            Assert.DoesNotContain("List of connection types not found.", output);
        }

        [Fact]
        public async Task Help_Function_PrintsTopic()
        {
            var output = await Output("HELP FUNCTION;");
            Assert.Contains("Available Functions:", output);
            Assert.Contains("CONCAT", output);
        }

        [Fact]
        public async Task Help_Visual_PrintsTopic()
        {
            var output = await Output("HELP VISUAL;");
            Assert.Contains("CREATE VISUAL", output);
        }

        [Fact]
        public async Task Help_SpecificConnector_PrintsTopic()
        {
            var output = await Output("HELP CONNECTION MSSQL;");
            Assert.Contains("HELP: CONNECTION MSSQL", output);
            Assert.Contains("TRUSTED_CONNECTION", output);
        }

        [Fact]
        public async Task Help_SpecificFunction_PrintsTopic()
        {
            var output = await Output("HELP FUNCTION CONCAT;");
            Assert.Contains("Concatenates multiple strings", output);
        }

        [Fact]
        public async Task Help_Snippets_ListsAll_PrintsTopic()
        {
            var output = await Output("HELP SNIPPETS;");
            Assert.Contains("$bar", output);
            Assert.Contains("$mssql", output);
        }

        [Fact]
        public async Task Help_Snippets_SpecificTrigger_PrintsTopic()
        {
            var output = await Output("HELP SNIPPETS bar;");
            Assert.Contains("HELP: SNIPPET $bar", output);
            Assert.Contains("AS BAR", output);
        }

        [Fact]
        public async Task Help_Snippets_WithDollarPrefix_PrintsTopic()
        {
            var output = await Output("HELP SNIPPETS $mssql;");
            Assert.Contains("HELP: SNIPPET $mssql", output);
            Assert.Contains("AS MSSQL", output);
        }

        [Fact]
        public async Task Help_Snippets_UnknownTrigger_PrintsTopic()
        {
            var output = await Output("HELP SNIPPETS nonexistent;");
            Assert.Contains("Snippet '$nonexistent' not found.", output);
        }

        // ── eng.version ───────────────────────────────────────────────────────

        [Fact]
        public async Task ShowVersion_ProducesVersionResult()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("SELECT * FROM eng.version;"));
            Assert.Equal(new[] { "component", "version", "metadata" }, eval.LastResult!.ColumnNames);
            Assert.NotEmpty(eval.LastResult.Rows);
        }

        [Fact]
        public async Task ShowVersion_IntoTable_WritesToTempTable()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("SELECT * INTO #V FROM eng.version; SELECT * FROM #V;"));
            Assert.Contains("version", eval.LastResult!.ColumnNames);
            Assert.NotEmpty(eval.LastResult.Rows);
        }

        // ── DROP TABLE ────────────────────────────────────────────────────────

        [Fact]
        public async Task DropTable_ExistingTable_RemovesTable()
        {
            var eval = await RunEval("SELECT 1 AS Id INTO #T; DROP TABLE #T;");
            await Assert.ThrowsAnyAsync<Exception>(() => eval.Evaluate(Parse("SELECT * FROM #T;")));
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
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                SELECT 1 AS Id, 'Alice' AS Name INTO #Employees;
                INSERT INTO #Employees VALUES (2, 'Bob');
                CREATE INDEX idx_name ON #Employees (Name);
                DROP INDEX idx_name ON #Employees;
            "));
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => eval.Evaluate(Parse("DROP INDEX idx_name ON #Employees;")));
            Assert.Contains("Unknown index", ex.Message);
        }

        // ── DROP PROCEDURE ────────────────────────────────────────────────────

        [Fact]
        public async Task DropProcedure_AfterCreate_Succeeds()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                CREATE PROCEDURE MyProc AS BEGIN
                    SELECT 1 AS Val INTO #Result;
                END;
                DROP PROCEDURE MyProc;
            "));
            Assert.False(eval.TryGetProcedure("MyProc", out _));
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
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                CREATE FUNCTION MyFunc(@x INT) RETURNS INT AS BEGIN
                    RETURN @x * 2;
                END;
                DROP FUNCTION MyFunc;
            "));
            Assert.False(eval.TryGetFunction("MyFunc", out _));
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
            var eval = await RunEval("SET BATCHSIZE = 500;");
            Assert.Equal(500, eval.BatchSize);
        }

        [Fact]
        public async Task SetJoinSpillThreshold_UpdatesContext()
        {
            var eval = await RunEval("SET JOIN_SPILL_THRESHOLD = 50000;");
            Assert.Equal(50000, eval.JoinSpillThreshold);
        }

        [Fact]
        public async Task SetWindowSpillThreshold_UpdatesContext()
        {
            var eval = await RunEval("SET WINDOW_SPILL_THRESHOLD = 25000;");
            Assert.Equal(25000, eval.WindowSpillThreshold);
        }

        [Fact]
        public async Task SetMaxRecursiveDepth_UpdatesContext()
        {
            var eval = await RunEval("SET MAX_RECURSIVE_DEPTH = 50;");
            Assert.Equal(50, eval.MaxRecursiveDepth);
        }

        [Fact]
        public async Task SetMaxParallelDegree_UpdatesContext()
        {
            var eval = await RunEval("SET MAX_PARALLEL_DEGREE = 4;");
            Assert.Equal(4, eval.MaxParallelDegree);
        }

        [Fact]
        public async Task SetRegexMatchTimeout_UpdatesContext()
        {
            var eval = await RunEval("SET REGEX_MATCH_TIMEOUT = 5000;");
            Assert.Equal(5000, eval.RegexMatchTimeoutMs);
        }

        [Fact]
        public async Task SetMaxGroupingSets_UpdatesContext()
        {
            var eval = await RunEval("SET MAX_GROUPING_SETS = 64;");
            Assert.Equal(64, eval.MaxGroupingSets);
        }

        [Fact]
        public async Task SetMaxGenerateRows_UpdatesContext()
        {
            var eval = await RunEval("SET MAX_GENERATE_ROWS = 10000;");
            Assert.Equal(10000, eval.MaxGenerateRows);
        }

        [Fact]
        public async Task SetForeachPageSize_UpdatesContext()
        {
            var eval = await RunEval("SET FOREACH_PAGE_SIZE = 200;");
            Assert.Equal(200, eval.ForeachPageSize);
        }

        [Fact]
        public async Task SetExternalHashPartitions_UpdatesContext()
        {
            var eval = await RunEval("SET EXTERNAL_HASH_PARTITIONS = 8;");
            Assert.Equal(8, eval.ExternalHashPartitions);
        }

        [Fact]
        public async Task SetMaxLastResultRows_UpdatesContext()
        {
            var eval = await RunEval("SET MAX_LAST_RESULT_ROWS = 1000;");
            Assert.Equal(1000, eval.MaxLastResultRows);
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
            var eval = await RunEval("SET EXTERNAL_SORT_CHUNK_SIZE = 5000;");
            Assert.Equal(5000, eval.ExternalSortChunkSize);
        }

        [Fact]
        public async Task SetMaxInMemoryBatches_UpdatesContext()
        {
            var eval = await RunEval("SET MAX_IN_MEMORY_BATCHES = 10;");
            Assert.Equal(10, eval.MaxInMemoryBatches);
        }

        // ── SET SPILL_ENCRYPTION / SPILL_COMPRESSION ──────────────────────────

        [Fact]
        public async Task SetSpillEncryption_Off_DisablesEncryption()
        {
            var eval = await RunEval("SET SPILL_ENCRYPTION ON; SET SPILL_ENCRYPTION OFF;");
            Assert.False(eval.SpillEncryptionEnabled);
        }

        [Fact]
        public async Task SetSpillEncryption_On_EnablesEncryption()
        {
            var eval = await RunEval("SET SPILL_ENCRYPTION OFF; SET SPILL_ENCRYPTION ON;");
            Assert.True(eval.SpillEncryptionEnabled);
        }

        [Fact]
        public async Task SetSpillCompression_Off_DisablesCompression()
        {
            var eval = await RunEval("SET SPILL_ENCRYPTION ON; SET SPILL_COMPRESSION ON; SET SPILL_COMPRESSION OFF;");
            Assert.False(eval.SpillCompressionEnabled);
        }

        [Fact]
        public async Task SetSpillCompression_On_EnablesCompression()
        {
            var eval = await RunEval("SET SPILL_ENCRYPTION ON; SET SPILL_COMPRESSION OFF; SET SPILL_COMPRESSION ON;");
            Assert.True(eval.SpillCompressionEnabled);
        }

        // ── PRINT ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Print_StringLiteral_WritesMessage()
        {
            var eval = await RunEval("PRINT 'Hello, World!';");
            Assert.Contains(eval.Messages, m => m.Message.EndsWith("Hello, World!", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Print_WithVariable_OutputsValue()
        {
            var eval = await RunEval("DECLARE @msg STRING = 'Test'; PRINT @msg;");
            Assert.Contains(eval.Messages, m => m.Message.EndsWith("Test", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Print_MultipleArgs_JoinsWithSpace()
        {
            var eval = await RunEval("PRINT 'A', 'B', 'C';");
            Assert.Contains(eval.Messages, m => m.Message.EndsWith("A B C", StringComparison.Ordinal));
        }

        // ── eng.safe_zones / eng.sessions ──────────────────────────────────────

        [Fact]
        public async Task ShowSafeZones_ProducesResult()
        {
            var eval = await RunEval("SELECT * FROM eng.safe_zones;");
            Assert.Equal(new[] { "path", "is_system_path", "resolution" }, eval.LastResult!.ColumnNames);
        }

        [Fact]
        public async Task ShowSessions_ProducesResult()
        {
            var eval = await RunEval("SELECT * FROM eng.sessions;");
            Assert.Contains("session_id", eval.LastResult!.ColumnNames);
        }

        // ── THROW ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Throw_InsideTryCatch_CaughtByHandler()
        {
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @caught INT = 0;
                BEGIN TRY
                    THROW 'Test error';
                    SET @caught = 99;
                END
                BEGIN CATCH
                    SET @caught = 1;
                END
            "));
            Assert.Equal(1m, eval.GetVariable("@caught"));
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
            var eval = Eval();
            await eval.Evaluate(Parse(@"
                DECLARE @i INT = 0;
                DECLARE @sum INT = 0;
                WHILE @i < 5 BEGIN
                    SET @i = @i + 1;
                    IF @i = 3 CONTINUE;
                    SET @sum = @sum + @i;
                END;
            "));
            Assert.Equal(12m, eval.GetVariable("@sum"));
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

        // ── eng.variables ─────────────────────────────────────────────────────

        [Fact]
        public async Task ShowVariables_ReturnsVariableList()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @x INT = 42; SELECT * FROM eng.variables;"));
            var row = Assert.Single(eval.LastResult!.Rows, r => string.Equals(r["variable_name"]?.ToString(), "@x", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("42", row["value"]?.ToString());
        }

        // ── CLEAR SESSION ─────────────────────────────────────────────────────

        [Fact]
        public async Task ClearSession_DropsSessionVariables()
        {
            var eval = Eval();
            await eval.Evaluate(Parse("DECLARE @keep INT = 7;"));
            Assert.Equal(7m, eval.GetVariable("@keep"));
            await eval.Evaluate(Parse("CLEAR SESSION;"));
            Assert.Null(eval.GetVariable("@keep"));
        }
    }
}
