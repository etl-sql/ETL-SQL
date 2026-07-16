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
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
    public class TpcHBenchmarks
    {
        private readonly double _scaleFactor;

        public TpcHBenchmarks() => _scaleFactor = 0.01;
        public TpcHBenchmarks(double scaleFactor) => _scaleFactor = scaleFactor;

        private Evaluator _evaluator = null!;
        private string _q1 = null!;
        private string _q6 = null!;
        private string _q3 = null!;
        private string _q5 = null!;
        private string _q12 = null!;
        private string _q14 = null!;
        private Script _q1Script = null!;
        private Script _q6Script = null!;
        private Script _q3Script = null!;
        private Script _q5Script = null!;
        private Script _q12Script = null!;
        private Script _q14Script = null!;
        public DataTable? LastResult => _evaluator?.LastResult;

        [GlobalSetup]
        public async Task Setup()
        {
            try
            {
                Console.WriteLine("// TpcHBenchmarks.Setup: Starting...");
                var services = new ServiceCollection();
                var l = new Mock<ILogger>().Object;
                var security = new SecurityService(l) { IsTestMode = true };
                var registry = new FunctionRegistry();
                StandardFunctions.Register(registry);

                var tracker = new Mock<ILineageTracker>();
                tracker.Setup(t => t.GlobalMetadata).Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, string>());

                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { { "Session:PersistentSessionTTLHours", "1" } })
                    .Build();

                var tempSessionDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-Benchmarks-" + Guid.NewGuid());
                Directory.CreateDirectory(tempSessionDir);

                var sessions = new SessionStateManager(l, security, config, new SqliteSessionMetadataStoreFactory(), tempSessionDir);
                var pushdown = new Mock<ExecutePushdownStatementHandler>(l);
                var bufferManager = new Mock<IBufferManager>();

                var connectors = new ConnectorRegistry();
                var tpcHSeeder = new TpcHMockDataSeeder(_scaleFactor);
                connectors.Register(new TpcHMockConnector(tpcHSeeder));

                var docker = new Mock<IDockerManager>();

                var handlers = new List<IStatementHandler>
                {
                    new SelectStatementHandler(l),
                    new CreateConnectionStatementHandler(connectors, l)
                };

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
                _evaluator.RedirectOutput = true;

                Console.WriteLine("// TpcHBenchmarks.Setup: Evaluator initialized.");

                // Initialize connection once
                var connSql = "CREATE CONNECTION tpch AS TPCHMOCK(SupportsSqlPushdown = false);";
                var tokens = new Lexer(connSql).Tokenize();
                var script = new Parser(tokens, connSql).Parse();
                await _evaluator.Evaluate(script);

                _q1 = @"
                    SELECT
                        l_returnflag,
                        l_linestatus,
                        SUM(l_quantity) AS sum_qty,
                        SUM(l_extendedprice) AS sum_base_price,
                        SUM(l_extendedprice * (1 - l_discount)) AS sum_disc_price,
                        SUM(l_extendedprice * (1 - l_discount) * (1 + l_tax)) AS sum_charge,
                        AVG(l_quantity) AS avg_qty,
                        AVG(l_extendedprice) AS avg_price,
                        AVG(l_discount) AS avg_disc,
                        COUNT(*) AS count_order
                    FROM
                        tpch.lineitem
                    WHERE
                        l_shipdate <= '1998-09-01'
                    GROUP BY
                        l_returnflag,
                        l_linestatus
                    ORDER BY
                        l_returnflag,
                        l_linestatus;";

                _q6 = @"
                    SELECT
                        SUM(l_extendedprice * l_discount) AS revenue
                    FROM
                        tpch.lineitem
                    WHERE
                        l_shipdate >= '1994-01-01'
                        AND l_shipdate < '1995-01-01'
                        AND l_discount BETWEEN 0.05 AND 0.07
                        AND l_quantity < 24;";
                _q3 = @"
                    SELECT
                        l_orderkey,
                        SUM(l_extendedprice * (1 - l_discount)) AS revenue,
                        o_orderdate,
                        o_shippriority
                    FROM tpch.lineitem
                    INNER JOIN tpch.orders ON l_orderkey = o_orderkey
                    INNER JOIN tpch.customer ON o_custkey = c_custkey
                    WHERE c_mktsegment = 'BUILDING'
                        AND o_orderdate < '1995-03-15'
                        AND l_shipdate > '1995-03-15'
                    GROUP BY l_orderkey, o_orderdate, o_shippriority
                    ORDER BY revenue DESC, o_orderdate
                    LIMIT 10;";

                _q5 = @"
                    SELECT
                        n_name,
                        SUM(l_extendedprice * (1 - l_discount)) AS revenue
                    FROM tpch.lineitem
                    INNER JOIN tpch.orders ON l_orderkey = o_orderkey
                    INNER JOIN tpch.customer ON o_custkey = c_custkey
                    INNER JOIN tpch.supplier ON l_suppkey = s_suppkey
                    INNER JOIN tpch.nation ON s_nationkey = n_nationkey
                    INNER JOIN tpch.region ON n_regionkey = r_regionkey
                    WHERE r_name = 'ASIA'
                        AND c_nationkey = s_nationkey
                        AND o_orderdate >= '1994-01-01'
                        AND o_orderdate < '1995-01-01'
                    GROUP BY n_name
                    ORDER BY revenue DESC;";

                _q12 = @"
                    SELECT
                        l_shipmode,
                        SUM(CASE WHEN o_orderpriority = '1-URGENT' OR o_orderpriority = '2-HIGH' THEN 1 ELSE 0 END) AS high_line_count,
                        SUM(CASE WHEN o_orderpriority <> '1-URGENT' AND o_orderpriority <> '2-HIGH' THEN 1 ELSE 0 END) AS low_line_count
                    FROM tpch.lineitem
                    INNER JOIN tpch.orders ON o_orderkey = l_orderkey
                    WHERE l_shipmode IN ('MAIL', 'SHIP')
                        AND l_commitdate < l_receiptdate
                        AND l_shipdate < l_commitdate
                        AND l_receiptdate >= '1994-01-01'
                        AND l_receiptdate < '1995-01-01'
                    GROUP BY l_shipmode
                    ORDER BY l_shipmode;";

                _q14 = @"
                    SELECT
                        100.00 * SUM(CASE WHEN p_type LIKE 'PROMO%' THEN l_extendedprice * (1 - l_discount) ELSE 0 END)
                               / SUM(l_extendedprice * (1 - l_discount)) AS promo_revenue
                    FROM tpch.lineitem
                    INNER JOIN tpch.part ON l_partkey = p_partkey
                    WHERE l_shipdate >= '1995-09-01'
                        AND l_shipdate < '1995-10-01';";

                _q1Script = new Parser(new Lexer(_q1).Tokenize()).Parse();
                _q6Script = new Parser(new Lexer(_q6).Tokenize()).Parse();
                _q3Script = new Parser(new Lexer(_q3).Tokenize()).Parse();
                _q5Script = new Parser(new Lexer(_q5).Tokenize()).Parse();
                _q12Script = new Parser(new Lexer(_q12).Tokenize()).Parse();
                _q14Script = new Parser(new Lexer(_q14).Tokenize()).Parse();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("// TpcHBenchmarks.Setup: ERROR!");
                Console.Error.WriteLine(ex.ToString());
                throw;
            }
        }

        [Benchmark]
        public async Task RunQ1()
        {
            await _evaluator.Evaluate(_q1Script);
        }

        [Benchmark]
        public async Task RunQ6()
        {
            await _evaluator.Evaluate(_q6Script);
        }

        [Benchmark]
        public async Task RunQ3()
        {
            await _evaluator.Evaluate(_q3Script);
        }

        [Benchmark]
        public async Task RunQ5()
        {
            await _evaluator.Evaluate(_q5Script);
        }

        [Benchmark]
        public async Task RunQ12()
        {
            await _evaluator.Evaluate(_q12Script);
        }

        [Benchmark]
        public async Task RunQ14()
        {
            await _evaluator.Evaluate(_q14Script);
        }

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
    }

    public class TpcHMockConnector : IConnector
    {
        private readonly IMockDataSeeder _seeder;
        public TpcHMockConnector(IMockDataSeeder seeder) => _seeder = seeder;
        public string Name => "TPCHMOCK";
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
        public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) => Task.FromResult("TPC-H Mock");
        public HashSet<string> GetSupportedFunctions() => new();
        public HashSet<string> GetSupportedKeywords() => new();
        public Dictionary<string, string[]> GetSupportedOptions() => new();
        public Dictionary<string, string[]> GetOptionValues() => new();
        public string GetHelp() => "TPC-H Mock Connector";

        public IDataSource CreateDataSource(IExecutionContext context, string connectionString, Dictionary<string, string>? options = null)
        {
            var ds = new ETL_SQL.Connectors.MockDb.MockSqlDataSource(context, connectionString, "MockDB", options, _seeder);
            return new PushdownDisabledDataSource(ds);
        }

        public async Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString)
        {
            var ds = new ETL_SQL.Connectors.MockDb.MockSqlDataSource(context, connectionString, "MockDB", null, _seeder);
            return await ds.GetTablesAsync();
        }
        public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
        public async Task<IEnumerable<string>> GetColumnsAsync(IExecutionContext context, string connectionString, string tableName)
        {
            var ds = new ETL_SQL.Connectors.MockDb.MockSqlDataSource(context, connectionString, "MockDB", null, _seeder);
            return await ds.GetColumnsAsync(tableName);
        }
        public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) => Task.FromResult(Enumerable.Empty<string>());
    }

    public class PushdownDisabledDataSource : IDatabaseSource
    {
        private readonly IDatabaseSource _inner;
        public PushdownDisabledDataSource(IDatabaseSource inner) => _inner = inner;
        public bool SupportsSqlPushdown => false;
        public string ConnectorType => _inner.ConnectorType;
        public string Path => _inner.Path;
        public string ConnectionString => _inner.ConnectionString;
        public string Dialect => _inner.Dialect;
        public Dictionary<string, string>? Options => _inner.Options;
        public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => _inner.ReadBatches(batchSize);
        public Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) => _inner.WriteBatches(batches, append);
        public Task<IEnumerable<string>> GetColumnsAsync() => _inner.GetColumnsAsync();
        public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => _inner.GetColumnsAsync(tableName);
        public Task<IEnumerable<string>> GetTablesAsync() => _inner.GetTablesAsync();
        public Task<IEnumerable<string>> GetViewsAsync() => _inner.GetViewsAsync();
        public Task<string> GetVersionAsync() => _inner.GetVersionAsync();
        public HashSet<string> GetSupportedFunctions() => _inner.GetSupportedFunctions();
        public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null) => _inner.ExecuteRawSql(sql, parameters);
        public object? Snapshot() => _inner.Snapshot();
        public void Restore(object? snapshot) => _inner.Restore(snapshot);
        public IDataSource WithTable(string tableName)
        {
            var tableDs = _inner.WithTable(tableName);
            if (tableDs is IDatabaseSource dbDs) return new PushdownDisabledDataSource(dbDs);
            return tableDs;
        }
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}
