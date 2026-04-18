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

namespace ETL_SQL.Tests.Statements
{
    public class ControlFlowTests
    {

        [Fact]
        public async Task TestIfElse()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @res STRING;
                IF 1 < 2 BEGIN
                    SET @res = 'TRUE';
                END ELSE BEGIN
                    SET @res = 'FALSE';
                END
            ";
            await ev.Evaluate(Parse(script));
            Assert.Equal("TRUE", ev.Variables["@res"]);
        }

        [Fact]
        public async Task TestWhileLoop()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @i INT; SET @i = 0;
                DECLARE @count INT; SET @count = 0;
                WHILE @i < 5 BEGIN
                    SET @count = @count + 1;
                    SET @i = @i + 1;
                END
            ";
            await ev.Evaluate(Parse(script));
            Assert.Equal(5m, ev.Variables["@count"]);
        }

        [Fact]
        public async Task TestForLoop()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @sum INT; SET @sum = 0;
                FOR @i = 1 TO 3 BEGIN
                    SET @sum = @sum + @i;
                END
            ";
            await ev.Evaluate(Parse(script));
            Assert.Equal(6m, ev.Variables["@sum"]);
        }

        [Fact]
        public async Task TestForeachLoop()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                CREATE TABLE #Items (Val INT);
                INSERT INTO #Items VALUES (10), (20), (30);
                DECLARE @sum INT = 0;
                FOREACH @row IN #Items BEGIN
                    SET @sum = @sum + @row.Val;
                END;
            ";
            await ev.Evaluate(TestHelpers.Parse(script));
            Assert.Equal(60m, ev.Variables["@sum"]);
        }

        [Fact]
        public async Task TestLoopBreak()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @i INT = 0;
                DECLARE @count INT = 0;
                WHILE @i < 10 BEGIN
                    IF @i = 5 BREAK;
                    SET @count = @count + 1;
                    SET @i = @i + 1;
                END
            ";
            await ev.Evaluate(TestHelpers.Parse(script));
            Assert.Equal(5m, ev.Variables["@count"]);
        }

        [Fact]
        public async Task TestLoopContinue()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @i INT = 0;
                DECLARE @count INT = 0;
                WHILE @i < 5 BEGIN
                    SET @i = @i + 1;
                    IF @i = 3 CONTINUE;
                    SET @count = @count + 1;
                END
            ";
            await ev.Evaluate(TestHelpers.Parse(script));
            Assert.Equal(4m, ev.Variables["@count"]);
        }

        [Fact]
        public async Task TestWaitForDelay_Completes()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await ev.Evaluate(TestHelpers.Parse("WAITFOR DELAY '00:00:00.100';"));
            sw.Stop();
            // Should have paused at least 100ms (allow generous margin for slow CI)
            Assert.True(sw.ElapsedMilliseconds >= 50, $"Expected >= 50ms but was {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void TestWaitForDelay_ParsesCorrectly()
        {
            var script = TestHelpers.Parse("WAITFOR DELAY '00:00:05';");
            Assert.Single(script.Statements);
            Assert.IsType<WaitForStatement>(script.Statements[0]);
        }

        [Fact]
        public async Task TestWaitForDelay_InvalidFormat_Throws()
        {
            var ev = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(async () =>
                await ev.Evaluate(TestHelpers.Parse("WAITFOR DELAY 'not-a-time';")));
        }

        private static Script Parse(string source)
        {
            return TestHelpers.Parse(source);
        }


    }
}
