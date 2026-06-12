using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Metadata;
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

namespace ETL_SQL.Benchmarks
{
    /// <summary>
    /// Benchmarks the five SELECT shapes that the v0.8.x streaming pipeline targets.
    /// Each benchmark establishes a Phase 0 baseline for comparison after streaming work lands.
    ///
    /// Run with:
    ///   dotnet run --project tests/ETL-SQL.Benchmarks -c Release -- --filter *SelectShape*
    ///
    /// LargeScale variants (100k rows):
    ///   dotnet run --project tests/ETL-SQL.Benchmarks -c Release -- --filter *SelectShapeLargeScale*
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
    public class SelectShapeBenchmarks
    {
        private readonly int _rowCount;

        public SelectShapeBenchmarks() => _rowCount = 10_000;
        public SelectShapeBenchmarks(int rowCount) => _rowCount = rowCount;

        private Evaluator _evaluator = null!;
        private Script _simpleFilterScript = null!;
        private Script _distinctScript = null!;
        private Script _limitedSortScript = null!;
        private Script _windowQualifyScript = null!;
        private Script _unionAllScript = null!;

        public DataTable? LastResult => _evaluator?.LastResult;

        [GlobalSetup]
        public async Task Setup()
        {
            try
            {
                Console.WriteLine($"// SelectShapeBenchmarks.Setup: Starting ({_rowCount:N0} rows)...");

                var l = new Mock<ILogger>().Object;
                var security = new SecurityService(l) { IsTestMode = true };
                var registry = new FunctionRegistry();
                StandardFunctions.Register(registry);

                var tracker = new Mock<ILineageTracker>();
                tracker.Setup(t => t.GlobalMetadata).Returns(new Dictionary<string, string>());

                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { { "Session:PersistentSessionTTLHours", "1" } })
                    .Build();

                var tempSessionDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-ShapeBench-" + Guid.NewGuid());
                Directory.CreateDirectory(tempSessionDir);

                var sessions = new SessionStateManager(l, security, config, tempSessionDir);
                var pushdown = new Mock<ExecutePushdownStatementHandler>(l);
                var bufferManager = new Mock<IBufferManager>();
                var docker = new Mock<IDockerManager>();

                var connectors = new ConnectorRegistry();
                connectors.Register(new SelectShapeMockConnector(new SelectShapeDataSeeder(_rowCount)));

                var handlers = new List<IStatementHandler>
                {
                    new SelectStatementHandler(l),
                    new CreateConnectionStatementHandler(connectors, l)
                };

                var services = new ServiceCollection();
                services.AddSingleton(l);
                services.AddSingleton(security);
                services.AddSingleton<IFunctionRegistry>(registry);
                services.AddSingleton(tracker.Object);
                services.AddSingleton(docker.Object);
                services.AddSingleton<IConnectorRegistry>(connectors);
                services.AddSingleton<ISessionStateManager>(sessions);
                services.AddSingleton(config);
                services.AddSingleton(pushdown.Object);
                services.AddSingleton(bufferManager.Object);
                var sp = services.BuildServiceProvider();

                _evaluator = new Evaluator(handlers, sp, registry, tracker.Object, docker.Object, connectors, sessions, security, l, new LanguageHelpRegistry(), new EvaluatorComponentRegistry());

                var connSql = "CREATE CONNECTION shapes AS SHAPEMOCK(SupportsSqlPushdown = false);";
                await _evaluator.Evaluate(new Parser(new Lexer(connSql).Tokenize()).Parse());

                Console.WriteLine("// SelectShapeBenchmarks.Setup: Connection established.");

                // Simple projection + filter — no blocking operators; streaming candidate.
                _simpleFilterScript = Parse(
                    "SELECT id, score FROM shapes.events WHERE score > 50;");

                // DISTINCT — blocking (hash set required).
                _distinctScript = Parse(
                    "SELECT DISTINCT category FROM shapes.events;");

                // ORDER BY + LIMIT — Top-N heap candidate; today does full sort then Take.
                _limitedSortScript = Parse(
                    "SELECT id, score FROM shapes.events ORDER BY score DESC LIMIT 100;");

                // Window + QUALIFY — picks the top-scoring event per category.
                // Blocking (window partitions require full partition materialization).
                _windowQualifyScript = Parse(@"
                    SELECT id, category, score,
                           ROW_NUMBER() OVER (PARTITION BY category ORDER BY score DESC) AS rnk
                    FROM shapes.events
                    QUALIFY rnk = 1;");

                // UNION ALL — concatenates two filtered streams; streaming candidate.
                _unionAllScript = Parse(@"
                    SELECT id, score FROM shapes.events WHERE score > 75
                    UNION ALL
                    SELECT id, score FROM shapes.events WHERE score <= 25;");

                Console.WriteLine("// SelectShapeBenchmarks.Setup: All scripts parsed.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("// SelectShapeBenchmarks.Setup: ERROR!");
                Console.Error.WriteLine(ex.ToString());
                throw;
            }
        }

        [Benchmark(Description = "SimpleFilter — WHERE, no aggregate (streaming candidate)")]
        public async Task SimpleFilter() => await _evaluator.Evaluate(_simpleFilterScript);

        [Benchmark(Description = "Distinct — SELECT DISTINCT (blocking: hash set)")]
        public async Task Distinct() => await _evaluator.Evaluate(_distinctScript);

        [Benchmark(Description = "LimitedSort — ORDER BY score DESC LIMIT 100 (Top-N heap candidate)")]
        public async Task LimitedSort() => await _evaluator.Evaluate(_limitedSortScript);

        [Benchmark(Description = "WindowQualify — ROW_NUMBER + QUALIFY per category (blocking: window)")]
        public async Task WindowQualify() => await _evaluator.Evaluate(_windowQualifyScript);

        [Benchmark(Description = "UnionAll — two filtered streams concatenated (streaming candidate)")]
        public async Task UnionAll() => await _evaluator.Evaluate(_unionAllScript);

        [GlobalCleanup]
        public void ReportExtraMetrics()
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            proc.Refresh();
            var mem = GC.GetGCMemoryInfo();
            long lohBytes = mem.GenerationInfo.Length > 3 ? mem.GenerationInfo[3].SizeAfterBytes : -1;
            Console.WriteLine($"// [ExtraMetrics] WorkingSet={proc.WorkingSet64 / 1024 / 1024} MB, " +
                $"ManagedHeap={GC.GetTotalMemory(false) / 1024 / 1024} MB, " +
                $"LOH≈{(lohBytes >= 0 ? lohBytes / 1024 : -1)} KB, " +
                $"SpillBytes={_evaluator.Telemetry.TotalSpilledBytes}, " +
                $"SortSpills={_evaluator.Telemetry.SortSpillCount}, " +
                $"RowsProcessed={_evaluator.Telemetry.RowsProcessed}, " +
                $"RetainedRows={_evaluator.LastResult?.Rows.Count ?? 0}");
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize()).Parse();
    }

    /// <summary>
    /// SELECT shape benchmarks at 100k rows. Excluded from CI; run explicitly for profiling.
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
    [BenchmarkCategory("LargeScale")]
    public class SelectShapeBenchmarksLargeScale
    {
        private readonly SelectShapeBenchmarks _inner = new(100_000);

        [GlobalSetup]
        public async Task Setup() => await _inner.Setup();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task SimpleFilter_100k() => await _inner.SimpleFilter();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task Distinct_100k() => await _inner.Distinct();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task LimitedSort_100k() => await _inner.LimitedSort();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task WindowQualify_100k() => await _inner.WindowQualify();

        [Benchmark]
        [BenchmarkCategory("LargeScale")]
        public async Task UnionAll_100k() => await _inner.UnionAll();
    }

    public class SelectShapeMockConnector : IConnector
    {
        private readonly IMockDataSeeder _seeder;
        public SelectShapeMockConnector(IMockDataSeeder seeder) => _seeder = seeder;
        public string Name => "SHAPEMOCK";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("SelectShape Mock");
        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();
        public Dictionary<string, string[]> GetSupportedOptions() => new();
        public Dictionary<string, string[]> GetOptionValues() => new();
        public string GetHelp() => "SelectShape Mock Connector";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            var ds = new MockSqlDataSource(context, connectionString, "MockDB", options, _seeder);
            return new PushdownDisabledDataSource(ds);
        }

        public async Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString)
        {
            var ds = new MockSqlDataSource(context, connectionString, "MockDB", null, _seeder);
            return await ds.GetTablesAsync();
        }

        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult(Enumerable.Empty<string>());

        public async Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName)
        {
            var ds = new MockSqlDataSource(context, connectionString, "MockDB", null, _seeder);
            return await ds.GetColumnsAsync(tableName);
        }

        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) =>
            Task.FromResult(Enumerable.Empty<string>());
    }
}
