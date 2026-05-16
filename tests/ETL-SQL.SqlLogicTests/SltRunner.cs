using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
    public class SltRunner : IDisposable
    {
        private readonly ILogger _logger;
        private readonly Evaluator _evaluator;
        public DataTable? LastResult => _evaluator.LastResult;
        private int _queryCount;

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
            var setup = @"CREATE CONNECTION slt ON MOCKDB();
SET LINEAGE = OFF;
SET TELEMETRY = OFF;";
            var tokens = new Lexer(setup).Tokenize();
            var script = new Parser(tokens, setup).Parse();
            await _evaluator.Evaluate(script);
        }

        private const string OurEngineName = "etlsql";

        public string? CurrentFile { get; set; }

        public async Task RunStatementDirectly(ETL_SQL.Core.Script script)
        {
            await _evaluator.Evaluate(script);
        }

        public async Task RunTestAsync(SltRecord record)
        {
            // skipif etlsql → skip; onlyif etlsql → run; otherwise opposite
            if (record.Type == SltRecordType.SkipIf && record.EngineCondition == OurEngineName) return;
            if (record.Type == SltRecordType.OnlyIf && record.EngineCondition != OurEngineName) return;

            if (string.IsNullOrWhiteSpace(record.Sql)) return;

            _queryCount++;
            LogProgress(record);

            var tokens = new Lexer(record.Sql).Tokenize();
            var script = new Parser(tokens, record.Sql).Parse();

            try
            {
                await _evaluator.Evaluate(script);

                if (record.Type == SltRecordType.Query)
                {
                    VerifyResults(record, _evaluator.LastResult);
                }

                // Clear results to prevent memory accumulation during long test runs
                _evaluator.LastResult = null;
                _evaluator.LastResultSets.Clear();

                if (_queryCount % 500 == 0)
                    GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

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

        private void LogProgress(SltRecord record)
        {
            // Always print the about-to-run line so the last output before a crash/cancel is informative.
            var sql = record.Sql?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            if (sql.Length > 80) sql = sql[..80] + "…";
            var file = CurrentFile != null ? System.IO.Path.GetFileName(CurrentFile) + " " : "";
            var prefix = $"[{_queryCount,5}] {file}L{record.LineNumber}: {sql}";

            // Every 50 queries also emit memory stats so you can spot the spike.
            if (_queryCount % 50 == 0)
            {
                var managedMB = GC.GetTotalMemory(false) / 1024 / 1024;
                var workingMB = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
                Console.Error.WriteLine($"{prefix}  | managed={managedMB}MB working={workingMB}MB");
            }
            else
            {
                Console.Error.WriteLine(prefix);
            }
        }

        private static readonly Regex _hashPattern =
            new(@"^(\d+) values hashing to ([0-9a-f]+)$", RegexOptions.Compiled);

        private void VerifyResults(SltRecord record, DataTable? actual)
        {
            var expectedTrimmed = (record.ExpectedResult ?? "").Trim();

            if (actual == null)
            {
                if (!string.IsNullOrEmpty(expectedTrimmed))
                    throw new Exception($"Line {record.LineNumber}: Expected results, but query returned no data.");
                return;
            }

            // Build rows as string arrays using SLT-canonical formatting
            var rows = actual.Rows
                .Select(r => actual.ColumnNames.Select(c => FormatSltValue(r[c])).ToArray())
                .ToList();

            // Apply sort mode
            IEnumerable<string[]> sortedRows = record.SortMode switch
            {
                SltSortMode.RowSort => rows.OrderBy(r => string.Join("\t", r), StringComparer.Ordinal),
                _ => rows
            };

            IEnumerable<string> flatValues = sortedRows.SelectMany(r => r);
            if (record.SortMode == SltSortMode.ValueSort)
                flatValues = flatValues.OrderBy(v => v, StringComparer.Ordinal);

            var flat = flatValues.ToList();

            // Hash-based comparison (SQLite SLT corpus format)
            var m = _hashPattern.Match(expectedTrimmed);
            if (m.Success)
            {
                var expectedCount = int.Parse(m.Groups[1].Value);
                var expectedHash  = m.Groups[2].Value;

                if (flat.Count != expectedCount)
                    throw new Exception($"Line {record.LineNumber}: Value count mismatch. Expected {expectedCount} values, got {flat.Count}.");

                var actualHash = ComputeSltHash(flat);
                if (actualHash != expectedHash)
                    throw new Exception(
                        $"Line {record.LineNumber}: Hash mismatch. Expected {expectedHash}, got {actualHash}. " +
                        $"Sample values: {string.Join(", ", flat.Take(5))}");
                return;
            }

            // Explicit value comparison
            var expectedValues = expectedTrimmed
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (flat.Count != expectedValues.Count)
                throw new Exception($"Line {record.LineNumber}: Row count mismatch. Expected {expectedValues.Count} values, got {flat.Count}.");

            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] != expectedValues[i])
                    throw new Exception($"Line {record.LineNumber}: Value mismatch at index {i}. Expected '{expectedValues[i]}', got '{flat[i]}'.");
            }
        }

        private static string FormatSltValue(object? value)
        {
            if (value == null) return "NULL";
            if (value is decimal d && d == Math.Truncate(d) && d >= long.MinValue && d <= long.MaxValue)
                return ((long)d).ToString();
            return value.ToString() ?? "NULL";
        }

        private static string ComputeSltHash(List<string> values)
        {
            var sb = new StringBuilder();
            foreach (var v in values) { sb.Append(v); sb.Append('\n'); }
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
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
                new SetThresholdStatementHandler(),
                new CreateConnectionStatementHandler(connectors, l)
            };

            var evaluator = new Evaluator(handlers, serviceProvider, registry, tracker.Object, docker.Object, connectors, sessions.Object, security, l, new ETL_SQL.Core.Metadata.LanguageHelpRegistry(), new EvaluatorComponentRegistry());
            evaluator.IsPersistentSession = false;
            return evaluator;
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

        public void Dispose()
        {
            _evaluator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
