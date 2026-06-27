using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtDuckPivotTests
    {
        [Fact]
        public async Task DuckPivot_DynamicDiscovery_SingleAggregate()
        {
            var rows = await Run(@"
                PIVOT (VALUES ('N','Q1',10), ('N','Q2',20), ('S','Q1',5)) AS t(region, quarter, amount)
                ON quarter USING SUM(amount);");

            Assert.Equal(2, rows.Count);
            var north = Find(rows, "region", "N");
            Assert.Equal(10m, north["Q1"]);
            Assert.Equal(20m, north["Q2"]);
            var south = Find(rows, "region", "S");
            Assert.Equal(5m, south["Q1"]);
            Assert.Null(south["Q2"]);
        }

        [Fact]
        public async Task DuckPivot_ExplicitInAndGroupBy()
        {
            var rows = await Run(@"
                PIVOT (VALUES ('N','Q1',10), ('N','Q2',20), ('S','Q1',5)) AS t(region, quarter, amount)
                ON quarter IN ('Q1','Q2') USING SUM(amount) GROUP BY region;");

            Assert.Equal(2, rows.Count);
            Assert.Equal(10m, Find(rows, "region", "N")["Q1"]);
            Assert.Equal(20m, Find(rows, "region", "N")["Q2"]);
        }

        [Fact]
        public async Task DuckPivot_MultipleAggregates_GetCompositeNames()
        {
            var rows = await Run(@"
                PIVOT (VALUES ('N','Q1',10), ('N','Q1',5)) AS t(region, quarter, amount)
                ON quarter USING SUM(amount) AS total, COUNT(*) AS cnt;");

            Assert.Single(rows);
            Assert.Equal(15m, rows[0]["Q1_total"]);
            Assert.Equal(2m, rows[0]["Q1_cnt"]);
        }

        [Fact]
        public async Task DuckUnpivot_ExplicitColumns()
        {
            var rows = await Run(@"
                UNPIVOT (VALUES ('N', 10, 20)) AS t(region, q1, q2)
                ON q1, q2 INTO NAME quarter VALUE amount;");

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal("N", r["region"]));
            Assert.Equal(10m, Find(rows, "quarter", "q1")["amount"]);
            Assert.Equal(20m, Find(rows, "quarter", "q2")["amount"]);
        }

        [Fact]
        public async Task DuckUnpivot_ColumnsExclude()
        {
            var rows = await Run(@"
                UNPIVOT (VALUES ('N', 10, 20)) AS t(region, q1, q2)
                ON COLUMNS(* EXCLUDE (region)) INTO NAME quarter VALUE amount;");

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal("N", r["region"]));
            Assert.Equal(10m, Find(rows, "quarter", "q1")["amount"]);
            Assert.Equal(20m, Find(rows, "quarter", "q2")["amount"]);
        }

        [Fact]
        public void DuckPivotUnpivot_OperatorsSerializeToReadableForm()
        {
            var pivotStmt = (SelectStatement)Parse("PIVOT sales ON quarter USING SUM(amount) GROUP BY region;").Statements[0];
            var pivot = pivotStmt.FromTable.TableOperators[0].ToSql();
            Assert.Contains("PIVOT ON", pivot);
            Assert.Contains("USING", pivot);
            Assert.Contains("GROUP BY", pivot);

            var unpivotStmt = (SelectStatement)Parse("UNPIVOT sales ON COLUMNS(* EXCLUDE (region)) INTO NAME quarter VALUE amount;").Statements[0];
            var unpivot = unpivotStmt.FromTable.TableOperators[0].ToSql();
            Assert.Contains("UNPIVOT", unpivot);
            Assert.Contains("EXCLUDE", unpivot);
        }

        private static async Task<List<Row>> Run(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            return batches.SelectMany(b => b.Rows).ToList();
        }

        private static Row Find(List<Row> rows, string col, object value) =>
            rows.First(r => r[col]?.ToString() == value.ToString());

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
