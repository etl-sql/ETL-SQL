using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Mid-run cancellation via a real cancellable statement (WAITFOR DELAY).</summary>
    public class CancellationTests
    {
        static CancellationTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        private static ConsoleEditor NewEditor()
        {
            var e = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            e._renderer.Headless = true;
            return e;
        }

        [Fact]
        public async Task CancelRun_StopsActiveRun_AndEditorStaysReusable()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "WAITFOR DELAY '00:00:30';" }); // long enough to be cancelled mid-flight

            var run = e.RunScript(); // background run; blocks in WAITFOR on its cancellation token

            var sw = Stopwatch.StartNew();
            while (!e.IsRunning && sw.ElapsedMilliseconds < 3000) await Task.Delay(10);
            Assert.True(e.IsRunning, "The run should enter the running state before cancellation.");

            e.CancelRun();           // cancels the token → WAITFOR's Task.Delay throws
            var cancelSw = Stopwatch.StartNew();
            await run;               // completes promptly once cancelled (well under the 30s delay)
            cancelSw.Stop();
            Assert.True(cancelSw.ElapsedMilliseconds < 10_000, // flaky-time-bound-ok: distinguishes cancellation from the 30s WAITFOR delay
                $"Cancellation should complete well before the 30s WAITFOR delay; actual {cancelSw.ElapsedMilliseconds}ms.");
            Assert.False(e.IsRunning);

            // The editor is reusable after a cancellation: a fresh run completes normally.
            e._buffer.Load(new[] { "SELECT 1;" });
            await e.RunScript();
            Assert.True(e._evaluator.LastResultSets.Count >= 1);
        }

        [Fact]
        public async Task SecondRun_WhileFirstIsBlocked_IsRejected()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "WAITFOR DELAY '00:00:30';" });

            var first = e.RunScript();
            var sw = Stopwatch.StartNew();
            while (!e.IsRunning && sw.ElapsedMilliseconds < 3000) await Task.Delay(10);
            Assert.True(e.IsRunning, "The first run should enter the running state before the second run is attempted.");

            var second = e.RunScript();    // must be rejected while the first is running
            Assert.True(second.IsCompleted);
            Assert.NotSame(first, second);

            e.CancelRun();
            await first;
            Assert.False(e.IsRunning);
        }
    }
}
