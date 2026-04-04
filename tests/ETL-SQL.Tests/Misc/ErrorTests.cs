using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using Spectre.Console;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Tests
{
    public class ErrorTests
    {



        [Fact]
        public async Task TestThrowBasic()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await Assert.ThrowsAsync<ExecutionException>(async () => await eval.Evaluate(new Lexer("THROW 'Custom Error Message';").TokenizeToScript()));
        }

        [Fact]
        public async Task TestTryCatchBasic()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer("DECLARE @Passed BIT = 0;").TokenizeToScript());
            await eval.Evaluate(new Lexer(@"
                BEGIN TRY
                    SELECT 1/0; -- Should fail in a real engine, but let's force a failure if needed.
                    -- For our engine, let's use THROW to test TRY/CATCH logic
                    THROW 'Error';
                END TRY
                BEGIN CATCH
                    SET @Passed = 1;
                END CATCH;
            ").TokenizeToScript());
            Assert.Equal(1, Convert.ToInt32(eval.Variables["@Passed"]));
        }

        [Fact]
        public async Task TestTryCatchWithThrow()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer(@"
                DECLARE @Msg STRING = '';
                BEGIN TRY
                    THROW 'Inner Error';
                END TRY
                BEGIN CATCH
                    SET @Msg = 'Caught';
                END CATCH;
            ").TokenizeToScript());
            Assert.Equal("Caught", eval.Variables["@Msg"]?.ToString());
        }

        [Fact]
        public async Task TestThrowTerminates()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer("DECLARE @X INT = 1;").TokenizeToScript());
            await Assert.ThrowsAsync<ExecutionException>(async () => await eval.Evaluate(new Lexer("SET @X = 2; THROW 'Stop'; SET @X = 3;").TokenizeToScript()));
            Assert.Equal(2, Convert.ToInt32(eval.Variables["@X"]));
        }
    }
}
