using Xunit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectre.Console;
using ETL_SQL.TUI.UI;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Connectors.MockDb;
using System.Linq;
using System.IO;

namespace ETL_SQL.Tests.UI
{
    public class ConsoleEditorTests
    {
        static ConsoleEditorTests()
        {
            // Initialize TUI ServiceProvider for integration tests to satisfy dependencies like IClipboardService
            var sp = ETL_SQL.TUI.TuiDependencyInjectionSetup.BuildServiceProvider();
            ETL_SQL.TUI.Program.ServiceProvider = sp;
        }

        private static List<string> TabLabels(ConsoleEditor editor) =>
            editor._tabs.Select(t => TabBarLayout.Label(t.FilePath, t.IsDirty)).ToList();

        [Fact]
        public async Task TestTyping()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "" });
            await editor.HandleKey(new ConsoleKeyInfo('S', ConsoleKey.S, false, false, false));
            await editor.HandleKey(new ConsoleKeyInfo('E', ConsoleKey.E, false, false, false));
            var text = editor._buffer.GetText().Trim();
            Assert.Equal("SE", text);
        }

        [Fact]
        public async Task TestBackspace()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "" });
            await editor.HandleKey(new ConsoleKeyInfo('S', ConsoleKey.S, false, false, false));
            await editor.HandleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
            var text = editor._buffer.GetText().Trim();
            Assert.Equal("", text);
        }

        [Fact]
        public async Task TestStarExpansion()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "CREATE CONNECTION T1 AS MOCKDB();", "SELECT * FROM T1 AS A" });
            editor._buffer.CursorLine = 1;
            editor._buffer.CursorColumn = 8; // After *
            editor._metadata.RefreshConnections(editor._buffer.GetText(), force: true);
            await editor.HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, true)); // Ctrl+Space
            var text = editor._buffer.GetText();
            Assert.Contains("A.UserID", text);
            Assert.Contains("A.UserName", text);
        }

        [Fact]
        public async Task TestStarExpansionScenarios()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            string setup = "CREATE CONNECTION T1 AS MOCKDB();\nCREATE CONNECTION T2 AS MOCKDB();";
            editor._buffer.Load((setup + "\nSELECT * FROM T1 AS A, T2").Split('\n'));
            editor._metadata.RefreshConnections(editor._buffer.GetText(), force: true);
            editor._buffer.CursorLine = 2;
            editor._buffer.CursorColumn = 8; // After *
            await editor.HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, true)); // Ctrl+Space
            var text = editor._buffer.Lines[2];
            Assert.Contains("A.UserName", text);
            Assert.Contains("A.Email", text);
        }

        [Fact]
        public async Task TestStarExpansionForTempTableAfterRun()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._buffer.Load(new[]
            {
                "CREATE CONNECTION m AS MOCKDB();",
                "SELECT * INTO #temp FROM m.Users;",
                "SELECT * FROM #temp"
            });

            // Run so #temp is materialized in the evaluator's connections.
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));

            // Cursor just after the '*' on the last line ("SELECT *").
            editor._buffer.CursorLine = 2;
            editor._buffer.CursorColumn = 8;
            await editor.HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, true)); // Ctrl+Space

            var text = editor._buffer.Lines[2];
            Assert.Contains("UserID", text);
            Assert.Contains("UserName", text);
        }

        [Fact]
        public async Task TestPathAutocomplete()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "CREATE CONNECTION C AS FLATFILE('./" });
            editor._buffer.CursorLine = 0;
            editor._buffer.CursorColumn = editor._buffer.Lines[0].Length;
            await editor.HandleKey(new ConsoleKeyInfo('.', ConsoleKey.OemPeriod, false, false, false)); 
            // Assert something here if needed, currently it just verifies it doesn't crash
        }

        [Fact]
        public void TestAliasHighlighting()
        {
            var script = "SELECT * FROM T1 AS MyAlias";
            var aliases = ETLSuggestEngine.ParseAliases(script);
            bool ends;
            var highlighted = ETLSuggestEngine.HighlightLine("SELECT * FROM MyAlias", 0, 1000, false, out ends, aliases);
            Assert.Contains("[purple]MyAlias[/]", highlighted);
            Assert.Contains("[bold blue]SELECT[/]", highlighted);
        }

        [Fact]
        public void TestPathRooting()
        {
            var suggestions = ETLSuggestEngine.GetFileSuggestions("/");
            Assert.DoesNotContain(suggestions, s => s.Contains("$Recycle.Bin") || s.Contains("Documents and Settings"));
        }

        [Fact]
        public void TestDifferentiatedHighlighting()
        {
            // Case 1: FROM orders
            var script1 = "SELECT * FROM orders";
            var aliases1 = ETLSuggestEngine.ParseAliases(script1);
            bool dummy;
            var high1 = ETLSuggestEngine.HighlightLine("SELECT * FROM orders", 0, 1000, false, out dummy, aliases1);
            Assert.Contains("[cyan]orders[/]", high1);
            Assert.Contains("[bold blue]SELECT[/]", high1);

            // Case 2: FROM orders o
            var script2 = "SELECT * FROM orders o";
            var aliases2 = ETLSuggestEngine.ParseAliases(script2);
            bool dummy2;
            var high2 = ETLSuggestEngine.HighlightLine("SELECT * FROM orders o", 0, 1000, false, out dummy2, aliases2);
            Assert.Contains("[cyan]orders[/]", high2);
            Assert.Contains("[purple]o[/]", high2);
        }
        
        [Fact]
        public async Task TestNavigationWithAutocomplete()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "SELECT ", "FROM orders" });
            editor._buffer.CursorLine = 1; 
            editor._buffer.CursorColumn = 4; // After FROM
            editor._renderer.Headless = true;
            
            // Force autocomplete visible with 2 options
            editor._renderer.AutocompleteVisible = true;
            editor._renderer.AutocompleteOptions = new List<Suggestion> 
            { 
                new Suggestion("orders", SuggestionType.Table), 
                new Suggestion("customers", SuggestionType.Table) 
            };
            editor._renderer.AutocompleteIndex = 1; // "customers" selected

            // First UP should move index to 0
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal(0, editor._renderer.AutocompleteIndex);
            Assert.Equal(1, editor._buffer.CursorLine);

            // Second UP should hide autocomplete (current behavior)
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.False(editor._renderer.AutocompleteVisible);
            Assert.Equal(1, editor._buffer.CursorLine);

            // Third UP should move cursor to line 0
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal(0, editor._buffer.CursorLine);
        }

        [Fact]
        public async Task TestResultsFocusNavigation()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "SELECT 1" });
            editor._buffer.CursorLine = 0;
            editor._renderer.Headless = true;
            
            // Focus starts at Editor
            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);

            // Press F6 -> since no special panel is visible, toggles to the active lower tab
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F6, false, false, false));
            Assert.Equal(EditorFocus.Messages, editor._renderer.Focus);

            // Press F6 again -> toggles back to Editor
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F6, false, false, false));
            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);

            // Make results visible
            editor._renderer.ResultsVisible = true;

            // Press F6 -> toggles to Results
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F6, false, false, false));
            Assert.Equal(EditorFocus.Results, editor._renderer.Focus);
            Assert.True(editor._renderer.ResultsFocus);

            // UP should scroll results, not move editor cursor
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal(0, editor._buffer.CursorLine);

            // Press F6 -> toggles back to Editor
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F6, false, false, false));
            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);
            Assert.False(editor._renderer.ResultsFocus);
        }

        [Fact]
        public async Task TestRunScriptKeepsEditorFocusAndActivatesExecutionTreeTab()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "SELECT 1;" });
            editor._renderer.Headless = true;

            Assert.Equal(EditorFocus.Messages, editor._renderer.ActiveLowerTab);
            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));

            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);
            Assert.Equal(EditorFocus.ExecutionTree, editor._renderer.ActiveLowerTab);
        }

        [Fact]
        public void TestScrolling()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            var lines = Enumerable.Range(1, 100).Select(i => $"Line {i}").ToList();
            editor._buffer.Load(lines);
            editor._renderer.Headless = true;
            
            // Re-render with a specific height (assume editor height is 10)
            editor._renderer.Render(editor, 80, 20); // resultAreaHeight=8, editorAreaHeight=10
            
            // Move cursor to line 50
            editor._buffer.CursorLine = 50;
            editor._renderer.Render(editor, 80, 20);
            
            // ScrollLine should have updated to include line 50
            Assert.InRange(editor._renderer.ScrollLine, 40, 50);
        }

        [Fact]
        public void TestResultsScrollClamping()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.ResultsVisible = true;

            // Mock a result set with 4 rows
            var dataTable = new DataTable();
            dataTable.SetColumns(new[] { "Col1" });
            dataTable.Rows.Add(new Row(dataTable.Schema, new object?[] { "Val1" }));
            dataTable.Rows.Add(new Row(dataTable.Schema, new object?[] { "Val2" }));
            dataTable.Rows.Add(new Row(dataTable.Schema, new object?[] { "Val3" }));
            dataTable.Rows.Add(new Row(dataTable.Schema, new object?[] { "Val4" }));
            dataTable.TotalRowsMatched = 4;
            editor._evaluator.LastResultSets.Add(dataTable);

            // Attempt to scroll to 3 (index of 4th row)
            editor._renderer.ResultScrollRow = 3;
            // Render should perform the clamp
            editor._renderer.Render(editor, 80, 20);

            // It should be clamped to 3 (rowCount - 1), NOT to 0 as previously
            Assert.Equal(3, editor._renderer.ResultScrollRow);

            // Attempt to scroll past the limit (e.g., 5)
            editor._renderer.ResultScrollRow = 5;
            editor._renderer.Render(editor, 80, 20);

            // It should clamp to 3
            Assert.Equal(3, editor._renderer.ResultScrollRow);
        }

        [Fact]
        public async Task TestLongBufferNavigationKeepsCursorVisible()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(Enumerable.Range(1, 500).Select(i => $"Line {i}"));
            editor._renderer.Headless = true;

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, true));
            editor._renderer.Render(editor, 100, 24);

            Assert.Equal(499, editor._buffer.CursorLine);
            Assert.True(editor._renderer.ScrollLine <= editor._buffer.CursorLine);

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, true));
            editor._renderer.Render(editor, 100, 24);

            Assert.Equal(0, editor._buffer.CursorLine);
            Assert.Equal(0, editor._renderer.ScrollLine);
        }

        [Fact]
        public void TestLongBufferFindWrapsToNextMatch()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            var lines = Enumerable.Range(1, 250).Select(i => i == 25 || i == 240 ? $"SELECT {i} AS Needle;" : $"SELECT {i};");
            editor._buffer.Load(lines);
            editor._buffer.CursorLine = 200;
            editor._buffer.CursorColumn = 0;

            Assert.True(editor.TryFindNext("needle"));
            Assert.Equal(239, editor._buffer.CursorLine);

            Assert.True(editor.TryFindNext("needle"));
            Assert.Equal(24, editor._buffer.CursorLine);

            Assert.False(editor.TryFindNext("missing"));
            Assert.Equal(24, editor._buffer.CursorLine);
        }

        [Fact]
        public async Task TestControlPanelScrollTargetsFocusedOutputPanel()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "SELECT 1" });
            editor._renderer.Headless = true;

            editor._renderer.Focus = EditorFocus.Messages;
            editor._renderer.ResultScrollRow = 7;

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, true));

            Assert.Equal(EditorFocus.Messages, editor._renderer.Focus);
            Assert.Equal(10, editor._renderer.MessageScrollRow);
            Assert.Equal(7, editor._renderer.ResultScrollRow);

            editor._renderer.Focus = EditorFocus.Results;
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, true));

            Assert.Equal(17, editor._renderer.ResultScrollRow);
            Assert.Equal(10, editor._renderer.MessageScrollRow);
        }

        [Fact]
        public async Task TestDiagnosticsNavigationJumpsBetweenLintFindings()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[]
            {
                "SELECT 1;",
                "SELECT @first;",
                "SELECT 2;",
                "SELECT @second;"
            });
            editor._renderer.Headless = true;

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));

            Assert.True(editor.Diagnostics.Count >= 2);
            Assert.Contains(editor.Diagnostics, d => d.Message.Contains("@first"));
            Assert.Contains(editor.Diagnostics, d => d.Message.Contains("@second"));

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F8, false, false, false));
            Assert.Equal(1, editor._buffer.CursorLine);

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F8, false, false, false));
            Assert.Equal(3, editor._buffer.CursorLine);

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F8, true, false, false));
            Assert.Equal(1, editor._buffer.CursorLine);
        }

        [Fact]
        public async Task TestDiagnosticsNavigationWithNoFindingsKeepsCursorPosition()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "SELECT 1;" });
            editor._buffer.CursorLine = 0;
            editor._buffer.CursorColumn = 4;
            editor._renderer.Headless = true;

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F8, false, false, false));

            Assert.Empty(editor.Diagnostics);
            Assert.Equal(0, editor._buffer.CursorLine);
            Assert.Equal(4, editor._buffer.CursorColumn);
        }

        [Fact]
        public async Task TestRepeatedHeadlessRunsResetDiagnostics()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;

            editor._buffer.Load(new[] { "SELECT @missing;" });
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));
            Assert.NotEmpty(editor.Diagnostics);

            editor._buffer.Load(new[] { "SELECT 1;" });
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));
            Assert.Empty(editor.Diagnostics);
        }

        [Fact]
        public void TestLargeMessageAndResultOutputScrollStateIsClamped()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "SELECT 1;" });
            editor._renderer.Headless = true;

            for (int i = 0; i < 500; i++)
            {
                editor._evaluator.Log($"Message {i}: {new string('x', 80)}", ConsoleColor.White);
            }

            editor._renderer.MessageScrollRow = int.MaxValue;
            editor._renderer.Render(editor, 100, 24);
            Assert.InRange(editor._renderer.MessageScrollRow, 0, 500 * 8);

            var table = new DataTable();
            table.SetColumns(new[] { "Id", "Name" });
            for (int i = 0; i < 500; i++)
            {
                table.Rows.Add(new Row(table.Schema, new object?[] { i, $"Name {i}" }));
            }

            editor._evaluator.LastResult = table;
            editor._evaluator.LastResultSets.Add(table);
            editor._renderer.ResultsVisible = true;
            editor._renderer.MessageScrollRow = 0;
            editor._renderer.ResultScrollRow = int.MaxValue;

            editor._renderer.Render(editor, 100, 24);
            Assert.InRange(editor._renderer.ResultScrollRow, 0, table.Rows.Count);
        }

        [Fact]
        public async Task TestMultiCursor()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "A", "B", "C" });
            editor._buffer.CursorLine = 0;
            editor._buffer.CursorColumn = 1;
            editor._renderer.Headless = true;

            // Alt+Down to add cursor on line 1
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, true, false)); // Alt+Down
            
            // Type 'X'
            await editor.HandleKey(new ConsoleKeyInfo('X', ConsoleKey.X, false, false, false));
            
            var text = editor._buffer.GetText();
            Assert.Contains("AX", text);
            Assert.Contains("BX", text);
        }

        [Fact]
        public async Task TestMultiCursorBoundaryDoesNotThrow()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "Only line" });
            editor._buffer.CursorLine = 0;
            editor._buffer.CursorColumn = 4;
            editor._renderer.Headless = true;

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, true, false));
            Assert.Equal(0, editor._buffer.CursorLine);
            Assert.Equal(4, editor._buffer.CursorColumn);
            Assert.False(editor._buffer.IsMultiLineMode);
            Assert.Empty(editor._buffer.SecondaryCursors);

            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, true, false));
            Assert.Equal(0, editor._buffer.CursorLine);
            Assert.Equal(4, editor._buffer.CursorColumn);
            Assert.False(editor._buffer.IsMultiLineMode);
            Assert.Empty(editor._buffer.SecondaryCursors);
        }

        [Fact]
        public void TestMouseClickAndScroll()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[]
            {
                "SELECT 1;",
                "SELECT 222;",
                "SELECT 33333;"
            });
            editor._renderer.Headless = true;
            
            // Set terminal size (width=80, height=24)
            editor._renderer.Render(editor, 80, 24);

            // Left Click (button 0) at x=6, y=3
            // Line count is 3, so gutter is "3".Length + 2 = 3 characters.
            // clickLine = y - editorAreaTop (2) = 1 (second line: "SELECT 222;")
            // clickCol = x - gutter (3) = 3 (character position 3, i.e., 'E' in "SELECT")
            editor._renderer.HandleMouseClick(0, 6, 3, false, editor);

            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);
            Assert.Equal(1, editor._buffer.CursorLine);
            Assert.Equal(3, editor._buffer.CursorColumn);

            // Scroll Up (button 64) in editor area (y=4)
            editor._renderer.ScrollLine = 10;
            editor._renderer.HandleMouseClick(64, 10, 4, false, editor);
            Assert.Equal(7, editor._renderer.ScrollLine); // scrollUp delta is -3

            // Scroll Down (button 65) in editor area (y=4)
            editor._renderer.HandleMouseClick(65, 10, 4, false, editor);
            Assert.Equal(10, editor._renderer.ScrollLine); // scrollDown delta is +3
        }

        [Fact]
        public async Task TestSidebarToggleAndNavigation()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;

            // Sidebar starts closed, focus starts at Editor
            Assert.False(editor._renderer.SidebarVisible);
            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);

            // Press F9 to toggle sidebar
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F9, false, false, false));
            Assert.True(editor._renderer.SidebarVisible);
            Assert.Equal(EditorFocus.Sidebar, editor._renderer.Focus);

            // Verify the root node is loaded
            Assert.NotEmpty(editor._renderer._sidebarPanel.RootNodes);
            var root = editor._renderer._sidebarPanel.RootNodes[0];
            Assert.True(root.IsDirectory);
            Assert.True(root.IsExpanded);

            // Get flat items count
            var items = editor._renderer._sidebarPanel.GetFlatVisibleItems();
            Assert.NotEmpty(items);

            // Move selection down
            editor._renderer._sidebarPanel.SelectedIndex = 0;
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
            Assert.Equal(1, editor._renderer._sidebarPanel.SelectedIndex);

            // Press Escape to return focus to Editor
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
            Assert.Equal(EditorFocus.Editor, editor._renderer.Focus);

            // Focus cycle via F6 (since sidebar is visible, should cycle Editor -> Sidebar)
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F6, false, false, false));
            Assert.Equal(EditorFocus.Sidebar, editor._renderer.Focus);

            // Cycle again via F6 (Sidebar -> active lower tab, which defaults to Messages)
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F6, false, false, false));
            Assert.Equal(EditorFocus.Messages, editor._renderer.Focus);

            // Toggle sidebar off using Ctrl+B
            await editor.HandleKey(new ConsoleKeyInfo('B', ConsoleKey.B, false, false, true)); // Ctrl+B
            Assert.False(editor._renderer.SidebarVisible);
        }

        [Fact]
        public async Task TestMultiFileTabsMouseAndKeyControls()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._renderer.Headless = true;
            editor._renderer.Render(editor, 80, 24);

            // Initially, we have 1 tab: "test.etlsql"
            Assert.Single(editor._tabs);
            Assert.Equal(0, editor._activeTabIndex);
            Assert.Equal("test.etlsql", editor._filePath);

            // 1. Create a new tab via keyboard shortcut Ctrl+T
            await editor.HandleKey(new ConsoleKeyInfo('T', ConsoleKey.T, false, false, true));
            Assert.Equal(2, editor._tabs.Count);
            Assert.Equal(1, editor._activeTabIndex);
            Assert.Equal("untitled.etlsql", editor._filePath);

            // 2. Switch back to tab 0 via keyboard shortcut Alt+LeftArrow
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, true, false));
            Assert.Equal(0, editor._activeTabIndex);
            Assert.Equal("test.etlsql", editor._filePath);

            // 3. Switch forward to tab 1 via keyboard shortcut Alt+RightArrow
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, true, false));
            Assert.Equal(1, editor._activeTabIndex);
            Assert.Equal("untitled.etlsql", editor._filePath);

            // 4. Mouse-click the first tab body (select). Coordinates derive from the shared
            //    TabBarLayout so the test tracks the real rendered geometry.
            var segs = TabBarLayout.Tabs(TabLabels(editor)).ToList();
            await editor._renderer.HandleMouseClick(0, segs[0].StartX + 2, 1, false, editor);
            Assert.Equal(0, editor._activeTabIndex);
            Assert.Equal("test.etlsql", editor._filePath);

            // 5. Mouse-click the '+' button to create a new tab.
            int plusX = TabBarLayout.PlusStartX(TabLabels(editor));
            await editor._renderer.HandleMouseClick(0, plusX + 1, 1, false, editor);
            Assert.Equal(3, editor._tabs.Count);
            Assert.Equal(2, editor._activeTabIndex);

            // 6. Mouse-click the 'x' close button of tab index 1.
            var segs2 = TabBarLayout.Tabs(TabLabels(editor)).ToList();
            await editor._renderer.HandleMouseClick(0, segs2[1].CloseX, 1, false, editor);
            Assert.Equal(2, editor._tabs.Count);

            // 7. Test that switching tabs saves and restores the bottom panel results state
            var dummyTable = new ETL_SQL.Data.DataTable();
            editor._evaluator.LastResultSets.Add(dummyTable);
            Assert.Single(editor._evaluator.LastResultSets);

            // Switch tab (Alt+LeftArrow) to tab 0 (which has no results)
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, true, false));
            // Tab 0 should have empty results
            Assert.Empty(editor._evaluator.LastResultSets);

            // Switch forward (Alt+RightArrow) back to tab 1 (which had the results)
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, true, false));
            // Tab 1's results should be restored!
            Assert.Single(editor._evaluator.LastResultSets);
        }
    }
}
