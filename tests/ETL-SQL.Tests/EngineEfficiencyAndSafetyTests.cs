using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
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
    public class EngineEfficiencyAndSafetyTests
    {
        private Mock<ILogger> _loggerMock = new();
        private Mock<IConnectorRegistry> _connectors = new();
        private Mock<IServiceProvider> _services = new();
        private SecurityService _security;
        private IConfiguration _config = new ConfigurationBuilder().Build();

        public EngineEfficiencyAndSafetyTests()
        {
            _security = new SecurityService(_loggerMock.Object);
            _security.IsTestMode = true;
        }

        [Fact]
        public async Task SetMaxSessionSize_ShouldUpdateContext()
        {
            // Arrange
            var evaluator = CreateEvaluator();

            // Act
            await evaluator.Evaluate(Parse("SET MAX_SESSION_SIZE = 104857600")); // 100MB

            // Assert
            Assert.Equal(104857600, evaluator.MaxSessionSize);
        }

        [Fact]
        public async Task SessionSizeLimit_ShouldThrowWhenExceeded()
        {
            // Arrange
            var logger = new TestLogger();
            var evaluator = CreateEvaluator(logger);

            // Set limit to a very small value (1KB)
            await evaluator.Evaluate(Parse("SET MAX_SESSION_SIZE = 1024"));

            // Directly declare a large variable to bypass mocked function registry
            evaluator.DeclareVariable("largeVar", new string('A', 2000));

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var sessions = new SessionStateManager(logger, _security, _config, new SqliteSessionMetadataStoreFactory(), tempDir);

                // Act & Assert
                var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                    sessions.SaveSession("test-session", evaluator)
                );

                Assert.Contains("exceeds the safety limit", ex.Message);
            }
            finally
            {
                // SQLite on Windows can hold file locks even after disposal due to connection pooling.
                // Clear the pool to ensure metadata.db is released so the directory can be deleted.
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void TypeConverter_ShouldThrowInformativeError_OnFailedCast()
        {
            // Arrange
            string invalidValue = "NotADate";

            // Act & Assert
            var ex = Assert.Throws<ExecutionException>(() =>
                TypeConverter.Cast(invalidValue, "DATETIME")
            );

            Assert.Contains($"Failed to cast value '{invalidValue}' to type 'DATETIME'", ex.Message);
            Assert.IsType<FormatException>(ex.InnerException);
        }

        [Fact]
        public async Task ExternalSort_ShouldUseSystemSortKeyColumn()
        {
            // Arrange
            var evaluator = CreateEvaluator();

            // We'll use a script that forces an external sort by setting chunk size very small
            string sql = @"
SET EXTERNAL_SORT_CHUNK_SIZE = 2;
CREATE TABLE #data (id INT, val VARCHAR);
INSERT INTO #data VALUES (1, 'C'), (2, 'A'), (3, 'B');
SELECT * FROM #data ORDER BY val;";

            // Act
            await evaluator.Evaluate(Parse(sql));

            // Assert
            Assert.NotNull(evaluator.LastResult);
            // The result should not contain the internal sort key column if it was cleaned up,
            // but we can verify it doesn't collide with a user column named '__SORT_KEYS'

            await evaluator.Evaluate(Parse(@"
SET EXTERNAL_SORT_CHUNK_SIZE = 2;
CREATE TABLE #collision (id INT, _SYS_SORT_KEYS_ VARCHAR);
INSERT INTO #collision VALUES (1, 'UserValue');
SELECT * FROM #collision ORDER BY id;"));

            Assert.Equal("UserValue", evaluator.LastResult.Rows[0]["_SYS_SORT_KEYS_"]);
        }

        private Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }

        private Evaluator CreateEvaluator(ILogger? logger = null)
        {
            var l = logger ?? new TestLogger();
            var registry = new Mock<IFunctionRegistry>();
            var tracker = new Mock<ILineageTracker>();
            tracker.Setup(t => t.GlobalMetadata).Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var docker = new Mock<IDockerManager>();
            var sessions = new Mock<SessionStateManager>(l, _security, _config, null);
            var pushdown = new Mock<ExecutePushdownStatementHandler>(l);

            var handlers = new List<IStatementHandler>
            {
                new SelectStatementHandler(l),
                new InsertStatementHandler(l, pushdown.Object),
                new UpdateStatementHandler(l),
                new SetVariableStatementHandler(l),
                new DeclareStatementHandler(l),
                new WhileStatementHandler(l),
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
