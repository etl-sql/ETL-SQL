using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Key-dispatch coverage for shortcuts that previously had none.</summary>
    public class KeyDispatchTests
    {
        static KeyDispatchTests()
        {
            ETL_SQL.TUI.Program.ServiceProvider = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
        }

        private static ConsoleEditor NewEditor()
        {
            var e = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            e._renderer.Headless = true;
            return e;
        }

        private static ConsoleKeyInfo Key(ConsoleKey k, bool shift = false, bool alt = false, bool ctrl = false, char ch = '\0')
            => new(ch, k, shift, alt, ctrl);

        [Fact]
        public async Task F2_TogglesHelpPage_WhenHelpVisible()
        {
            var e = NewEditor();
            e._renderer.HelpVisible = true;
            e._renderer.HelpPageIndex = 0;

            await e.HandleKey(Key(ConsoleKey.F2));
            Assert.Equal(1, e._renderer.HelpPageIndex);

            await e.HandleKey(Key(ConsoleKey.F2));
            Assert.Equal(0, e._renderer.HelpPageIndex);
        }

        [Fact]
        public async Task AltR_TogglesReportPreviewOff()
        {
            // Toggling on with no report definitions auto-runs and reverts; the clean, run-free
            // assertion is that Alt+R toggles an already-open preview back off.
            var e = NewEditor();
            e._renderer.ReportVisible = true;

            await e.HandleKey(Key(ConsoleKey.R, alt: true, ch: 'r'));
            Assert.False(e._renderer.ReportVisible);
        }

        [Fact]
        public async Task ShiftF5_RunsStatementAtCursor()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "SELECT 1;" });
            e._buffer.CursorLine = 0;

            await e.HandleKey(Key(ConsoleKey.F5, shift: true));
            await e.WaitForRunAsync();

            Assert.True(e._evaluator.LastResultSets.Count >= 1);
        }

        [Fact]
        public async Task CtrlF5_RunsSelectedText()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "SELECT 1;" });
            e._buffer.SelectionStartLine = 0;
            e._buffer.SelectionStartCol = 0;
            e._buffer.CursorLine = 0;
            e._buffer.CursorColumn = "SELECT 1".Length; // select "SELECT 1"

            await e.HandleKey(Key(ConsoleKey.F5, ctrl: true));
            await e.WaitForRunAsync();

            Assert.True(e._evaluator.LastResultSets.Count >= 1);
        }

        [Fact]
        public async Task ShiftTab_InSnippetMode_MovesToPreviousPlaceholder()
        {
            var e = NewEditor();
            e._buffer.Load(new[] { "x «a» «b»" });
            e._renderer.SnippetModeActive = true;

            // Place the selection on the second placeholder «b».
            int bStart = "x «a» ".Length;
            int bEnd = bStart + "«b»".Length;
            e._buffer.SelectRange(0, bStart, bEnd);

            await e.HandleKey(Key(ConsoleKey.Tab, shift: true, ch: '\t'));

            // Selection should now be on the first placeholder «a».
            Assert.Equal("x ".Length, e._buffer.SelectionStartCol);
        }
    }
}
