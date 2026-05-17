using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class StmtValuesConstructorTests
    {
        [Fact]
        public async Task ValuesConstructorProjectsNamedColumns()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var res = await ev.ExecuteQuery(Parse("SELECT * FROM (VALUES (1, 'A'), (2, 'B')) AS t(id, name) ORDER BY id;").Statements[0]).FirstAsync();

            Assert.Equal(new[] { "id", "name" }, res.ColumnNames);
            Assert.Equal(2, res.Rows.Count);
            Assert.Equal(1m, res.Rows[0]["id"]);
            Assert.Equal("A", res.Rows[0]["name"]);
            Assert.Equal(2m, res.Rows[1]["id"]);
            Assert.Equal("B", res.Rows[1]["name"]);
        }

        [Fact]
        public async Task ValuesConstructorSupportsQualifiedReferences()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var res = await ev.ExecuteQuery(Parse("SELECT t.name FROM (VALUES (1, 'A'), (2, 'B')) AS t(id, name) WHERE t.id = 2;").Statements[0]).FirstAsync();

            Assert.Single(res.Rows);
            Assert.Equal("B", res.Rows[0]["name"]);
        }

        [Fact]
        public async Task ValuesConstructorWorksInJoins()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sql = "SELECT t.name, x.label FROM (VALUES (1, 'A'), (2, 'B')) AS t(id, name) JOIN (VALUES (2, 'Two')) AS x(id, label) ON t.id = x.id;";
            var res = await ev.ExecuteQuery(Parse(sql).Statements[0]).FirstAsync();

            Assert.Single(res.Rows);
            Assert.Equal("B", res.Rows[0]["name"]);
            Assert.Equal("Two", res.Rows[0]["label"]);
        }

        [Fact]
        public async Task ValuesConstructorUsesStandardDefaultColumnNames()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var res = await ev.ExecuteQuery(Parse("SELECT column1 FROM (VALUES (7)) AS v;").Statements[0]).FirstAsync();

            Assert.Equal("column1", Assert.Single(res.ColumnNames));
            Assert.Equal(7m, res.Rows[0]["column1"]);
        }

        [Fact]
        public async Task ValuesConstructorRejectsMismatchedRowWidths()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await ev.ExecuteQuery(Parse("SELECT * FROM (VALUES (1), (2, 3)) AS v(id);").Statements[0]).FirstAsync();
            });

            Assert.Contains("same number of expressions", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ValuesConstructorRequiresAlias()
        {
            var script = Parse("SELECT * FROM (VALUES (1));");

            var diagnostic = Assert.Single(script.Diagnostics);
            Assert.Contains("Expected alias after VALUES table constructor", diagnostic.Message);
        }

        private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
    }
}
