using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Engine;

namespace ETL_SQL.Tests.Engine
{
    /// <summary>
    /// Verifies that multiple Evaluator instances can execute concurrently without
    /// sharing state. This is a Phase 9 prerequisite: the Blazor dashboard spins up
    /// one Evaluator per user session and they must be fully isolated.
    /// </summary>
    public class ConcurrentEvaluatorTests
    {
        private static Evaluator BuildEvaluator(string sessionId)
        {
            var sp = DependencyInjectionSetup.BuildServiceProvider();
            var ev = sp.GetRequiredService<Evaluator>();
            ev.SessionId = sessionId;
            return ev;
        }

        private static async Task<Evaluator> RunScript(Evaluator ev, string script)
        {
            var tokens = new Lexer(script).Tokenize();
            var parsed = new Parser(tokens).Parse();
            await ev.Evaluate(parsed);
            return ev;
        }

        // ── Variable isolation ────────────────────────────────────────────────

        [Fact]
        public async Task ConcurrentEvaluators_HaveIsolatedVariables()
        {
            const int concurrency = 5;
            var tasks = Enumerable.Range(1, concurrency).Select(i => Task.Run(async () =>
            {
                var ev = BuildEvaluator($"session-{i}");
                await RunScript(ev, $"DECLARE @x INT = {i * 100};");
                return (sessionIndex: i, value: Convert.ToInt32(ev.Variables["@x"]));
            }));

            var results = await Task.WhenAll(tasks);

            foreach (var r in results)
                Assert.Equal(r.sessionIndex * 100, r.value);
        }

        // ── Temp table isolation ──────────────────────────────────────────────

        [Fact]
        public async Task ConcurrentEvaluators_HaveIsolatedTempTables()
        {
            const int concurrency = 4;
            var tasks = Enumerable.Range(1, concurrency).Select(i => Task.Run(async () =>
            {
                var ev = BuildEvaluator($"tmp-session-{i}");
                await RunScript(ev, $@"
CREATE TABLE #Local (N INT);
INSERT INTO #Local (N) VALUES ({i});
INSERT INTO #Local (N) VALUES ({i + 1000});
");
                var ds = ev.Connections["#Local"] as InMemoryDataSource;
                Assert.NotNull(ds);

                var rows = new List<Row>();
                await foreach (var batch in ds.ReadBatches())
                    rows.AddRange(batch.Rows);

                return (sessionIndex: i, count: rows.Count, first: Convert.ToInt32(rows[0]["N"]));
            }));

            var results = await Task.WhenAll(tasks);

            foreach (var r in results)
            {
                Assert.Equal(2, r.count);
                Assert.Equal(r.sessionIndex, r.first);
            }
        }

        // ── No shared state pollution between connection names ─────────────────

        [Fact]
        public async Task ConcurrentEvaluators_DoNotShareConnectionNames()
        {
            var barrier = new SemaphoreSlim(0, 2);

            async Task<bool> RunAndCheck(int id)
            {
                var ev = BuildEvaluator($"conn-session-{id}");
                await RunScript(ev, $@"
CREATE TABLE #T{id} (V INT);
INSERT INTO #T{id} (V) VALUES ({id});
");
                // Signal we've created our table and wait for the other task
                barrier.Release();
                await barrier.WaitAsync();

                // Each evaluator should only see its own table
                bool hasOwn  = ev.Connections.ContainsKey($"#T{id}");
                bool hasOther = ev.Connections.ContainsKey($"#T{(id == 1 ? 2 : 1)}");
                return hasOwn && !hasOther;
            }

            var t1 = Task.Run(() => RunAndCheck(1));
            var t2 = Task.Run(() => RunAndCheck(2));
            var results = await Task.WhenAll(t1, t2);

            Assert.All(results, r => Assert.True(r));
        }

        // ── Throughput: N evaluators complete without deadlock ────────────────

        [Fact]
        public async Task ConcurrentEvaluators_CompleteWithoutDeadlock()
        {
            const int concurrency = 10;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var tasks = Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
            {
                var ev = BuildEvaluator($"throughput-{i}");
                await RunScript(ev, @"
DECLARE @a INT = 1;
DECLARE @b INT = 2;
DECLARE @c INT = @a + @b;
");
                return Convert.ToInt32(ev.Variables["@c"]);
            }, cts.Token));

            var results = await Task.WhenAll(tasks);

            Assert.Equal(concurrency, results.Length);
            Assert.All(results, v => Assert.Equal(3, v));
        }

        // ── SessionId is stamped and isolated per evaluator ───────────────────

        [Fact]
        public async Task ConcurrentEvaluators_SessionIdsAreIsolated()
        {
            const int concurrency = 5;
            var tasks = Enumerable.Range(1, concurrency).Select(i => Task.Run(async () =>
            {
                var sessionId = $"sid-{i}-{Guid.NewGuid():N}";
                var ev = BuildEvaluator(sessionId);
                await RunScript(ev, "DECLARE @dummy INT = 1;");
                return (expected: sessionId, actual: ev.SessionId);
            }));

            var results = await Task.WhenAll(tasks);

            foreach (var r in results)
                Assert.Equal(r.expected, r.actual);
        }
    }
}
