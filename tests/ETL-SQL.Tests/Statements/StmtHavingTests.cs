using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class HavingTests
    {
        private Evaluator GetEvaluator()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            ev.Connections["#data"] = new InMemoryDataSource();
            return ev;
        }

        private Script Parse(string sql)
        {
            return new Parser(new Lexer(sql).Tokenize()).Parse();
        }

        [Fact]
        public async Task TestHavingCount()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Category STRING, Price INT);
                INSERT INTO #data (Category, Price) VALUES ('A', 10), ('A', 20), ('B', 5);
                SELECT Category, COUNT(*) AS Cnt FROM #data GROUP BY Category HAVING COUNT(*) > 1;
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.Equal("A", result.Rows[0]["Category"]);
            Assert.Equal(2m, result.Rows[0]["Cnt"]);
        }

        [Fact]
        public async Task TestHavingSum()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Category STRING, Price INT);
                INSERT INTO #data (Category, Price) VALUES ('A', 10), ('A', 20), ('B', 50), ('B', 60);
                SELECT Category, SUM(Price) AS Total FROM #data GROUP BY Category HAVING SUM(Price) > 100;
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.Equal("B", result.Rows[0]["Category"]);
            Assert.Equal(110m, result.Rows[0]["Total"]);
        }

        [Fact]
        public async Task TestHavingWithoutSelect()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Category STRING, Price INT);
                INSERT INTO #data (Category, Price) VALUES ('A', 10), ('A', 20), ('B', 5);
                SELECT Category FROM #data GROUP BY Category HAVING COUNT(*) > 1;
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.Equal("A", result.Rows[0]["Category"]);
            // The column name is "Category" in the result row
            Assert.False(result.Rows[0].Columns.ContainsKey("Cnt"));
        }

        [Fact]
        public async Task TestHavingComplex()
        {
            var ev = GetEvaluator();
            await ev.Evaluate(Parse(@"
                CREATE TABLE #data (Category STRING, Price INT);
                INSERT INTO #data (Category, Price) VALUES ('A', 10), ('A', 20), ('B', 100), ('B', 100);
                SELECT Category FROM #data GROUP BY Category HAVING COUNT(*) > 1 AND AVG(Price) > 50;
            "));

            var result = ev.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.Equal("B", result.Rows[0]["Category"]);
        }
    }
}
