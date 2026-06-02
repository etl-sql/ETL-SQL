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

namespace ETL_SQL.Tests.Engine
{
    public class ModularTests
    {



        [Fact]
        public async Task TestBasicProcedure()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer(@"
                CREATE PROCEDURE TestProc(@Val INT) AS
                BEGIN
                    DECLARE @Result INT = @Val * 2;
                    PRINT 'Result: ' + CAST(@Result AS STRING);
                END;
            ").TokenizeToScript());
            await eval.Evaluate(new Lexer("EXECUTE TestProc 21;").TokenizeToScript());
        }

        [Fact]
        public async Task TestBasicFunction()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer(@"
                CREATE FUNCTION DoubleIt(@N NUMERIC) RETURNS NUMERIC AS
                BEGIN
                    RETURN @N * 2;
                END;
            ").TokenizeToScript());
            await eval.Evaluate(new Lexer("SELECT DoubleIt(5) AS Res;").TokenizeToScript());
            var result = eval.LastResult?.Rows.FirstOrDefault()?.Columns.Values.FirstOrDefault();
            Assert.Equal(10m, Convert.ToDecimal(result));
        }

        [Fact]
        public async Task TestScopeIsolation()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer("DECLARE @X INT = 10;").TokenizeToScript());
            await eval.Evaluate(new Lexer(@"
                CREATE PROCEDURE OuterProc() AS
                BEGIN
                    DECLARE @Y INT = 20;
                    EXECUTE InnerProc;
                END;
            ").TokenizeToScript());
            await eval.Evaluate(new Lexer(@"
                CREATE PROCEDURE InnerProc() AS
                BEGIN
                    DECLARE @Z INT = 30;
                END;
            ").TokenizeToScript());
            
            await eval.Evaluate(new Lexer("EXECUTE OuterProc;").TokenizeToScript());
            
            Assert.False(eval.Variables.ContainsKey("@Y"), "@Y leaked to global scope");
            Assert.False(eval.Variables.ContainsKey("@Z"), "@Z leaked to global scope");
            Assert.Equal(10, Convert.ToInt32(eval.Variables["@X"]));
        }

        [Fact]
        public async Task TestRecursion()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer(@"
                CREATE FUNCTION Factorial(@N NUMERIC) RETURNS NUMERIC AS
                BEGIN
                    IF @N <= 1 RETURN 1;
                    RETURN @N * Factorial(@N - 1);
                END;
            ").TokenizeToScript());
            await eval.Evaluate(new Lexer("SELECT Factorial(5) AS Res;").TokenizeToScript());
            var result = eval.LastResult?.Rows.FirstOrDefault()?.Columns.Values.FirstOrDefault();
            Assert.Equal(120m, Convert.ToDecimal(result));
        }

        [Fact]
        public async Task TestNestedCalls()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await eval.Evaluate(new Lexer(@"
                CREATE FUNCTION Square(@N NUMERIC) RETURNS NUMERIC AS BEGIN RETURN @N * @N; END;
                CREATE FUNCTION SumOfSquares(@A NUMERIC, @B NUMERIC) RETURNS NUMERIC AS
                BEGIN
                    RETURN Square(@A) + Square(@B);
                END;
            ").TokenizeToScript());
            await eval.Evaluate(new Lexer("SELECT SumOfSquares(3, 4) AS Res;").TokenizeToScript());
            var result = eval.LastResult?.Rows.FirstOrDefault()?.Columns.Values.FirstOrDefault();
            Assert.Equal(25m, Convert.ToDecimal(result));
        }

        [Fact]
        public async Task TestAlterOrCreateOrAlter()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            
            await eval.Evaluate(new Lexer(@"
                CREATE OR ALTER PROCEDURE IdempotentProc() AS
                BEGIN
                    PRINT 'Version 1';
                END;
            ").TokenizeToScript());
            
            await eval.Evaluate(new Lexer(@"
                CREATE OR ALTER PROCEDURE IdempotentProc() AS
                BEGIN
                    PRINT 'Version 2';
                END;
            ").TokenizeToScript());
            
            await eval.Evaluate(new Lexer(@"
                ALTER PROCEDURE IdempotentProc() AS
                BEGIN
                    PRINT 'Version 3';
                END;
            ").TokenizeToScript());

            await eval.Evaluate(new Lexer("CREATE OR ALTER FUNCTION AddOne(@N INT) RETURNS INT AS BEGIN RETURN @N + 1; END;").TokenizeToScript());
            await eval.Evaluate(new Lexer("SELECT AddOne(10) AS Res;").TokenizeToScript());
            var res1 = eval.LastResult?.Rows.FirstOrDefault()?.Columns.Values.FirstOrDefault();
            Assert.Equal(11, Convert.ToInt32(res1));

            await eval.Evaluate(new Lexer("CREATE OR ALTER FUNCTION AddOne(@N INT) RETURNS INT AS BEGIN RETURN @N + 2; END;").TokenizeToScript());
            await eval.Evaluate(new Lexer("SELECT AddOne(10) AS Res;").TokenizeToScript());
            var res2 = eval.LastResult?.Rows.FirstOrDefault()?.Columns.Values.FirstOrDefault();
            Assert.Equal(12, Convert.ToInt32(res2));

            await eval.Evaluate(new Lexer("CREATE CONNECTION TestConn AS FLATFILE('old.csv');").TokenizeToScript());
            await eval.Evaluate(new Lexer("ALTER CONNECTION TestConn AS FLATFILE('new.csv');").TokenizeToScript());
            
            Assert.True(eval.Connections.ContainsKey("TestConn"), "Connection not found after ALTER");
            
            await Assert.ThrowsAsync<ExecutionException>(async () => 
                await eval.Evaluate(new Lexer("ALTER PROCEDURE NonExistent() AS BEGIN PRINT 1; END;").TokenizeToScript()));

            await Assert.ThrowsAsync<ExecutionException>(async () => 
                await eval.Evaluate(new Lexer("CREATE PROCEDURE IdempotentProc() AS BEGIN PRINT 1; END;").TokenizeToScript()));
        }
    }
}
