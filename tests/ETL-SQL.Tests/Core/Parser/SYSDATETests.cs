using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class SYSDATETests
    {
        [Fact]
        public async Task TestSysDateParsingAndEvaluation()
        {
            var serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = serviceProvider.GetRequiredService<Evaluator>();

            // Test as a field in SELECT
            var res = await EvaluateExpression(evaluator, "SYSDATE");
            Assert.True(res is DateTime, $"SYSDATE should return DateTime, but got {res?.GetType().Name}");
            Assert.True((DateTime.Now - (DateTime)res).TotalMinutes < 1, "SYSDATE should be relative to current time");

            // Test as a comparison
            var isFuture = await EvaluateExpression(evaluator, "SYSDATE > '2000-01-01'");
            Assert.Equal(true, isFuture);
        }

        private static async Task<object?> EvaluateExpression(Evaluator ev, string expression)
        {
            var varName = "@res_" + Guid.NewGuid().ToString("N");
            var script = $"DECLARE {varName} ANY; SET {varName} = ({expression});";
            var lexer = new global::ETL_SQL.Core.Parser.Lexer(script);
            var parser = new global::ETL_SQL.Core.Parser.Parser(lexer.Tokenize());
            await ev.Evaluate(parser.Parse());
            return ev.Variables[varName];
        }
    }
}
