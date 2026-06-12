using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.TUI.UI;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Covers the per-tab session pipeline: a new tab must start with an empty bottom
    /// pane, switching tabs must preserve and restore each tab's results AND its bottom
    /// view mode, and the exit path must see unsaved changes in every tab (not just the
    /// active one).
    /// </summary>
    public class TabSessionTests
    {
        static TabSessionTests()
        {
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        private static ConsoleEditor NewEditor()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            return editor;
        }

        private static DataTable OneRowTable()
        {
            var t = new DataTable();
            t.SetColumns(new[] { "Col" });
            t.Rows.Add(new Row(t.Schema, new object?[] { "v" }));
            return t;
        }

        [Fact]
        public async Task NewTab_ClearsBottomPaneAndViewMode()
        {
            var editor = NewEditor();

            editor._evaluator.LastResultSets.Add(OneRowTable());
            editor._evaluator.Log("hello", System.ConsoleColor.White);
            editor._renderer.ResultsVisible = true;

            await editor.NewTab();

            Assert.Empty(editor._evaluator.LastResultSets);
            Assert.Empty(editor._evaluator.Messages);
            Assert.False(editor._renderer.ResultsVisible);
            Assert.False(editor._renderer.PerformanceVisible);
            Assert.False(editor._renderer.CompareMode);
        }

        [Fact]
        public async Task SwitchingTabs_RestoresResultsAndViewMode()
        {
            var editor = NewEditor();

            // Tab 0: has a result set and is showing the Results view.
            editor._evaluator.LastResultSets.Add(OneRowTable());
            editor._renderer.ResultsVisible = true;

            await editor.NewTab(); // tab 1 — empty pane
            Assert.Empty(editor._evaluator.LastResultSets);
            Assert.False(editor._renderer.ResultsVisible);

            editor.SwitchToTab(0); // back to tab 0
            Assert.Single(editor._evaluator.LastResultSets);
            Assert.True(editor._renderer.ResultsVisible);

            editor.SwitchToTab(1); // tab 1 still empty
            Assert.Empty(editor._evaluator.LastResultSets);
            Assert.False(editor._renderer.ResultsVisible);
        }

        [Fact]
        public async Task CountDirtyTabs_SeesEveryTabNotJustActive()
        {
            var editor = NewEditor();
            Assert.Equal(0, editor.CountDirtyTabs());

            editor.MarkDirty();                 // active (tab 0) dirty
            Assert.Equal(1, editor.CountDirtyTabs());

            await editor.NewTab();              // saves tab 0 (dirty); active tab 1 is clean
            Assert.Equal(1, editor.CountDirtyTabs());

            editor.MarkDirty();                 // tab 1 now dirty too
            Assert.Equal(2, editor.CountDirtyTabs());
        }
    }
}
