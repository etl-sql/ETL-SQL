using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtFilteredAggregateTests
    {
        [Fact]
        public async Task FilteredAggregatesApplyFilterPerAggregate()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT
                    SUM(amount) FILTER (WHERE region = 'North') AS north_sum,
                    COUNT(*) FILTER (WHERE amount >= 20) AS high_count,
                    AVG(amount) FILTER (WHERE region = 'South') AS south_avg
                FROM (VALUES
                    ('North', 10),
                    ('North', 30),
                    ('South', 20),
                    ('South', NULL)
                ) AS sales(region, amount);";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal(40m, res.Rows[0]["north_sum"]);
            Assert.Equal(2m, res.Rows[0]["high_count"]);
            Assert.Equal(20m, res.Rows[0]["south_avg"]);
        }

        [Fact]
        public async Task FilteredAggregatesWorkWithGroupBy()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT region,
                       COUNT(*) FILTER (WHERE amount >= 20) AS high_count,
                       SUM(amount) FILTER (WHERE amount < 20) AS low_sum
                FROM (VALUES
                    ('North', 10),
                    ('North', 30),
                    ('South', 5),
                    ('South', 25)
                ) AS sales(region, amount)
                GROUP BY region
                ORDER BY region;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal("North", res.Rows[0]["region"]);
            Assert.Equal(1m, res.Rows[0]["high_count"]);
            Assert.Equal(10m, res.Rows[0]["low_sum"]);
            Assert.Equal("South", res.Rows[1]["region"]);
            Assert.Equal(1m, res.Rows[1]["high_count"]);
            Assert.Equal(5m, res.Rows[1]["low_sum"]);
        }

        [Fact]
        public async Task FilteredAggregatesWorkInHaving()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT region
                FROM (VALUES
                    ('North', 10),
                    ('North', 30),
                    ('South', 5),
                    ('South', 25)
                ) AS sales(region, amount)
                GROUP BY region
                HAVING SUM(amount) FILTER (WHERE amount >= 20) > 25;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Single(res.Rows);
            Assert.Equal("North", res.Rows[0]["region"]);
        }

        [Fact]
        public async Task FilteredAggregatesReturnEmptyAggregateValuesWhenNoRowsPass()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT
                    COUNT(*) FILTER (WHERE amount > 100) AS no_count,
                    SUM(amount) FILTER (WHERE amount > 100) AS no_sum
                FROM (VALUES (10), (20)) AS sales(amount);";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal(0m, res.Rows[0]["no_count"]);
            Assert.Null(res.Rows[0]["no_sum"]);
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
