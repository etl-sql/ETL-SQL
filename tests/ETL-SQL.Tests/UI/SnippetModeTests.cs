using Xunit;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.TUI.UI;
using ETL_SQL.Core;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Snippet tab-stop mode activation and its visual hint.</summary>
    public class SnippetModeTests
    {
        static SnippetModeTests()
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

        private static void AcceptSuggestion(ConsoleEditor editor, string text)
        {
            editor._buffer.Load(new[] { "sel" });
            editor._buffer.CursorLine = 0;
            editor._buffer.CursorColumn = 3;
            editor._renderer.AutocompleteVisible = true;
            editor._renderer.AutocompleteOptions = new List<Suggestion> { new(text, default) };
            editor._renderer.AutocompleteIndex = 0;
            editor._autocomplete.Accept();
        }

        [Fact]
        public void AcceptingSnippetWithPlaceholder_ActivatesModeAndShowsHint()
        {
            var editor = NewEditor();
            AcceptSuggestion(editor, "SELECT «col» FROM «tbl»");

            Assert.True(editor._renderer.SnippetModeActive);
            Assert.Contains("Snippet mode", editor._renderer.StatusMessage);
            // The first placeholder is selected, ready to type over.
            Assert.True(editor._buffer.SelectionStartLine.HasValue);
        }

        [Fact]
        public void AcceptingPlainSuggestion_DoesNotActivateSnippetMode()
        {
            var editor = NewEditor();
            AcceptSuggestion(editor, "SELECT");

            Assert.False(editor._renderer.SnippetModeActive);
        }

        [Fact]
        public async System.Threading.Tasks.Task SnippetSuggestion_ShowsTriggerLabel_InsertsBody()
        {
            var editor = NewEditor();
            editor._buffer.Load(new[] { "$mssql" });
            editor._buffer.CursorLine = 0;
            editor._buffer.CursorColumn = 6;

            await editor._autocomplete.UpdateAsync();

            var opt = editor._renderer.AutocompleteOptions
                .FirstOrDefault(o => o.Label == "$mssql");
            Assert.NotNull(opt);                              // list shows the trigger, not the body
            Assert.Contains("MSSQL", opt!.Text);              // but the body is what gets inserted
            Assert.Equal("$mssql", AutocompleteController.ToDisplayLabel(opt.Label!, 20));
        }
    }
}
