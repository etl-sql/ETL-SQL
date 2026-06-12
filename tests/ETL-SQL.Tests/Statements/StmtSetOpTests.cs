using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class SetOperationTests
    {
        [Fact]
        public async Task TestUnionAll()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = "SELECT 1 AS ID UNION ALL SELECT 1 AS ID;";
            var resultBatches = await ev.ExecuteQuery(Parse(script).Statements[0]).ToListAsync();
            var rows = resultBatches.SelectMany(b => b.Rows).ToList();
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public async Task TestUnion()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = "SELECT 1 AS ID UNION SELECT 1 AS ID;";
            var resultBatches = await ev.ExecuteQuery(Parse(script).Statements[0]).ToListAsync();
            var rows = resultBatches.SelectMany(b => b.Rows).ToList();
            Assert.Single(rows);
        }

        [Fact]
        public async Task TestExcept()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = "SELECT 1 AS ID UNION ALL SELECT 2 AS ID EXCEPT SELECT 1 AS ID;";
            var resultBatches = await ev.ExecuteQuery(Parse(script).Statements[0]).ToListAsync();
            var rows = resultBatches.SelectMany(b => b.Rows).ToList();
            Assert.Single(rows);
            Assert.Equal(2m, rows[0]["ID"]);
        }

        [Fact]
        public async Task TestIntersect()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = "SELECT 1 AS ID UNION ALL SELECT 2 AS ID INTERSECT SELECT 2 AS ID;";
            var resultBatches = await ev.ExecuteQuery(Parse(script).Statements[0]).ToListAsync();
            var rows = resultBatches.SelectMany(b => b.Rows).ToList();
            Assert.Single(rows);
            Assert.Equal(2m, rows[0]["ID"]);
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }


    }
}
