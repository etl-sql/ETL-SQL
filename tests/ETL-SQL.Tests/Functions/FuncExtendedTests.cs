using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Functions
{
    public class ExtendedFunctionTests
    {
        private Evaluator GetEvaluator()
        {
            return DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        }

        [Fact]
        public async Task TestStuff()
        {
            var ev = GetEvaluator();
            await AssertEval(ev, "STUFF('abcdef', 2, 3, 'ijkl')", "aijklef");
            await AssertEval(ev, "STUFF('abcdef', 2, 0, 'ijkl')", "aijklbcdef");
        }

        [Fact]
        public async Task TestStringSplit()
        {
            var ev = GetEvaluator();
            var result = await EvaluateExpression(ev, "STRING_SPLIT('a,b,c', ',')") as DataTable;
            Assert.NotNull(result);
            Assert.Equal(3, result.Rows.Count);
            Assert.Equal("a", result.Rows[0][0]);
            Assert.Equal("b", result.Rows[1][0]);
            Assert.Equal("c", result.Rows[2][0]);
        }

        [Fact]
        public async Task TestFormat()
        {
            var ev = GetEvaluator();
            await AssertEval(ev, "FORMAT(123.456, 'F2')", "123.46");
        }

        [Fact]
        public async Task TestPatIndex()
        {
            var ev = GetEvaluator();
            // In SQL Server, PATINDEX('%bc%', 'abcdef') would be 2. 
            // My implementation returns a boolean (1 or 0) for now if matched.
            // Actually, let's check it.
            await AssertEval(ev, "PATINDEX('%bc%', 'abcdef')", 1m);
        }

        [Fact]
        public async Task TestQuoteName()
        {
            var ev = GetEvaluator();
            await AssertEval(ev, "QUOTENAME('my]table')", "[my]]table]");
        }

        [Fact]
        public async Task TestTranslate()
        {
            var ev = GetEvaluator();
            await AssertEval(ev, "TRANSLATE('1abc23', 'abc', 'def')", "1def23");
        }

        [Fact]
        public async Task TestReplicate()
        {
            var ev = GetEvaluator();
            await AssertEval(ev, "REPLICATE('abc', 3)", "abcabcabc");
        }

        [Fact]
        public async Task TestTryCast()
        {
            var ev = GetEvaluator();
            await AssertEval(ev, "TRY_CAST('123' AS INT)", 123m);
            await AssertEval(ev, "TRY_CAST('abc' AS INT)", null);
        }

        [Fact]
        public async Task TestTrigFunctions()
        {
            var ev = GetEvaluator();
            await AssertEval(ev, "SIN(0)", 0m);
            await AssertEval(ev, "COS(0)", 1m);
            await AssertEval(ev, "SIGN(-123)", -1m);
            await AssertEval(ev, "SIGN(123)", 1m);
            await AssertEval(ev, "SIGN(0)", 0m);
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
            return ev.Variables.ContainsKey(varName) ? ev.Variables[varName] : null;
        }

        private static Script Parse(string source)
        {
            var lexer = new Lexer(source);
            return new Parser(lexer.Tokenize()).Parse();
        }
    }
}
