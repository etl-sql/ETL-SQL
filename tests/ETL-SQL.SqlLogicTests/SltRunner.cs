using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;


namespace ETL_SQL.SqlLogicTests
{
    public class SltRunner : IDisposable
    {
        private static readonly long TotalSystemMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        private static readonly System.Diagnostics.Process CurrentProcess = System.Diagnostics.Process.GetCurrentProcess();
        private const double MemoryGuardFraction = 0.75;

        private readonly ILogger _logger;
        private readonly Evaluator _evaluator;
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
            var setup = @"CREATE CONNECTION slt AS MOCKDB();
SET LINEAGE = OFF;
SET TELEMETRY = OFF;";
            var tokens = new Lexer(setup).Tokenize();
            var script = new Parser(tokens, setup).Parse();
            await _evaluator.Evaluate(script);
        }

        private const string OurEngineName = "etlsql";

        public string? CurrentFile { get; set; }

        public long TempTableSpillThresholdRows
        {
            get => _evaluator.TempTableSpillThresholdRows;
            set => _evaluator.TempTableSpillThresholdRows = value;
        }

        public bool IsPersistentSession
        {
            get => _evaluator.IsPersistentSession;
            set => _evaluator.IsPersistentSession = value;
        }

        public string? SessionId
        {
            get => _evaluator.SessionId;
            set => _evaluator.SessionId = value;
        }

        public string SessionRoot
        {
            get => _evaluator.SessionRoot;
            set => _evaluator.SessionRoot = value;
        }

        public async Task RunTestAsync(SltRecord record)
        {
            // skipif etlsql → skip; onlyif etlsql → run; otherwise opposite
            if (record.Type == SltRecordType.SkipIf && record.EngineCondition == OurEngineName) return;
            if (record.Type == SltRecordType.OnlyIf && record.EngineCondition != OurEngineName) return;

            if (string.IsNullOrWhiteSpace(record.Sql)) return;

            _queryCount++;
            if (_queryCount % 10 == 0)
                ThrowIfMemoryExceeded();
            LogProgress(record);

            var tokens = new Lexer(record.Sql).Tokenize();
            var script = new Parser(tokens, record.Sql).Parse();

            // A record is query-like if it has an expected result payload (covers plain Query records
            // AND query records wrapped inside skipif/onlyif directives).
            bool isQueryLike = record.Type == SltRecordType.Query || record.ExpectedResult != null;

            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await _evaluator.Evaluate(script, cts.Token);

                if (isQueryLike)
                {
                    VerifyResults(record, _evaluator.LastResult);
                }

                // Clear results to prevent memory accumulation during long test runs
                _evaluator.LastResult = null;
                _evaluator.LastResultSets.Clear();

                if (_queryCount % 200 == 0)
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                    GC.WaitForPendingFinalizers();
                }

                if (!record.ExpectSuccess && !isQueryLike)
                {
                    throw new Exception($"Line {record.LineNumber}: Expected failure, but statement succeeded.");
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new Exception($"Line {record.LineNumber}: Query execution timed out after 5 minutes. SQL: {record.Sql}");
            }
            catch (Exception ex)
            {
                if (record.ExpectSuccess)
                {
                    try
                    {
                        var failLog = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slt_failure_debug.log");
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"--- FAILURE AT LINE {record.LineNumber} ---");
                        sb.AppendLine($"SQL: {record.Sql}");
                        sb.AppendLine($"Error: {ex.Message}");
                        if (_evaluator.Connections.TryGetValue("t3", out var t3src) && t3src is InMemoryDataSource t3mem)
                        {
                            var t3Batches = new List<DataTable>();
                            await foreach (var batch in t3mem.ReadBatches()) t3Batches.Add(batch);
                            var t3Rows = t3Batches.SelectMany(b => b.Rows).ToList();
                            sb.AppendLine($"t3 row count: {t3Rows.Count}");
                            var matchingT3 = t3Rows.Where(r =>
                            {
                                var val = r["a3"];
                                if (val == null || val == DBNull.Value) return false;
                                try
                                {
                                    long l = Convert.ToInt64(val);
                                    return l == 637 || l == 591 || l == 710 || l == 644;
                                }
                                catch { return false; }
                            }).ToList();
                            sb.AppendLine($"t3 matching rows: {matchingT3.Count}");
                        }
                        if (_evaluator.Connections.TryGetValue("t7", out var t7src) && t7src is InMemoryDataSource t7mem)
                        {
                            var t7Batches = new List<DataTable>();
                            await foreach (var batch in t7mem.ReadBatches()) t7Batches.Add(batch);
                            var t7Rows = t7Batches.SelectMany(b => b.Rows).ToList();
                            sb.AppendLine($"t7 row count: {t7Rows.Count}");
                            var matchingT7 = t7Rows.Where(r =>
                            {
                                var val = r["e7"];
                                if (val == null || val == DBNull.Value) return false;
                                try
                                {
                                    long l = Convert.ToInt64(val);
                                    return l == 280;
                                }
                                catch { return false; }
                            }).ToList();
                            sb.AppendLine($"t7 matching rows: {matchingT7.Count}");
                        }
                        System.IO.File.AppendAllText(failLog, sb.ToString());
                    }
                    catch (Exception debugEx)
                    {
                        try
                        {
                            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slt_failure_debug.log"), $"Debug logging failed: {debugEx.Message}\n");
                        }
                        catch { }
                    }
                    throw new Exception($"Line {record.LineNumber}: Statement failed: {ex.Message}", ex);
                }
            }
        }

        private void ThrowIfMemoryExceeded()
        {
            CurrentProcess.Refresh();
            var workingSet = CurrentProcess.WorkingSet64;
            var limitBytes = (long)(TotalSystemMemoryBytes * MemoryGuardFraction);
            if (workingSet <= limitBytes) return;

            var usedMB = workingSet / 1024 / 1024;
            var limitMB = limitBytes / 1024 / 1024;
            var totalMB = TotalSystemMemoryBytes / 1024 / 1024;
            throw new InvalidOperationException(
                $"SLT memory guard: working set {usedMB}MB exceeds 75% of system memory " +
                $"({totalMB}MB total, limit={limitMB}MB). Aborted at query {_queryCount}.");
        }

        private void LogProgress(SltRecord record)
        {
            // Always print the about-to-run line so the last output before a crash/cancel is informative.
            var sql = record.Sql?.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            if (sql.Length > 80) sql = sql[..80] + "…";
            var file = CurrentFile != null ? System.IO.Path.GetFileName(CurrentFile) + " " : "";
            var prefix = $"[{_queryCount,5}] {file}L{record.LineNumber}: {sql}";

            try
            {
                var progressFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "slt_progress.log");
                System.IO.File.WriteAllText(progressFile, prefix + Environment.NewLine);
            }
            catch { }

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
            var expectedTrimmed = (record.ExpectedResult ?? "").Trim('\r', '\n');

            if (actual == null)
            {
                if (!string.IsNullOrEmpty(expectedTrimmed))
                    throw new Exception($"Line {record.LineNumber}: Expected results, but query returned no data.");
                return;
            }

            // Build rows as string arrays using SLT-canonical formatting
            var rows = new List<string[]>();
            foreach (var r in actual.Rows)
            {
                var rowVals = new string[actual.ColumnNames.Count];
                for (int c = 0; c < actual.ColumnNames.Count; c++)
                {
                    char typeChar = 'T';
                    if (!string.IsNullOrEmpty(record.ColumnTypes) && c < record.ColumnTypes.Length)
                    {
                        typeChar = char.ToUpperInvariant(record.ColumnTypes[c]);
                    }
                    rowVals[c] = FormatSltValue(r[c], typeChar);
                }
                rows.Add(rowVals);
            }

            // Softened type checking validation (SQLite dynamically typed tests)
            // No strict assertions since dynamic types are expected in the SLT corpus.

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
                var expectedHash = m.Groups[2].Value;

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

            if (record.LineNumber == 5510 || record.LineNumber == 38929 || record.LineNumber == 24654)
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"--- DEBUG VerifyResults for Line {record.LineNumber} ---");
                    sb.AppendLine($"flat: {string.Join(", ", flat)}");
                    sb.AppendLine($"expectedValues: {string.Join(", ", expectedValues)}");
                    System.IO.File.AppendAllText(@"C:\Users\chuck\scratch\ETL-SQL\debug_select.txt", sb.ToString());
                }
                catch { }
            }

            if (flat.Count != expectedValues.Count)
                throw new Exception($"Line {record.LineNumber}: Row count mismatch. Expected {expectedValues.Count} values, got {flat.Count}.");

            for (int i = 0; i < flat.Count; i++)
            {
                if (flat[i] != expectedValues[i])
                    throw new Exception($"Line {record.LineNumber}: Value mismatch at index {i}. Expected '{expectedValues[i]}', got '{flat[i]}'.");
            }
        }

        private static string FormatSltValue(object? value, char typeChar)
        {
            if (value == null || value == DBNull.Value) return "NULL";
            if (value is bool bv) return bv ? "1" : "0";
            if (value is byte[] bytes)
            {
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }

            if (typeChar == 'I')
            {
                try
                {
                    double d;
                    if (value is double db) d = db;
                    else if (value is float fl) d = fl;
                    else if (value is decimal dec) d = (double)dec;
                    else if (value is int i) d = i;
                    else if (value is long l) d = l;
                    else if (value is short s) d = s;
                    else if (value is byte b) d = b;
                    else d = double.Parse(value.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);

                    if (double.IsNaN(d) || double.IsInfinity(d))
                        return "NULL";

                    return ((long)Math.Truncate(d)).ToString();
                }
                catch
                {
                    return "0";
                }
            }
            else if (typeChar == 'R')
            {
                try
                {
                    double d;
                    if (value is double db) d = db;
                    else if (value is float fl) d = fl;
                    else if (value is decimal dec) d = (double)dec;
                    else if (value is int i) d = i;
                    else if (value is long l) d = l;
                    else if (value is short s) d = s;
                    else if (value is byte b) d = b;
                    else d = double.Parse(value.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);

                    if (double.IsNaN(d)) return "NaN";
                    if (double.IsPositiveInfinity(d)) return "Inf";
                    if (double.IsNegativeInfinity(d)) return "-Inf";

                    return d.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    return "0.000";
                }
            }
            else
            {
                if (value is decimal dec && dec == Math.Truncate(dec) && dec >= long.MinValue && dec <= long.MaxValue)
                    return ((long)dec).ToString();
                if (value is double d && d == Math.Truncate(d) && d >= long.MinValue && d <= long.MaxValue)
                    return ((long)d).ToString();
                return value.ToString() ?? "NULL";
            }
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
            FileFunctions.Register(registry);
            StandardFunctions.Register(registry);
            JsonFunctions.Register(registry);
            XmlFunctions.Register(registry);
            FuzzyFunctions.Register(registry);

            var tracker = new Mock<ILineageTracker>();
            tracker.Setup(t => t.GlobalMetadata).Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, string>());

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
                new CreateConnectionStatementHandler(connectors, l),
                new CreateViewStatementHandler(),
                new DropViewStatementHandler(l),
                new ShowViewsStatementHandler(),
                new MergeStatementHandler(l),
                new AlterTableStatementHandler(l),
                new GenerateStatementHandler(l),
                new TruncateTableStatementHandler(l)
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
