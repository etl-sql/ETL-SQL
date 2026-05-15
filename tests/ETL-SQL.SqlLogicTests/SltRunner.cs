using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Core.Functions;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Connectors;
using ETL_SQL.Core.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Services;


namespace ETL_SQL.SqlLogicTests
{
    public class SltRunner
    {
        private readonly ILogger _logger;
        private readonly Evaluator _evaluator;

        public SltRunner(ILogger? logger = null)
        {
            _logger = logger ?? new ConsoleLogger();
            _evaluator = CreateEvaluator(_logger);

            // Auto-initialize a MOCKDB connection for standard SQL tests.
            // Task.Run avoids a deadlock if a sync context is present.
            Task.Run(() => InitializeAsync()).GetAwaiter().GetResult();
        }

        private async Task InitializeAsync()
        {
            var setup = @"CREATE CONNECTION slt ON MOCKDB();";
            var tokens = new Lexer(setup).Tokenize();
            var script = new Parser(tokens, setup).Parse();
            await _evaluator.Evaluate(script);
        }

        private const string OurEngineName = "etlsql";

        public async Task RunTestAsync(SltRecord record)
        {
            // skipif etlsql → skip; onlyif etlsql → run; otherwise opposite
            if (record.Type == SltRecordType.SkipIf && record.EngineCondition == OurEngineName) return;
            if (record.Type == SltRecordType.OnlyIf && record.EngineCondition != OurEngineName) return;

            if (string.IsNullOrWhiteSpace(record.Sql)) return;

            var tokens = new Lexer(record.Sql).Tokenize();
            var script = new Parser(tokens, record.Sql).Parse();

            try
            {
                await _evaluator.Evaluate(script);
                
                if (record.Type == SltRecordType.Query)
                {
                    VerifyResults(record, _evaluator.LastResult);
                }
                
                if (!record.ExpectSuccess && record.Type == SltRecordType.Statement)
                {
                    throw new Exception($"Line {record.LineNumber}: Expected failure, but statement succeeded.");
                }
            }
            catch (Exception ex)
            {
                if (record.ExpectSuccess)
                {
                    throw new Exception($"Line {record.LineNumber}: Statement failed: {ex.Message}", ex);
                }
            }
        }

        private void VerifyResults(SltRecord record, DataTable? actual)
        {
            if (actual == null)
            {
                if (!string.IsNullOrEmpty(record.ExpectedResult))
                    throw new Exception($"Line {record.LineNumber}: Expected results, but query returned no data.");
                return;
            }

            var actualValues = actual.Rows.SelectMany(r => actual.ColumnNames.Select(c => r[c]?.ToString() ?? "NULL")).ToList();
            var expectedValues = (record.ExpectedResult ?? "")
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (actualValues.Count != expectedValues.Count)
            {
                throw new Exception($"Line {record.LineNumber}: Row count mismatch. Expected {expectedValues.Count} values, got {actualValues.Count}.");
            }

            for (int i = 0; i < actualValues.Count; i++)
            {
                if (actualValues[i] != expectedValues[i])
                {
                    throw new Exception($"Line {record.LineNumber}: Value mismatch at index {i}. Expected '{expectedValues[i]}', got '{actualValues[i]}'.");
                }
            }
        }

        private Evaluator CreateEvaluator(ILogger l)
        {
            var services = new ServiceCollection();
            
            var security = new SecurityService(l) { IsTestMode = true };
            var registry = new ETL_SQL.Engine.Functions.FunctionRegistry();
            StandardFunctions.Register(registry);
            
            var tracker = new Mock<ILineageTracker>();
            tracker.Setup(t => t.GlobalMetadata).Returns(new Dictionary<string, string>());
            
            var docker = new Mock<IDockerManager>();
            var sessions = new Mock<ISessionStateManager>();
            var pushdown = new Mock<ExecutePushdownStatementHandler>(l);
            var bufferManager = new Mock<IBufferManager>();
            
            var connectors = new ConnectorRegistry();
            connectors.Register(new ETL_SQL.Connectors.MockDb.MockDbConnector());

            services.AddSingleton(l);
            services.AddSingleton(security);
            services.AddSingleton<IFunctionRegistry>(registry);
            services.AddSingleton(tracker.Object);
            services.AddSingleton(docker.Object);
            services.AddSingleton<ISessionStateManager>(sessions.Object);
            services.AddSingleton(pushdown.Object);
            services.AddSingleton(bufferManager.Object);
            services.AddSingleton<IConnectorRegistry>(connectors);

            var serviceProvider = services.BuildServiceProvider();

            var handlers = new List<IStatementHandler>
            {
                new SelectStatementHandler(l),
                new InsertStatementHandler(l, pushdown.Object),
                new UpdateStatementHandler(l),
                new DeleteStatementHandler(l),
                new SetVariableStatementHandler(l),
                new DeclareStatementHandler(l),
                new IfStatementHandler(l),
                new WhileStatementHandler(l),
                new ParallelStatementHandler(),
                new PrintStatementHandler(l),
                new CreateTableStatementHandler(l),
                new CreateIndexStatementHandler(l),
                new DropTableStatementHandler(l),
                new DropIndexStatementHandler(l),
                new BlockStatementHandler(),
                new CreateConnectionStatementHandler(connectors, l)
            };

            return new Evaluator(handlers, serviceProvider, registry, tracker.Object, docker.Object, connectors, sessions.Object, security, l, new ETL_SQL.Core.Metadata.LanguageHelpRegistry(), new EvaluatorComponentRegistry());
        }

        private class ConsoleLogger : ILogger
        {
            public bool IsDebugEnabled => true;
            public bool IsVerboseEnabled => true;
            public bool IsVerbose { get; set; }
            public bool SuppressConsole { get; set; }
            public bool IsJsonMode { get; set; }
            public string? SessionId { get; set; }
#pragma warning disable CS0067
            public event Action<string, string?, ConsoleColor>? OnMessage;
#pragma warning restore CS0067

            public void Log(LogLevel level, string message, Exception? ex = null)
            {
                if (level >= LogLevel.Warning) Console.WriteLine($"[{level}] {message}");
            }

            public void Debug(string message) => Log(LogLevel.Debug, message);
            public void Info(string message) => Log(LogLevel.Info, message);
            public void Warning(string message) => Log(LogLevel.Warning, message);
            public void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
            public void WriteLine(string message, ConsoleColor color = ConsoleColor.White) => Console.WriteLine(message);

            public void Debug(string template, params object?[] args) => Log(LogLevel.Debug, message: template);
            public void Info(string template, params object?[] args) => Log(LogLevel.Info, template);
            public void Warning(string template, params object?[] args) => Log(LogLevel.Warning, template);
            public void Error(string template, Exception? ex, params object?[] args) => Log(LogLevel.Error, template, ex);
        }
    }
}
