using Xunit;
using ETL_SQL.Engine;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Common;
using ETL_SQL.Tests.Core;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Moq;
using ETL_SQL.Common;
using ETL_SQL.Services;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;

namespace ETL_SQL.Tests.Engine
{
    public class SubqueryComplexTests
    {
        private Evaluator CreateEvaluator()
        {
            var logger = new Mock<ILogger>();
            var services = new Mock<IServiceProvider>();
            var functions = new Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            var tracker = new Mock<ILineageTracker>();
            var docker = new Mock<IDockerManager>();
            var connectors = new Mock<IConnectorRegistry>();
            var security = new SecurityService(logger.Object);
            var sessions = new Mock<ISessionStateManager>();
            var languageHelp = new Mock<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry>();
            var variableScopeManager = new VariableScopeManager();
            
            var registry = new EvaluatorComponentRegistry();
            
            // Mock a connector for MOCKDB
            var mockConnector = new Mock<IConnector>();
            mockConnector.Setup(c => c.CreateDataSource(It.IsAny<IExecutionContext>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<IEnumerable<ColumnDefinition>>()))
                         .Returns((IExecutionContext ctx, string target, Dictionary<string, string> opts, IEnumerable<ColumnDefinition> schema) => {
                             var m = new PersistentMockDatabaseSource();
                             // Return one row to satisfy SELECT ... FROM src
                             var dt = new DataTable();
                             dt.SetColumns(new[] { "DUMMY" });
                             dt.AddRowAsync(new Row(dt.Schema, new object[] { 1 })).GetAwaiter().GetResult();
                             m.SeededResult = dt;
                             return m;
                         });
            connectors.Setup(c => c.GetConnector("MOCKDB")).Returns(mockConnector.Object);

            // Mock Lineage Tracker to avoid NRE
            string dummy;
            tracker.Setup(t => t.InheritMetadata(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), out dummy))
                   .Returns(new Dictionary<string, string>());

            var pushdown = new ExecutePushdownStatementHandler(logger.Object);
            var handlers = new List<IStatementHandler> 
            { 
                new SelectStatementHandler(logger.Object),
                new CreateConnectionStatementHandler(connectors.Object, logger.Object),
                new CreateTableStatementHandler(logger.Object),
                new InsertStatementHandler(logger.Object, pushdown),
                new GenerateStatementHandler(logger.Object)
            };

            var evaluator = new Evaluator(
                handlers, 
                services.Object, 
                functions.Object, 
                tracker.Object, 
                docker.Object, 
                connectors.Object, 
                sessions.Object, 
                security, 
                logger.Object, 
                languageHelp.Object, 
                registry,
                variableScopeManager: variableScopeManager);

            registry.Initialize(evaluator, logger.Object, variableScopeManager);
            
            // Disable cache for baseline test
            // evaluator.Options.SubqueryCacheSize = 0; 
            
            return evaluator;
        }

        private Statement Parse(string sql)
        {
            if (!sql.TrimEnd().EndsWith(";")) sql += ";";
            var script = TestHelpers.Parse(sql);
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var errors = string.Join("; ", script.Diagnostics.Select(d => d.Message));
                throw new System.Exception($"SQL Parse Error: {errors} in [{sql}]");
            }
            if (!script.Statements.Any())
            {
                throw new System.Exception($"No statements parsed from [{sql}]");
            }
            return script.Statements.First();
        }

        [Fact]
        public async Task Evaluator_SupportsSubqueryWithGroupBy()
        {
            var evaluator = CreateEvaluator();
            await evaluator.EvaluateStatement(Parse("CREATE CONNECTION src ON MOCKDB();"));
            
            // Populate #data using DUAL to avoid MOCKDB pushdown side-effects for literals
            await evaluator.EvaluateStatement(Parse("SELECT 1 as id, 'A' as cat, 10.0 as val INTO #data FROM DUAL;"));
            await evaluator.EvaluateStatement(Parse("INSERT INTO #data(id, cat, val) SELECT 2, 'A', 20.0 FROM DUAL;"));
            await evaluator.EvaluateStatement(Parse("INSERT INTO #data(id, cat, val) SELECT 3, 'B', 30.0 FROM DUAL;"));

            // Verify #data content
            var dataResult = await evaluator.EvaluateSelect((SelectStatement)Parse("SELECT * FROM #data;")).ToListAsync();
            var dataRows = dataResult.SelectMany(b => b.Rows).ToList();
            Assert.Equal(3, dataRows.Count);

            // Subquery with GROUP BY
            var sql = "SELECT cat, (SELECT SUM(val) FROM #data d2 WHERE d2.cat = #data.cat GROUP BY cat) as total FROM #data;";
            var select = (SelectStatement)Parse(sql);
            var result = await evaluator.EvaluateSelect(select).ToListAsync();

            var rows = result.SelectMany(b => b.Rows).ToList();
            Assert.Equal(3, rows.Count);
            
            // Normalize to decimal for comparison
            Assert.Equal(30m, Convert.ToDecimal(rows[0]["total"])); // A: 10 + 20
            Assert.Equal(30m, Convert.ToDecimal(rows[1]["total"])); // A: 10 + 20
            Assert.Equal(30m, Convert.ToDecimal(rows[2]["total"])); // B: 30
        }

        [Fact]
        public async Task Evaluator_SupportsSubqueryWithWindowFunction()
        {
            var evaluator = CreateEvaluator();
            await evaluator.EvaluateStatement(Parse("CREATE CONNECTION src ON MOCKDB();"));
            await evaluator.EvaluateStatement(Parse("SELECT 1 as id, 10.0 as val INTO #data FROM src;"));
            await evaluator.EvaluateStatement(Parse("INSERT INTO #data SELECT 2, 20.0 FROM src;"));

            // Subquery with Window Function (ROW_NUMBER)
            var sql = "SELECT id, (SELECT ROW_NUMBER() OVER(ORDER BY val) FROM #data d2 WHERE d2.id = #data.id) as rn FROM #data;";
            var select = (SelectStatement)Parse(sql);
            var result = await evaluator.EvaluateSelect(select).ToListAsync();

            var rows = result.SelectMany(b => b.Rows).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal(1L, Convert.ToInt64(rows[0]["rn"]));
            Assert.Equal(1L, Convert.ToInt64(rows[1]["rn"]));
        }

        [Fact]
        public async Task Evaluator_SupportsNestedCorrelatedSubqueries()
        {
            var evaluator = CreateEvaluator();
            await evaluator.EvaluateStatement(Parse("CREATE CONNECTION src ON MOCKDB();"));
            await evaluator.EvaluateStatement(Parse("SELECT 1 as a INTO #t1 FROM src;"));
            await evaluator.EvaluateStatement(Parse("SELECT 1 as b INTO #t2 FROM src;"));
            await evaluator.EvaluateStatement(Parse("SELECT 1 as c INTO #t3 FROM src;"));

            // Double nested correlation: t3 -> t2 -> t1
            var sql = "SELECT a, (SELECT b FROM #t2 WHERE b = a AND (SELECT c FROM #t3 WHERE c = b) = 1) as res FROM #t1;";
            var select = (SelectStatement)Parse(sql);
            var result = await evaluator.EvaluateSelect(select).ToListAsync();

            var rows = result.SelectMany(b => b.Rows).ToList();
            Assert.Single(rows);
            Assert.Equal(1m, Convert.ToDecimal(rows[0]["res"]));
            
            // Check telemetry
            Assert.True(evaluator.Telemetry.SubqueryCacheMisses >= 2);
        }

        private class PersistentMockDatabaseSource : IDatabaseSource
        {
            public DataTable SeededResult { get; set; }
            public string Dialect => "MSSQL";
            public bool SupportsSqlPushdown => true;
            public string ConnectionString => "mock://local";
            public string Path => "mock://local";
            public Dictionary<string, string>? Options => null;
            public string ConnectorType => "MOCK";

            public IAsyncEnumerable<DataTable> ExecuteRawSql(string sql, IEnumerable<object?>? parameters = null)
            {
                return System.Linq.AsyncEnumerable.ToAsyncEnumerable(new[] { SeededResult.Clone() });
            }

            public Task<string> GetVersionAsync() => Task.FromResult("Mock 1.0");
            public HashSet<string> GetSupportedFunctions() => new();
            public Task<IEnumerable<string>> GetTablesAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetViewsAsync() => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(string tableName) => Task.FromResult(Enumerable.Empty<string>());
            public IAsyncEnumerable<DataTable> ReadBatches(int batchSize = 10000) => System.Linq.AsyncEnumerable.ToAsyncEnumerable(new[] { SeededResult.Clone() });
            public async Task WriteBatches(IAsyncEnumerable<DataTable> batches, bool append = false) { await foreach (var b in batches) {} }
            public Task<IEnumerable<string>> GetColumnsAsync() => Task.FromResult(SeededResult.Schema.ColumnNames.AsEnumerable());
            public object? Snapshot() => null;
            public void Restore(object? snapshot) { }
            public IDataSource WithTable(string tableName) => this;
            public async ValueTask DisposeAsync() => await Task.CompletedTask;
        }
    }
}
