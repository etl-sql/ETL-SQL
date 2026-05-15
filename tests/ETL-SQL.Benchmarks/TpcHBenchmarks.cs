using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ETL_SQL.Core;
using ETL_SQL.Core.Functions;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Metadata;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Services;
using ETL_SQL.Common;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Engine.Functions;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.MockDb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;

namespace ETL_SQL.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(launchCount: 1, warmupCount: 1, iterationCount: 3)]
    public class TpcHBenchmarks
    {

        private Evaluator _evaluator;
        private string _q1;
        private string _q6;
        private Script _q1Script;
        private Script _q6Script;
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
                tracker.Setup(t => t.GlobalMetadata).Returns(new Dictionary<string, string>());
                
                var config = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string> { { "Session:PersistentSessionTTLHours", "1" } })
                    .Build();
                
                var tempSessionDir = Path.Combine(Path.GetTempPath(), "ETL-SQL-Benchmarks-" + Guid.NewGuid());
                Directory.CreateDirectory(tempSessionDir);
                
                var sessions = new SessionStateManager(l, security, config, tempSessionDir);
                var pushdown = new Mock<ExecutePushdownStatementHandler>(l);
                var bufferManager = new Mock<IBufferManager>();
                
                var connectors = new ConnectorRegistry();
                var tpcHSeeder = new TpcHMockDataSeeder(0.01);
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

                Console.WriteLine("// TpcHBenchmarks.Setup: Evaluator initialized.");

                // Initialize connection once
                var connSql = "CREATE CONNECTION tpch ON TPCHMOCK() WITH (SupportsSqlPushdown = false);";
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
                _q1Script = new Parser(new Lexer(_q1).Tokenize()).Parse();
                _q6Script = new Parser(new Lexer(_q6).Tokenize()).Parse();
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
