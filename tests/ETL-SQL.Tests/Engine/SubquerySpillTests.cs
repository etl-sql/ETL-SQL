using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class SubquerySpillTests : IDisposable
    {
        private readonly Evaluator _evaluator;
        private readonly string _testSessionPath;

        public SubquerySpillTests()
        {
            _testSessionPath = Path.Combine(Path.GetTempPath(), "ETL_SQL_SubquerySpillTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testSessionPath);

            var logger = new Moq.Mock<ILogger>();
            var services = new Moq.Mock<IServiceProvider>();
            var functions = new Moq.Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            var tracker = new Moq.Mock<ILineageTracker>();
            var docker = new Moq.Mock<IDockerManager>();
            var connectors = new Moq.Mock<IConnectorRegistry>();
            var security = new SecurityService(logger.Object);
            var sessions = new Moq.Mock<ISessionStateManager>();
            var languageHelp = new Moq.Mock<ETL_SQL.Core.Interfaces.ILanguageHelpRegistry>();
            var variableScopeManager = new VariableScopeManager();

            var registry = new EvaluatorComponentRegistry();

            var handlers = new List<IStatementHandler> { new SelectStatementHandler(logger.Object) };

            _evaluator = new Evaluator(
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
                variableScopeManager: variableScopeManager)
            {
                SessionRoot = _testSessionPath,
                SubquerySpillThresholdRows = 100 // Low threshold for testing
            };

            registry.Initialize(_evaluator, logger.Object, variableScopeManager);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testSessionPath)) Directory.Delete(_testSessionPath, true);
        }

        [Fact]
        public async Task InClause_SpillsToDisk_WhenLarge()
        {
            // Setup: Create a temp table with many rows
            var columnNames = new[] { "ID" };
            var schema = new TableSchema(columnNames);
            var source = new InMemoryDataSource();
            source.SetSchema(columnNames.Select(c => new ColumnDefinition(c, "INT", false)).ToList());

            var rows = new List<Row>();
            for (int i = 0; i < 500; i++) rows.Add(new Row(schema, new object[] { i }));
            var dt = new DataTable { Schema = schema };
            dt.Rows.AddRange(rows);
            await source.WriteBatches(AsyncEnumerable.ToAsyncEnumerable(new[] { dt }));

            _evaluator.Connections["SourceTable"] = source;

            // Query: SELECT SourceTable.ID FROM SourceTable WHERE SourceTable.ID IN (SELECT SourceTable.ID FROM SourceTable)
            var selectSubq = new SelectStatement(
                new List<SelectColumn> { new SelectColumn(new IdentifierExpression("SourceTable.ID")) },
                null,
                new TableReference("SourceTable"),
                new List<JoinClause>(),
                null
            );

            var inExpr = new InExpression(new LiteralExpression(250m, TokenType.NUMBER), new SubqueryExpression(selectSubq), false);
            var contextRow = new Row(new TableSchema(new string[0]), new object[0]);

            // First execution (Cache Miss)
            var result = await _evaluator.ExpressionEvaluator.EvaluateInternal(inExpr, contextRow);
            Assert.True((bool)result!);
            Assert.Equal(1, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(0, _evaluator.Telemetry.SubqueryCacheHits);

            // Verify it used StreamData (spilled) because 500 > 100
            var key = new SubqueryCacheKey(selectSubq, new CompoundKey(new object[0]), SubqueryResultType.Stream);
            Assert.True(_evaluator.SubqueryCache.TryGetValue(key, out var cached));
            Assert.NotNull(cached!.StreamData);
            Assert.Null(cached.InSet);

            // Second execution (Cache Hit)
            var result2 = await _evaluator.ExpressionEvaluator.EvaluateInternal(inExpr, contextRow);
            Assert.True((bool)result2!);
            Assert.Equal(1, _evaluator.Telemetry.SubqueryCacheMisses);
            Assert.Equal(1, _evaluator.Telemetry.SubqueryCacheHits);
        }

        [Fact]
        public async Task InClause_UsesHashSet_WhenSmall()
        {
            // Setup: Small table
            var columnNames = new[] { "ID" };
            var schema = new TableSchema(columnNames);
            var source = new InMemoryDataSource();
            source.SetSchema(columnNames.Select(c => new ColumnDefinition(c, "INT", false)).ToList());

            var rows = new List<Row>();
            for (int i = 0; i < 50; i++) rows.Add(new Row(schema, new object[] { i }));
            var dt = new DataTable { Schema = schema };
            dt.Rows.AddRange(rows);
            await source.WriteBatches(AsyncEnumerable.ToAsyncEnumerable(new[] { dt }));

            _evaluator.Connections["SmallTable"] = source;

            var selectSubq = new SelectStatement(
                new List<SelectColumn> { new SelectColumn(new IdentifierExpression("SmallTable.ID")) },
                null,
                new TableReference("SmallTable"),
                new List<JoinClause>(),
                null
            );

            var inExpr = new InExpression(new LiteralExpression(25m, TokenType.NUMBER), new SubqueryExpression(selectSubq), false);
            var contextRow = new Row(new TableSchema(new string[0]), new object[0]);

            // Execution
            await _evaluator.ExpressionEvaluator.EvaluateInternal(inExpr, contextRow);

            // Verify it used InSet (HashSet) because 50 < 100
            var key = new SubqueryCacheKey(selectSubq, new CompoundKey(new object[0]), SubqueryResultType.Stream);
            Assert.True(_evaluator.SubqueryCache.TryGetValue(key, out var cached));
            Assert.NotNull(cached!.InSet);
            Assert.Null(cached.StreamData);
        }

        [Fact]
        public async Task Exists_CachesResult()
        {
            var columnNames = new[] { "ID" };
            var schema = new TableSchema(columnNames);
            var source = new InMemoryDataSource();
            source.SetSchema(columnNames.Select(c => new ColumnDefinition(c, "INT", false)).ToList());
            var dt = new DataTable { Schema = schema };
            dt.Rows.Add(new Row(schema, new object[] { 1 }));
            await source.WriteBatches(AsyncEnumerable.ToAsyncEnumerable(new[] { dt }));
            _evaluator.Connections["T1"] = source;

            var selectSubq = new SelectStatement(
                new List<SelectColumn> { new SelectColumn(new IdentifierExpression("T1.ID")) },
                null,
                new TableReference("T1"),
                new List<JoinClause>(),
                null
            );

            var existsExpr = new ExistsExpression(selectSubq, false);
            var contextRow = new Row(new TableSchema(new string[0]), new object[0]);

            // First
            await _evaluator.ExpressionEvaluator.EvaluateInternal(existsExpr, contextRow);
            Assert.Equal(1, _evaluator.Telemetry.SubqueryCacheMisses);

            // Second
            await _evaluator.ExpressionEvaluator.EvaluateInternal(existsExpr, contextRow);
            Assert.Equal(1, _evaluator.Telemetry.SubqueryCacheHits);
        }
    }
}
