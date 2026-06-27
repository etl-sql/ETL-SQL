using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    // Verification of previously-untested PIVOT/UNPIVOT claims.
    public class StmtDuckPivotVerifyTests
    {
        [Fact]
        public async Task MultipleOnColumns_ProduceCompositeColumnNames()
        {
            var batch = await RunFirstBatch(@"
                PIVOT (VALUES ('N',2000,'Q1',10), ('N',2000,'Q2',20), ('N',2001,'Q1',5)) AS t(region, yr, quarter, amount)
                ON yr, quarter USING SUM(amount);");

            var rows = batch.Rows;
            Assert.Single(rows);
            Assert.Contains("2000_Q1", batch.ColumnNames);
            Assert.Contains("2000_Q2", batch.ColumnNames);
            Assert.Contains("2001_Q1", batch.ColumnNames);
            Assert.Equal(10m, rows[0]["2000_Q1"]);
            Assert.Equal(20m, rows[0]["2000_Q2"]);
            Assert.Equal(5m, rows[0]["2001_Q1"]);
            Assert.Equal("N", rows[0]["region"]);
        }

        [Fact]
        public async Task DynamicDiscovery_SortsValuesNumerically()
        {
            var batch = await RunFirstBatch(@"
                PIVOT (VALUES ('N',2,10), ('N',10,20), ('N',1,5)) AS t(region, n, amount)
                ON n USING SUM(amount);");

            // Expect numeric ordering 1, 2, 10 (not lexical 1, 10, 2).
            var pivotCols = batch.ColumnNames.Where(c => c != "region").ToList();
            Assert.Equal(new[] { "1", "2", "10" }, pivotCols);
        }

        [Fact]
        public void MultiColumnIn_IsRejectedWithDiagnostic()
        {
            var sql = "PIVOT sales ON yr, quarter IN ('Q1') USING SUM(amount);";
            var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
            Assert.Contains(script.Diagnostics, d => d.Message.Contains("single ON column"));
        }

        [Fact]
        public async Task ImplicitGrouping_KeepsNonPivotNonAggregateColumns()
        {
            // Both region and dept are non-ON, non-aggregate -> both become grouping columns.
            var rows = (await Run(@"
                PIVOT (VALUES ('N','X','Q1',10), ('N','X','Q2',20), ('N','Y','Q1',7)) AS t(region, dept, quarter, amount)
                ON quarter USING SUM(amount);"));

            Assert.Equal(2, rows.Count); // (N,X) and (N,Y)
            var nx = rows.First(r => r["dept"]?.ToString() == "X");
            Assert.Equal(10m, nx["Q1"]);
            Assert.Equal(20m, nx["Q2"]);
            var ny = rows.First(r => r["dept"]?.ToString() == "Y");
            Assert.Equal(7m, ny["Q1"]);
            Assert.Null(ny["Q2"]); // absent combo -> NULL
        }

        private static async Task<List<Row>> Run(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(new Parser(new Lexer(sql).Tokenize(), sql).Parse().Statements[0]).ToListAsync();
            return batches.SelectMany(b => b.Rows).ToList();
        }

        private static async Task<DataTable> RunFirstBatch(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(new Parser(new Lexer(sql).Tokenize(), sql).Parse().Statements[0]).ToListAsync();
            return batches[0];
        }
    }
}
