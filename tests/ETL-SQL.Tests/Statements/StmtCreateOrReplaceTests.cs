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
    public class StmtCreateOrReplaceTests
    {
        [Fact]
        public async Task CreateOrReplaceTable_ReplacesExistingDefinitionAndData()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #t (a INT);"));
            await ev.Evaluate(Parse("INSERT INTO #t VALUES (1), (2);"));
            await ev.Evaluate(Parse("CREATE OR REPLACE TABLE #t (b INT);"));
            await ev.Evaluate(Parse("INSERT INTO #t VALUES (9);"));

            var rows = (await ev.ExecuteQuery(Parse("SELECT b FROM #t;").Statements[0]).ToListAsync())
                .SelectMany(b => b.Rows).ToList();
            Assert.Single(rows);
            Assert.Equal(9m, rows[0]["b"]);
        }

        [Fact]
        public async Task CreateOrReplaceView_ReplacesDefinition()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await ev.Evaluate(Parse("CREATE TABLE #src (x INT);"));
            await ev.Evaluate(Parse("INSERT INTO #src VALUES (1), (2), (3);"));
            await ev.Evaluate(Parse("CREATE VIEW v AS SELECT x FROM #src WHERE x > 1;"));
            await ev.Evaluate(Parse("CREATE OR REPLACE VIEW v AS SELECT x FROM #src WHERE x > 2;"));

            var rows = (await ev.ExecuteQuery(Parse("SELECT x FROM v;").Statements[0]).ToListAsync())
                .SelectMany(b => b.Rows).ToList();
            Assert.Single(rows);
            Assert.Equal(3m, rows[0]["x"]);
        }

        [Fact]
        public void CreateOrReplaceTable_RoundTripsThroughToSql()
        {
            var sql = "CREATE OR REPLACE TABLE #t (a INT);";
            var serialized = Parse(sql).Statements[0].ToSql();
            Assert.Contains("CREATE", serialized);
            Assert.Contains("TABLE", serialized);
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
