using Xunit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Prompt-driven shortcuts (Replace, Go to line) exercised via a queued-input seam.</summary>
    public class PromptDispatchTests
    {
        static PromptDispatchTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        private static ConsoleEditor NewEditor()
        {
            var e = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            e._renderer.Headless = true;
            return e;
        }

        private static ConsoleKeyInfo Key(ConsoleKey k, bool ctrl = false)
            => new('\0', k, false, false, ctrl);

        [Fact]
        public async Task CtrlH_Replace_ReplacesAllOccurrences()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "SELECT foo FROM foo;" });
            e.EnqueuePromptResponse("foo"); // Find
            e.EnqueuePromptResponse("bar"); // Replace with

            await e.HandleKey(Key(ConsoleKey.H, ctrl: true));

            Assert.Equal("SELECT bar FROM bar;", e._buffer.GetText());
        }

        [Fact]
        public async Task CtrlG_GoToLine_MovesCursor()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "a", "b", "c", "d", "e" });
            e.EnqueuePromptResponse("3");

            await e.HandleKey(Key(ConsoleKey.G, ctrl: true));

            Assert.Equal(2, e._buffer.CursorLine); // line 3 -> 0-based index 2
        }

        [Fact]
        public async Task CtrlG_GoToLine_ClampsBeyondEnd()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "a", "b", "c" });
            e.EnqueuePromptResponse("999");

            await e.HandleKey(Key(ConsoleKey.G, ctrl: true));

            Assert.Equal(2, e._buffer.CursorLine); // clamped to the last line
        }
    }
}
