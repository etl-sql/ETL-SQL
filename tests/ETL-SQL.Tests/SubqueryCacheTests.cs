using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests
{
    public class SubqueryCacheTests
    {
        [Fact]
        public async Task Subquery_ShouldReevaluate_WhenGlobalVariableChanges()
        {
            // Setup
            var logger = new Mock<ILogger>();
            var registry = new Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            var tracker = new LineageTracker(logger.Object);
            var docker = new Mock<IDockerManager>();
            var connectors = new Mock<IConnectorRegistry>();
            var sessions = new Mock<ISessionStateManager>();
            var security = new SecurityService(logger.Object);
            security.IsTestMode = true;
            var services = new Mock<IServiceProvider>();

            var handlers = new List<IStatementHandler>();
            var selectHandler = new SelectStatementHandler(logger.Object);
            handlers.Add(selectHandler);
            handlers.Add(new DeclareStatementHandler(logger.Object));
            handlers.Add(new SetVariableStatementHandler(logger.Object));

            var evaluator = new Evaluator(handlers, services.Object, registry.Object, tracker, docker.Object, connectors.Object, sessions.Object, security, logger.Object, new ETL_SQL.Core.Metadata.LanguageHelpRegistry(), new EvaluatorComponentRegistry());

            // 1. Initial run: @val = 1
            await evaluator.Evaluate(Parse("DECLARE @val = 1; SELECT 1 AS X INTO #t;"));

            // Query using subquery with variable
            var sql = "SELECT X FROM #t WHERE X IN (SELECT @val)";

            await evaluator.Evaluate(Parse(sql));
            Assert.Single(evaluator.LastResult.Rows);
            Assert.Equal("1", evaluator.LastResult.Rows[0][0].ToString());

            // 2. Change variable: @val = 2
            await evaluator.Evaluate(Parse("SET @val = 2;"));

            // Run same query again
            await evaluator.Evaluate(Parse(sql));

            // If bug exists, it will return 1 row (cached 1). 
            // If fixed, it will return 0 rows (since X=1 but @val=2).
            Assert.Empty(evaluator.LastResult.Rows);

            // 3. Change variable back: @val = 1
            await evaluator.Evaluate(Parse("SET @val = 1;"));
            await evaluator.Evaluate(Parse(sql));
            Assert.Single(evaluator.LastResult.Rows);
        }

        private Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens, sql).Parse();
        }
    }
}
