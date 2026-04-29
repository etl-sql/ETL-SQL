using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Data;
using ETL_SQL.Engine;
using ETL_SQL.Core.Parser;
using Xunit;
using ETL_SQL.Data;
using ETL_SQL.Core.Common;
using ETL_SQL.Common;
using ETL_SQL.Services;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using System.IO;
using ETL_SQL.Core;
using ETL_SQL.Core.Execution;
using ETL_SQL.Tests.Core;
using Moq;

namespace ETL_SQL.Tests.Engine
{
    public class SubqueryCacheTests : IDisposable
    {
        private Evaluator _evaluator;
        private readonly string _testSessionPath;

        public SubqueryCacheTests()
        {
            _testSessionPath = Path.Combine(Path.GetTempPath(), "ETL_SQL_SubqueryCacheTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testSessionPath);
            _evaluator = CreateEvaluator(10);
        }

        private Evaluator CreateEvaluator(int cacheSize)
        {
            var logger = new Moq.Mock<ILogger>();
            var services = new Moq.Mock<IServiceProvider>();
            var functions = new Moq.Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            var tracker = new Moq.Mock<ILineageTracker>();
            var docker = new Moq.Mock<IDockerManager>();
            var connectors = new Moq.Mock<IConnectorRegistry>();
            var security = new SecurityService(logger.Object);
            security.IsTestMode = true;
            var sessions = new Moq.Mock<ISessionStateManager>();
            var languageHelp = new Moq.Mock<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry>();
            var variableScopeManager = new VariableScopeManager();
            
            var registry = new EvaluatorComponentRegistry();

            var handlers = new List<IStatementHandler> { 
                new SelectStatementHandler(logger.Object),
                new SetVariableStatementHandler(logger.Object),
                new DeclareStatementHandler(logger.Object),
                new GenerateStatementHandler(logger.Object),
                new CreateTableStatementHandler(logger.Object),
                new InsertStatementHandler(logger.Object, new ExecutePushdownStatementHandler(logger.Object))
            };

            var options = new EvaluatorOptions { SubqueryCacheSize = cacheSize };

            var eval = new Evaluator(
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
                variableScopeManager: variableScopeManager,
                options: options)
            {
                SessionRoot = _testSessionPath
            };
            
            // Mock lineage
            string dummy;
            tracker.Setup(t => t.InheritMetadata(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(), out dummy))
                   .Returns(new Dictionary<string, string>());

            return eval;
        }

        public void Dispose()
        {
            if (Directory.Exists(_testSessionPath)) Directory.Delete(_testSessionPath, true);
        }

        [Fact]
        public async Task ScalarCorrelation_WithNulls_CorrectlyCaches()
        {
            await Run(@"
                CREATE TABLE #T1 (ID INT, Val STRING);
                INSERT INTO #T1 (ID, Val) VALUES (1, 'A'), (2, NULL), (3, 'C');
            ");

            var results1 = await Execute("SELECT ID, (SELECT Val FROM #T1 AS Sub WHERE Sub.ID = #T1.ID) as V FROM #T1;");
            
            Assert.Equal(3, results1.Count);
            Assert.Equal("A", results1[0]["V"]);
            Assert.True(results1[1]["V"].IsNull());
            Assert.Equal("C", results1[2]["V"]);
            
            Assert.Equal(3, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(0, _evaluator.Telemetry.SubqueryCacheHits);

            var results2 = await Execute("SELECT ID, (SELECT Val FROM #T1 AS Sub WHERE Sub.ID = #T1.ID) as V FROM #T1;");
            Assert.Equal(3, results2.Count);
            Assert.Equal(3, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(3, _evaluator.Telemetry.SubqueryCacheHits);
        }

        [Fact]
        public async Task MultiVariableCorrelation_CorrectlyCaches()
        {
            await Run(@"
                CREATE TABLE #T1 (A INT, B INT, Res INT);
                INSERT INTO #T1 (A, B, Res) VALUES (1, 1, 11), (1, 2, 12), (1, 1, 11);
            ");

            await Run("SELECT (SELECT Res FROM #T1 AS Sub WHERE Sub.A = #T1.A AND Sub.B = #T1.B) as V FROM #T1;");
            
            // 3 rows. Row 1: miss. Row 2: miss. Row 3 (1,1): hit.
            Assert.Equal(2, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(1, _evaluator.Telemetry.SubqueryCacheHits);
        }

        [Fact]
        public async Task LruEviction_RespectsCacheSize()
        {
            // Use a fresh evaluator for this test to ensure cache size is applied
            var localEval = CreateEvaluator(2);
            
            await Run(localEval, @"
                CREATE TABLE #T1 (ID INT);
                GENERATE 5 ROWS INTO #T1 AS (ID = 'SEQUENCE(1,1)');
            ");

            await Run(localEval, "SELECT (SELECT ID FROM #T1 AS Sub WHERE Sub.ID = #T1.ID) as V FROM #T1;");
            Assert.Equal(5, localEval.Telemetry.SubqueryCacheMisses);

            // Re-run for ID 5 (should be hit)
            await Run(localEval, "SELECT (SELECT ID FROM #T1 AS Sub WHERE Sub.ID = #T1.ID) as V FROM #T1 WHERE #T1.ID = 5;");
            Assert.Equal(1, localEval.Telemetry.SubqueryCacheHits);

            // Re-run for ID 1 (should be MISS because it was evicted)
            await Run(localEval, "SELECT (SELECT ID FROM #T1 AS Sub WHERE Sub.ID = #T1.ID) as V FROM #T1 WHERE #T1.ID = 1;");
            Assert.Equal(6, localEval.Telemetry.SubqueryCacheMisses);
        }

        [Fact]
        public async Task NestedSubquery_CorrectlyCaches()
        {
            await Run(@"
                CREATE TABLE #T1 (ID INT, Val STRING);
                INSERT INTO #T1 (ID, Val) VALUES (1, 'A'), (2, 'B');
            ");

            // Outer loop: 2 rows.
            // Mid subquery: called 2 times.
            // Inner subquery: called 2 times.
            // Total subqueries: 4. All should be misses if they are all unique.
            // BUT wait, the Mid subquery is correlated to Outer. The Inner subquery is correlated to Mid (and potentially Outer).
            
            await Run("SELECT (SELECT (SELECT Val FROM #T1 AS InnerT WHERE InnerT.ID = Mid.ID) FROM #T1 AS Mid WHERE Mid.ID = #T1.ID) as V FROM #T1;");
            
            // Outer row 1 (ID=1): Mid(1) [miss], Inner(1) [miss]
            // Outer row 2 (ID=2): Mid(2) [miss], Inner(2) [miss]
            Assert.Equal(4, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(0, _evaluator.Telemetry.SubqueryCacheHits);

            // Second run
            await Run("SELECT (SELECT (SELECT Val FROM #T1 AS InnerT WHERE InnerT.ID = Mid.ID) FROM #T1 AS Mid WHERE Mid.ID = #T1.ID) as V FROM #T1;");
            Assert.Equal(4, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(2, _evaluator.Telemetry.SubqueryCacheHits);
        }

        [Fact]
        public async Task ComplexLogic_CorrectlyCaches()
        {
            await Run(@"
                CREATE TABLE #T1 (ID INT, Val INT);
                INSERT INTO #T1 (ID, Val) VALUES (1, 10), (1, 20), (2, 30);
            ");

            // SELECT ID, (SELECT MAX(Val) FROM #T1 AS Sub WHERE Sub.ID = #T1.ID) FROM #T1
            // Row 1 (ID=1): Miss.
            // Row 2 (ID=1): Hit.
            // Row 3 (ID=2): Miss.
            await Run("SELECT ID, (SELECT MAX(Sub.Val) FROM #T1 AS Sub WHERE Sub.ID = #T1.ID) as V FROM #T1;");
            
            Assert.Equal(2, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(1, _evaluator.Telemetry.SubqueryCacheHits);
        }

        [Fact]
        public async Task StaticSubquery_CorrectlyCaches()
        {
            await Run(@"
                CREATE TABLE #T1 (ID INT);
                INSERT INTO #T1 (ID) VALUES (1), (2), (3);
            ");

            // Non-correlated subquery
            await Run("SELECT ID, (SELECT COUNT(*) FROM #T1) as Total FROM #T1;");
            
            // Row 1: Miss.
            // Row 2: Hit.
            // Row 3: Hit.
            Assert.Equal(1, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(2, _evaluator.Telemetry.SubqueryCacheHits);
        }

        private async Task Run(string sql) => await Run(_evaluator, sql);
        
        private async Task Run(Evaluator eval, string sql)
        {
            var script = TestHelpers.Parse(sql);
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                throw new Exception($"Syntax Error: {script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message}");
            }
            foreach (var stmt in script.Statements)
            {
                await eval.EvaluateStatement(stmt);
            }
        }

        private async Task<List<Row>> Execute(string sql) => await Execute(_evaluator, sql);

        private async Task<List<Row>> Execute(Evaluator eval, string sql)
        {
            var script = TestHelpers.Parse(sql);
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                throw new Exception($"Syntax Error: {script.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error).Message}");
            }
            var lastSelect = (SelectStatement)script.Statements.Last();
            
            var results = new List<Row>();
            await foreach (var batch in eval.EvaluateSelect(lastSelect))
            {
                results.AddRange(batch.Rows);
            }
            return results;
        }
    }
}
