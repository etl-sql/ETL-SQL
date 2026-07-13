using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements
{
    public class WaitForPollingTests
    {
        private async Task<Evaluator> GetEvaluator()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            return provider.GetRequiredService<Evaluator>();
        }

        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }

        [Fact]
        public async Task TestWaitFor_PollingCondition()
        {
            var eval = await GetEvaluator();

            // Background task to update a variable after 1.5 seconds.
            // Note: This relies on Evaluator variable thread-safety (resolved in TQ-4).
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                eval.SetVariable("@ready", 1);
            });

            var sql = @"
                DECLARE @ready INT = 0;
                WAITFOR (@ready = 1);
                SELECT 'Done' AS Status;
            ";

            var startTime = DateTime.Now;
            await eval.Evaluate(Parse(sql));
            var duration = DateTime.Now - startTime;

            Assert.True(duration >= TimeSpan.FromSeconds(1.5), $"Wait duration was too short: {duration}");
            Assert.Equal("Done", eval.LastResult.Rows[0]["Status"]);
        }

        [Fact]
        public async Task TestWaitUntil_Syntax()
        {
            var eval = await GetEvaluator();

            // Background task to set a variable after 1 second
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                eval.SetVariable("@signal", 1);
            });

            var sql = @"
                DECLARE @signal INT = 0;
                WAIT UNTIL (SELECT COUNT(*) FROM DIRECTORY('../../../../Docs') WHERE 1=0) = 0; -- Immediate true
                WAIT UNTIL @signal = 1;
                SELECT 'Signal Received' AS msg;
            ";

            await eval.Evaluate(Parse(sql));
            Assert.Equal("Signal Received", eval.LastResult.Rows[0]["msg"]);
        }

        [Fact]
        public async Task TestWaitFor_Cancellation()
        {
            var eval = await GetEvaluator();
            var cts = new CancellationTokenSource();

            var sql = @"
                WAITFOR (1 = 0); -- Infinite wait
            ";

            var evalTask = eval.Evaluate(Parse(sql), cts.Token);

            await Task.Delay(100); // let the WAITFOR loop start; timing is not load-critical (see below)
            cts.Cancel();

            // Cancellation surfaces as TaskCanceledException (from the loop's cancellable Task.Delay) or
            // the base OperationCanceledException (from the loop's ThrowIfCancellationRequested), depending
            // on where the cancel lands. ThrowsAny accepts either, so this no longer needs a precise 2 s
            // wait and cannot flake on a slow/loaded runner.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await evalTask);
        }
    }
}
