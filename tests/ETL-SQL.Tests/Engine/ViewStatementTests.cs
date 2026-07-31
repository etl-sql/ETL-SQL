using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class ViewStatementTests
    {
        private static Evaluator Eval() =>
            DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        private static Script Parse(string sql) =>
            new Parser(new Lexer(sql).Tokenize()).Parse();

        private static async Task<Evaluator> Run(string sql)
        {
            var eval = Eval();
            await eval.Evaluate(Parse(sql));
            return eval;
        }

        [Fact]
        public async Task CreateView_SelectFromView_EvaluatesQueryAtReadTime()
        {
            var eval = await Run(@"
CREATE TABLE #orders (id INT, amount INT);
INSERT INTO #orders VALUES (1, 10), (2, 25), (3, 40);
CREATE VIEW LargeOrders AS SELECT id, amount FROM #orders WHERE amount >= 25;
SELECT id, amount FROM LargeOrders ORDER BY id;
");

            Assert.NotNull(eval.LastResult);
            Assert.Equal(2, eval.LastResult!.Rows.Count);
            Assert.Equal(2m, Convert.ToDecimal(eval.LastResult.Rows[0]["id"]));
            Assert.Equal(40m, Convert.ToDecimal(eval.LastResult.Rows[1]["amount"]));
        }

        [Fact]
        public async Task CreateOrAlterView_ReplacesDefinition()
        {
            var eval = await Run(@"
CREATE TABLE #orders (id INT, amount INT);
INSERT INTO #orders VALUES (1, 10), (2, 25);
CREATE VIEW LargeOrders AS SELECT id FROM #orders WHERE amount >= 20;
CREATE OR ALTER VIEW LargeOrders AS SELECT id FROM #orders WHERE amount >= 10;
SELECT id FROM LargeOrders ORDER BY id;
");

            Assert.Equal(2, eval.LastResult!.Rows.Count);
        }

        [Fact]
        public async Task ShowViews_IntoTempTable_ListsRegisteredViews()
        {
            var eval = await Run(@"
CREATE VIEW LargeOrders AS SELECT id FROM #orders;
SELECT * INTO #views FROM eng.views;
SELECT view_name FROM #views;
");

            Assert.Single(eval.LastResult!.Rows);
            Assert.Equal("LargeOrders", eval.LastResult.Rows[0]["view_name"]);
        }

        [Fact]
        public async Task DropView_RemovesView()
        {
            await Run("CREATE VIEW V AS SELECT 1 AS n; DROP VIEW V; DROP VIEW IF EXISTS V;");
        }

        [Fact]
        public async Task RecursiveViewReference_ThrowsClearError()
        {
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(@"
CREATE VIEW A AS SELECT * FROM B;
CREATE VIEW B AS SELECT * FROM A;
SELECT * FROM A;
"));

            Assert.Contains("Recursive view reference", ex.Message);
        }

        [Fact]
        public async Task View_AsDmlTarget_ThrowsReadOnlyError()
        {
            var ex = await Assert.ThrowsAsync<ExecutionException>(() => Run(@"
CREATE TABLE #orders (id INT);
CREATE VIEW OrderView AS SELECT id FROM #orders;
INSERT INTO OrderView VALUES (1);
"));

            Assert.Contains("read-only", ex.Message);
        }

        [Fact]
        public async Task CreateVisual_AllowsViewSource()
        {
            var eval = await Run(@"
CREATE VIEW SalesView AS SELECT region, amount FROM #sales;
CREATE VISUAL SalesTable AS TABLE (
    SOURCE = SalesView
);
");

            Assert.True(eval.ReportContext.VisualDefinitions.ContainsKey("SalesTable"));
        }
    }
}
