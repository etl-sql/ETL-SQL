using System;
using System.Collections.Generic;
using Xunit;
using Terminal.Gui;
using NStack;
using ETL_SQL.TUI.UI;
using ETL_SQL.App;
using ETL_SQL;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Headless Terminal.Gui tests — uses FakeDriver so no real console is needed.
    /// Each test class gets its own init/shutdown to avoid cross-test state.
    /// </summary>
    public class IdeWindowTests : IDisposable
    {
        private readonly TerminalIdeWindow _window;

        public IdeWindowTests()
        {
            Application.Init(new FakeDriver(), null);
            var ctx = new CliContext { Command = "ui", UiMode = "edit" };
            _window = new TerminalIdeWindow(ctx, serviceProvider: null);
            Application.Top.Add(_window);
        }

        public void Dispose()
        {
            Application.Shutdown();
        }

        // ── Construction ─────────────────────────────────────────────────────

        [Fact]
        public void Constructor_AllViewsCreated()
        {
            Assert.NotNull(_window._editor);
            Assert.NotNull(_window._resultsView);
            Assert.NotNull(_window._messagesView);
            Assert.NotNull(_window._treeView);
            Assert.NotNull(_window._perfView);
            Assert.NotNull(_window._tabView);
        }

        [Fact]
        public void Constructor_BuiltinAutocomplete_IsConfigured()
        {
            Assert.NotNull(_window._editor.Autocomplete);
            // Enter is used instead of Tab to avoid conflict with Terminal.Gui's default Tab navigation
            Assert.Equal(Key.Enter, _window._editor.Autocomplete.SelectionKey);
        }

        [Fact]
        public void Constructor_DefaultTab_IsExecuteTree()
        {
            // Execute Tree is the default tab so users immediately see execution progress on run
            Assert.Equal("results", _window._activeTab);
            Assert.Equal("Execute Tree", _window._tabView.SelectedTab.Text.ToString());
        }

        [Fact]
        public void Constructor_TabView_HasFourTabs()
        {
            Assert.Equal(4, _window._tabView.Tabs.Count);
        }

        // ── Tab switching ─────────────────────────────────────────────────────

        [Fact]
        public void SwitchTab_Results_IsActiveTab()
        {
            _window.SwitchTab("messages");
            _window.SwitchTab("results");

            Assert.Equal("results", _window._activeTab);
            Assert.Equal("Results", _window._tabView.SelectedTab.Text.ToString());
        }

        [Fact]
        public void SwitchTab_Messages_IsActiveTab()
        {
            _window.SwitchTab("messages");

            Assert.Equal("messages", _window._activeTab);
            Assert.Equal("Messages", _window._tabView.SelectedTab.Text.ToString());
        }

        [Fact]
        public void SwitchTab_Tree_IsActiveTab()
        {
            _window.SwitchTab("tree");

            Assert.Equal("tree", _window._activeTab);
            Assert.Equal("Execute Tree", _window._tabView.SelectedTab.Text.ToString());
        }

        [Fact]
        public void SwitchTab_Perf_IsActiveTab()
        {
            _window.SwitchTab("perf");

            Assert.Equal("perf", _window._activeTab);
            Assert.Equal("Perf", _window._tabView.SelectedTab.Text.ToString());
        }

        [Fact]
        public void SwitchTab_SameTab_DoesNotThrow()
        {
            _window.SwitchTab("results");
            var ex = Record.Exception(() => _window.SwitchTab("results"));
            Assert.Null(ex);
        }

        [Fact]
        public void SwitchTab_UpdatesActiveTabField()
        {
            _window.SwitchTab("results");  Assert.Equal("results",  _window._activeTab);
            _window.SwitchTab("messages"); Assert.Equal("messages", _window._activeTab);
            _window.SwitchTab("tree");     Assert.Equal("tree",     _window._activeTab);
            _window.SwitchTab("perf");     Assert.Equal("perf",     _window._activeTab);
        }

        // ── Status bar ────────────────────────────────────────────────────────

        [Fact]
        public void UpdateStatusBar_NoFile_DoesNotThrow()
        {
            _window._currentFilePath = null;
            var ex = Record.Exception(() => _window.UpdateStatusBar());
            Assert.Null(ex);
        }

        // ── Autocomplete (built-in TextViewAutocomplete) ──────────────────────
        // NOTE: HostControl is set in TerminalIdeWindow constructor but a bare SyntaxTextView
        // does NOT auto-wire it. Tests that call GenerateSuggestions must rely on the fact
        // that TerminalIdeWindow already wired HostControl = _editor in its constructor.
        // Visible must also be set explicitly after GenerateSuggestions (it doesn't auto-set).

        [Fact]
        public void Autocomplete_StartsNotVisible()
        {
            Assert.False(_window._editor.Autocomplete.Visible);
        }

        [Fact]
        public void AcceptSuggestion_Tab_ReplacesPrefixInEditor()
        {
            // Arrange: editor contains "SEL", cursor at end
            _window._editor.Text = ustring.Make("SEL");
            _window._editor.CursorPosition = new Point(3, 0);

            // Populate and show via built-in GenerateSuggestions.
            // HostControl is already wired by TerminalIdeWindow constructor.
            // Visible must be set manually — GenerateSuggestions does not set it.
            _window._editor.Autocomplete.AllSuggestions = new List<string> { "SELECT" };
            _window._editor.Autocomplete.GenerateSuggestions(0);
            _window._editor.Autocomplete.Visible = true;

            // Act: ProcessKey(Tab) → TextViewAutocomplete.ProcessKey runs first,
            // accepts via InsertSelection / DeleteTextBackwards + InsertText
            _window._editor.ProcessKey(new KeyEvent(Key.Tab, new KeyModifiers()));

            // Assert: prefix replaced (Visible stays true after acceptance — that's fine,
            // the next keystroke's UpdateAutocompleteAsync will evaluate fresh state)
            Assert.Contains("SELECT", _window._editor.Text.ToString());
        }

        [Fact]
        public void AcceptSuggestion_Tab_MidLine_OnlyReplacesPrefixNotSuffix()
        {
            // "SEL FROM" — cursor at position 3 (after SEL), suffix must be preserved
            _window._editor.Text = ustring.Make("SEL FROM");
            _window._editor.CursorPosition = new Point(3, 0);

            _window._editor.Autocomplete.AllSuggestions = new List<string> { "SELECT" };
            _window._editor.Autocomplete.GenerateSuggestions(0);
            _window._editor.Autocomplete.Visible = true;

            _window._editor.ProcessKey(new KeyEvent(Key.Tab, new KeyModifiers()));

            var result = _window._editor.Text.ToString();
            Assert.Contains("SELECT", result);
            Assert.Contains("FROM", result);
        }

        [Fact]
        public void Autocomplete_Escape_ClosesPopup()
        {
            _window._editor.Text = ustring.Make("SEL");
            _window._editor.CursorPosition = new Point(3, 0);
            _window._editor.Autocomplete.AllSuggestions = new List<string> { "SELECT" };
            _window._editor.Autocomplete.GenerateSuggestions(0);
            _window._editor.Autocomplete.Visible = true;

            _window._editor.ProcessKey(new KeyEvent(Key.Esc, new KeyModifiers()));

            Assert.False(_window._editor.Autocomplete.Visible);
        }

        // ── Format ────────────────────────────────────────────────────────────

        [Fact]
        public void FormatScript_EmptyEditor_DoesNotThrow()
        {
            _window._editor.Text = "";
            var ex = Record.Exception(() => _window.FormatScript());
            Assert.Null(ex);
        }

        [Fact]
        public void FormatScript_ValidSql_KeywordsAreUppercase()
        {
            _window._editor.Text = "select id,name from orders where id=1";
            _window.FormatScript();
            Assert.Contains("SELECT", _window._editor.Text.ToString());
        }
    }
}
