using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Background (non-blocking) execution lifecycle and cancellation plumbing.</summary>
    public class NonBlockingRunTests
    {
        static NonBlockingRunTests()
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
        public void ExecutionRunning_FalseInitially()
        {
            var e = NewEditor();
            Assert.False(e.IsRunning);
            Assert.False(e._renderer.ExecutionRunning);
        }

        [Fact]
        public void WaitForRun_WhenIdle_CompletesImmediately()
        {
            Assert.True(NewEditor().WaitForRunAsync().IsCompleted);
        }

        [Fact]
        public void CancelRun_WhenIdle_DoesNotThrow()
        {
            NewEditor().CancelRun(); // no exception
        }

        [Fact]
        public async Task RunScript_ClearsRunningStateWhenDone()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "SELECT 1;" });

            await e.RunScript(); // returns the background task — awaiting it waits for completion

            Assert.False(e.IsRunning);
            Assert.False(e._renderer.ExecutionRunning);
        }

        [Fact]
        public async Task RunScript_WhileRunning_IsRejected()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "SELECT 1;" });

            var first = e.RunScript();
            // Second call before the first completes must not start a concurrent run.
            var second = e.RunScript();
            Assert.True(second.IsCompleted); // rejected → CompletedTask
            Assert.NotSame(first, second);

            await first;
            Assert.False(e.IsRunning);
        }
    }
}
