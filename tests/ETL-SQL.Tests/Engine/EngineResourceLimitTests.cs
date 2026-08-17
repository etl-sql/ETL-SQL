using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ETL_SQL.Tests
{
    public class EngineResourceLimitTests
    {
        private Mock<ILogger> _loggerMock = new();
        private Mock<IConnectorRegistry> _connectors = new();
        private Mock<IServiceProvider> _services = new();
        private SecurityService _security;

        public EngineResourceLimitTests()
        {
            _security = new SecurityService(_loggerMock.Object);
            _security.IsTestMode = true;
        }

        [Fact]
        public async Task ParallelExecution_ShouldPreserveSubmissionOrder()
        {
            // Arrange
            var logger = new TestLogger();
            var evaluator = CreateEvaluator(logger);

            // We'll execute a parallel block with 5 prints. Each print inside its own fork
            // should be merged back in order.
            string sql = @"
PARALLEL BEGIN
    PRINT 'Task 1';
    PRINT 'Task 2';
    PRINT 'Task 3';
    PRINT 'Task 4';
    PRINT 'Task 5';
END";
            var script = Parse(sql);

            // Act
            await evaluator.Evaluate(script);

            // Assert
            var prints = evaluator.Messages.Where(l => l.Message.StartsWith("Task ")).Select(l => l.Message).ToList();
            Assert.Equal(5, prints.Count);
            Assert.Equal("Task 1", prints[0]);
            Assert.Equal("Task 2", prints[1]);
            Assert.Equal("Task 3", prints[2]);
            Assert.Equal("Task 4", prints[3]);
            Assert.Equal("Task 5", prints[4]);
        }

        [Fact]
        public async Task AggregateCube_ShouldRespectMaxGroupingSets()
        {
            // Arrange
            var evaluator = CreateEvaluator();
            await evaluator.Evaluate(Parse("CREATE TABLE #t (a INT, b INT, c INT, d INT, e INT)"));

            // CUBE(a,b,c,d,e) = 2^5 = 32 sets.
            // Set limit to 10.
            await evaluator.Evaluate(Parse("SET MAX_GROUPING_SETS = 10"));

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                evaluator.Evaluate(Parse("SELECT a, b, SUM(c) FROM #t GROUP BY CUBE(a,b,c,d,e)"))
            );
            Assert.Contains("exceeds the maximum grouping sets limit", ex.Message);

            // Increase limit and try again
            await evaluator.Evaluate(Parse("SET MAX_GROUPING_SETS = 100"));
            await evaluator.Evaluate(Parse("SELECT a, b, SUM(c) FROM #t GROUP BY CUBE(a,b,c,d,e)")); // Should succeed
        }

        [Fact]
        public async Task SelectResult_ShouldFlagAsCapped_WhenLimitExceeded()
        {
            // Arrange
            var evaluator = CreateEvaluator();
            evaluator.MaxLastResultRows = 50000;

            // Generate 50005 rows - this triggers the 50k cap.
            // Using a more efficient way if possible, but the engine is fast for #temp table inserts.
            string sql = @"
DECLARE @i INT = 0;
CREATE TABLE #large (v INT);
WHILE @i < 50005
BEGIN
    INSERT INTO #large VALUES (@i);
    SET @i = @i + 1;
END
SELECT * FROM #large;";
            var script = Parse(sql);

            // Act
            await evaluator.Evaluate(script);

            // Assert
            Assert.NotNull(evaluator.LastResult);
            Assert.True(evaluator.LastResult.IsCapped);
            Assert.Equal(50000, evaluator.LastResult.Rows.Count);
        }

        private Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        [Fact]
        public async Task RowCeiling_AbortsTheExecutionAndNamesTheLimit()
        {
            var evaluator = CreateEvaluator();
            evaluator.MaxRowsProcessed = 3;
            var script = Parse(@"
CREATE TABLE #Rows (Id INT);
INSERT INTO #Rows VALUES (1), (2), (3), (4), (5);");

            var error = await Assert.ThrowsAnyAsync<Exception>(() => evaluator.Evaluate(script));

            Assert.Contains("row limit", Flatten(error), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("3", Flatten(error));
        }

        [Fact]
        public async Task RowCeilingOfZeroLeavesExecutionUnlimited()
        {
            var evaluator = CreateEvaluator();
            evaluator.MaxRowsProcessed = 0;
            var script = Parse(@"
CREATE TABLE #Rows (Id INT);
INSERT INTO #Rows VALUES (1), (2), (3), (4), (5);");

            await evaluator.Evaluate(script);

            Assert.Equal(5, evaluator.Telemetry.RowsProcessed);
        }

        [Fact]
        public void RowCeilingIsEnforcedWhereEveryHandlerAccumulates()
        {
            // Handlers all reach rows through this one property, so the ceiling cannot be missing
            // from a handler somebody adds later. Resets and bookkeeping must still pass through.
            var telemetry = new ExecutionTelemetryManager { MaxRowsProcessed = 10 };

            telemetry.RowsProcessed += 10;
            Assert.Equal(10, telemetry.RowsProcessed);
            telemetry.RowsProcessed = 4;
            Assert.Equal(4, telemetry.RowsProcessed);

            var breach = Assert.Throws<ExecutionException>(() => telemetry.RowsProcessed += 7);
            Assert.Contains("row limit", breach.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static string Flatten(Exception error)
        {
            var text = new System.Text.StringBuilder();
            for (var current = error; current is not null; current = current.InnerException)
                text.Append(current.Message).Append(' ');
            return text.ToString();
        }

        private Evaluator CreateEvaluator(ILogger? logger = null)
        {
            var l = logger ?? new TestLogger();
            var registry = new Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            var tracker = new Mock<ILineageTracker>();
            tracker.Setup(t => t.GlobalMetadata).Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var docker = new Mock<IDockerManager>();
            var sessions = new Mock<SessionStateManager>(l, _security, new Mock<IConfiguration>().Object, new ETL_SQL.Core.Execution.SqliteSessionMetadataStoreFactory(), null);
            var pushdown = new Mock<ExecutePushdownStatementHandler>(l);

            var handlers = new List<IStatementHandler>
            {
                new SelectStatementHandler(l),
                new InsertStatementHandler(l, pushdown.Object),
                new UpdateStatementHandler(l),
                new SetVariableStatementHandler(l),
                new DeclareStatementHandler(l),
                new WhileStatementHandler(l),
                new ParallelStatementHandler(),
                new PrintStatementHandler(l),
                new SetThresholdStatementHandler(),
                new CreateTableStatementHandler(l),
                new BlockStatementHandler()
            };

            _services.Setup(s => s.GetService(typeof(IEnumerable<IStatementHandler>))).Returns(handlers);

            return new Evaluator(handlers, _services.Object, registry.Object, tracker.Object, docker.Object, _connectors.Object, sessions.Object, _security, l, new ETL_SQL.Core.Metadata.LanguageHelpRegistry(), new EvaluatorComponentRegistry());
        }

        private class TestLogger : ILogger
        {
            public List<string> Lines { get; } = new();
            public bool IsVerbose { get; set; }
            public string? SessionId { get; set; }
            public bool IsDebugEnabled => true;
            public bool IsVerboseEnabled => IsVerbose;
            public bool SuppressConsole { get; set; }
            public bool IsJsonMode { get; set; }
            public event Action<string, string?, ConsoleColor>? OnMessage;

            public void Log(LogLevel level, string message, Exception? ex = null)
            {
                System.Console.WriteLine($"LOGGER: {message}");
                Lines.Add(message);
                OnMessage?.Invoke(message, SessionId, ConsoleColor.White);
            }

            public void Debug(string message) => Log(LogLevel.Debug, message);
            public void Info(string message) => Log(LogLevel.Info, message);
            public void Warning(string message) => Log(LogLevel.Warning, message);
            public void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
            public void WriteLine(string message, ConsoleColor color = ConsoleColor.White) => Log(LogLevel.Info, message);

            public void Debug(string template, params object?[] args) => Log(LogLevel.Debug, template);
            public void Info(string template, params object?[] args) => Log(LogLevel.Info, template);
            public void Warning(string template, params object?[] args) => Log(LogLevel.Warning, template);
            public void Error(string template, Exception? ex, params object?[] args) => Log(LogLevel.Error, template, ex);
        }
    }
}
