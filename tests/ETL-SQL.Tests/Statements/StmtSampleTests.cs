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
    public class StmtSampleTests
    {
        [Fact]
        public async Task SampleRows_ReturnsExactlyNRows()
        {
            var rows = await Run("SELECT x FROM (VALUES (1),(2),(3),(4),(5)) AS t(x) USING SAMPLE 2 ROWS REPEATABLE (42);");
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.InRange((decimal)r["x"]!, 1m, 5m));
        }

        [Fact]
        public async Task SampleRows_MoreThanTotal_ReturnsAll()
        {
            var rows = await Run("SELECT x FROM (VALUES (1),(2)) AS t(x) USING SAMPLE 10 ROWS;");
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public async Task SamplePercent_100_ReturnsAll()
        {
            var rows = await Run("SELECT x FROM (VALUES (1),(2),(3)) AS t(x) USING SAMPLE 100 PERCENT;");
            Assert.Equal(3, rows.Count);
        }

        [Fact]
        public async Task SamplePercent_0_ReturnsNone()
        {
            var rows = await Run("SELECT x FROM (VALUES (1),(2),(3)) AS t(x) USING SAMPLE 0 PERCENT;");
            Assert.Empty(rows);
        }

        [Fact]
        public async Task SamplePercent_SignSyntax_Parses()
        {
            var rows = await Run("SELECT x FROM (VALUES (1),(2),(3)) AS t(x) USING SAMPLE 100%;");
            Assert.Equal(3, rows.Count);
        }

        [Fact]
        public void Sample_RoundTripsThroughToSql()
        {
            var sql = "SELECT x FROM t USING SAMPLE 10 PERCENT REPEATABLE (7);";
            Assert.Contains("USING SAMPLE", Parse(sql).Statements[0].ToSql());
        }

        private static async Task<List<Row>> Run(string sql)
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var batches = await ev.ExecuteQuery(Parse(sql).Statements[0]).ToListAsync();
            return batches.SelectMany(b => b.Rows).ToList();
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
