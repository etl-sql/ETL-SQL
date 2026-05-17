using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtApproxCountDistinctTests
    {
        [Fact]
        public async Task ApproxCountDistinctEstimatesDistinctNonNullValues()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT APPROX_COUNT_DISTINCT(id) AS approx_ids
                FROM (VALUES (1), (1), (2), (3), (NULL)) AS v(id);";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();
            var estimate = Convert.ToDecimal(res.Rows[0]["approx_ids"]);

            Assert.InRange(estimate, 2.5m, 3.5m);
        }

        [Fact]
        public async Task ApproxCountDistinctWorksWithGroupByAndFilter()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = @"
                SELECT region,
                       APPROX_COUNT_DISTINCT(customer_id) FILTER (WHERE active = TRUE) AS active_customers
                FROM (VALUES
                    ('North', 1, TRUE),
                    ('North', 1, TRUE),
                    ('North', 2, FALSE),
                    ('North', 3, TRUE),
                    ('South', 10, TRUE),
                    ('South', 11, TRUE),
                    ('South', 11, TRUE)
                ) AS v(region, customer_id, active)
                GROUP BY region
                ORDER BY region;";

            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Equal("North", res.Rows[0]["region"]);
            Assert.InRange(Convert.ToDecimal(res.Rows[0]["active_customers"]), 1.5m, 2.5m);
            Assert.Equal("South", res.Rows[1]["region"]);
            Assert.InRange(Convert.ToDecimal(res.Rows[1]["active_customers"]), 1.5m, 2.5m);
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
