using Xunit;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Parser;
using ETL_SQL.App;
using System;
using System.Threading;

namespace ETL_SQL.Tests
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
            
            await Task.Delay(2000); // Increase wait to 2s to ensure polling logic has started (TQ-7)
            cts.Cancel();

            // The task should return or throw TaskCanceledException (which inherits from OperationCanceledException)
            await Assert.ThrowsAsync<TaskCanceledException>(async () => await evalTask);
        }
    }
}
