using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    public class RegexTests
    {
        [Fact]
        public async Task TestRegexpLike()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(ev, "REGEXP_LIKE('Hello World', 'Hello')", true);
            await AssertEval(ev, "REGEXP_LIKE('Hello World', '^Hello')", true);
            await AssertEval(ev, "REGEXP_LIKE('Hello World', 'world$', 'i')", true);
            await AssertEval(ev, "REGEXP_LIKE('Hello World', 'world$')", true); // Default is 'i'
            await AssertEval(ev, "REGEXP_LIKE('12345', '^\\d+$')", true);
            await AssertEval(ev, "REGEXP_LIKE('abcde', '^\\d+$')", false);
        }

        [Fact]
        public async Task TestPostgresRegexOperators()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(ev, "'Hello World' ~ '^Hello'", true);
            await AssertEval(ev, "'Hello World' ~ 'world$'", false);
            await AssertEval(ev, "'Hello World' ~* 'world$'", true);
            await AssertEval(ev, "NULL ~ '.*'", null);
        }

        [Fact]
        public async Task TestPostgresIlikeOperator()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(ev, "'Hello World' ILIKE 'hello%'", true);
            await AssertEval(ev, "'Hello World' NOT ILIKE 'goodbye%'", true);
        }

        [Fact]
        public async Task TestRegexpSubstr()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(ev, "REGEXP_SUBSTR('Hello 123 World', '\\d+')", "123");
            await AssertEval(ev, "REGEXP_SUBSTR('A1 B2 C3', '[A-Z]\\d', 1, 2)", "B2");
            await AssertEval(ev, "REGEXP_SUBSTR('A1 B2 C3', '[A-Z]\\d', 4, 1)", "B2");
        }

        [Fact]
        public async Task TestRegexpReplace()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(ev, "REGEXP_REPLACE('Hello 123 World', '\\d+', '456')", "Hello 456 World");
            await AssertEval(ev, "REGEXP_REPLACE('A1 B2 C3', '\\d', 'X')", "AX BX CX");
            await AssertEval(ev, "REGEXP_REPLACE('A1 B2 C3', '\\d', 'X', 1, 2)", "A1 BX C3");
        }

        [Fact]
        public async Task TestRegexpInstr()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(ev, "REGEXP_INSTR('Hello 123 World', '\\d+')", 7m);
            await AssertEval(ev, "REGEXP_INSTR('A1 B2 C3', '[A-Z]\\d', 1, 3)", 7m);
        }

        [Fact]
        public async Task TestRegexpCount()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await AssertEval(ev, "REGEXP_COUNT('A1 B2 C3', '[A-Z]\\d')", 3m);
            await AssertEval(ev, "REGEXP_COUNT('A1 B2 C3', '\\d', 4)", 2m);
        }

        [Fact]
        public async Task TestRegexpMatches()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var res = await EvaluateExpression(ev, "REGEXP_MATCHES('A1 B2 C3', '[A-Z]\\d')") as List<object?>;
            Assert.NotNull(res);
            Assert.Equal(3, res.Count);
            Assert.Equal("A1", res[0]);
            Assert.Equal("B2", res[1]);
            Assert.Equal("C3", res[2]);
        }

        [Fact]
        public async Task TestRegexpSplitToTable()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var script = "SELECT * FROM REGEXP_SPLIT_TO_TABLE('A,B;C D', '[,; ]');";
            var batches = ev.ExecuteQuery(Parse(script).Statements[0]);
            var list = new List<string>();
            await foreach (var batch in batches)
            {
                foreach (var row in batch.Rows) list.Add(row["VALUE"]?.ToString() ?? "");
            }
            Assert.Equal(4, list.Count);
            Assert.Equal("A", list[0]);
            Assert.Equal("B", list[1]);
            Assert.Equal("C", list[2]);
            Assert.Equal("D", list[3]);
        }

        private static async Task AssertEval(Evaluator ev, string expression, object? expected)
        {
            var res = await EvaluateExpression(ev, expression);
            if (res is int i) res = (decimal)i;
            if (expected is int ei) expected = (decimal)ei;
            Assert.Equal(expected, res);
        }

        private static async Task<object?> EvaluateExpression(Evaluator ev, string expression)
        {
            var varName = "@res_" + Guid.NewGuid().ToString("N");
            var script = $"DECLARE {varName} ANY; SET {varName} = ({expression});";
            await ev.Evaluate(Parse(script));
            return ev.Variables[varName];
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
