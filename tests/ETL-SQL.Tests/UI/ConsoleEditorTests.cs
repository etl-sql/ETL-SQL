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

namespace ETL_SQL.Tests
{
    public class ConsoleEditorTests
    {
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
            editor._buffer.Load(new[] { "CREATE CONNECTION T1 ON MOCKDB('mock');", "SELECT * FROM T1 AS A" });
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
            string setup = "CREATE CONNECTION T1 ON MOCKDB;\nCREATE CONNECTION T2 ON MOCKDB;";
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
        public async Task TestPathAutocomplete()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            editor._buffer.Load(new[] { "CREATE CONNECTION C ON FLATFILE('./" });
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
            var highlighted = ETLSuggestEngine.HighlightLine("SELECT * FROM MyAlias", aliases);
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
            var high1 = ETLSuggestEngine.HighlightLine("SELECT * FROM orders", aliases1);
            Assert.Contains("[cyan]orders[/]", high1);
            Assert.Contains("[bold blue]SELECT[/]", high1);

            // Case 2: FROM orders o
            var script2 = "SELECT * FROM orders o";
            var aliases2 = ETLSuggestEngine.ParseAliases(script2);
            var high2 = ETLSuggestEngine.HighlightLine("SELECT * FROM orders o", aliases2);
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
            
            // Toggle focus to results
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, false, false, false));
            Assert.True(editor._renderer.ResultsFocus);

            // UP should scroll results, not move editor cursor
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
            Assert.Equal(0, editor._buffer.CursorLine);

            // F3 back to editor
            await editor.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, false, false, false));
            Assert.False(editor._renderer.ResultsFocus);
        }

        [Fact]
        public void TestScrolling()
        {
            var editor = new ConsoleEditor("test.etlsql", new Dictionary<string, IDataSource>());
            var lines = Enumerable.Range(1, 100).Select(i => $"Line {i}").ToList();
            editor._buffer.Load(lines);
            editor._renderer.Headless = true;
            
            // Re-render with a specific height (assume editor height is 10)
            editor._renderer.Render(editor._buffer, editor._evaluator, "test.etlsql", false, 80, 20); // resultAreaHeight=8, editorAreaHeight=10
            
            // Move cursor to line 50
            editor._buffer.CursorLine = 50;
            editor._renderer.Render(editor._buffer, editor._evaluator, "test.etlsql", false, 80, 20);
            
            // ScrollLine should have updated to include line 50
            Assert.InRange(editor._renderer.ScrollLine, 40, 50);
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
    }
}
