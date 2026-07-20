using System;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class TempTableIntegerConstraintTests
    {
        [Fact]
        public async Task TempTablePositiveOnlyIntegerRejectsNegativeValue()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE TABLE #PositiveOnly (val INT(5,+));
                INSERT INTO #PositiveOnly VALUES (-42);
            ";

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse()));

            Assert.Contains("positive-only constraint", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TempTableNegativeOnlyIntegerRejectsPositiveValue()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE TABLE #NegativeOnly (val INT(5,-));
                INSERT INTO #NegativeOnly VALUES (42);
            ";

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse()));

            Assert.Contains("negative-only constraint", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TempTableIntegerDigitLimitRejectsOverflowValue()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE TABLE #DigitLimit (val INT(5));
                INSERT INTO #DigitLimit VALUES (123456);
            ";

            var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
                eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse()));

            Assert.Contains("exceeds declared digit limit of 5", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TempTablePositiveOnlyIntegerAcceptsValidValue()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE TABLE #ValidPos (val INT(5,+));
                INSERT INTO #ValidPos VALUES (12345);
                SELECT val FROM #ValidPos;
            ";

            await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.Equal(12345m, Convert.ToDecimal(result.Rows[0]["VAL"]));
        }

        [Fact]
        public async Task TempTableNegativeOnlyIntegerAcceptsValidValue()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE TABLE #ValidNeg (val INT(5,-));
                INSERT INTO #ValidNeg VALUES (-12345);
                SELECT val FROM #ValidNeg;
            ";

            await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());
            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Single(result.Rows);
            Assert.Equal(-12345m, Convert.ToDecimal(result.Rows[0]["VAL"]));
        }
    }
}
