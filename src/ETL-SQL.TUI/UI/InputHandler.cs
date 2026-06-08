using System;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Processes and dispatches keyboard input for the <see cref="ConsoleEditor"/>.
    /// Handles shortcuts, navigation, and state transitions between editor, results, and prompts.
    /// </summary>
    public class InputHandler
    {
        private readonly ConsoleEditor _editor;
        private readonly EditorBuffer _buffer;
        private readonly EditorRenderer _renderer;
        private readonly AutocompleteController _autocomplete;

        /// <summary>Initializes a new instance of the <see cref="InputHandler"/> class.</summary>
        /// <param name="editor">The parent editor instance.</param>
        /// <param name="buffer">The text buffer.</param>
        /// <param name="renderer">The renderer.</param>
        /// <param name="autocomplete">The autocomplete controller.</param>
        public InputHandler(ConsoleEditor editor, EditorBuffer buffer, EditorRenderer renderer, AutocompleteController autocomplete)
        {
            _editor = editor;
            _buffer = buffer;
            _renderer = renderer;
            _autocomplete = autocomplete;
        }

        /// <summary>Processes a key press in the primary editor or results context.</summary>
        /// <param name="key">The key information from the console.</param>
        public async Task HandleKey(ConsoleKeyInfo key)
        {
            if (_renderer.Focus == EditorFocus.Sidebar)
            {
                if (key.Key == ConsoleKey.UpArrow)
                {
                    var items = _renderer._sidebarPanel.GetFlatVisibleItems();
                    if (items.Count > 0)
                    {
                        _renderer._sidebarPanel.SelectedIndex = Math.Max(0, _renderer._sidebarPanel.SelectedIndex - 1);
                        if (_renderer._sidebarPanel.SelectedIndex < _renderer.SidebarScrollRow)
                        {
                            _renderer.SidebarScrollRow = _renderer._sidebarPanel.SelectedIndex;
                        }
                    }
                    _renderer.ForceFullRepaint();
                    return;
                }
                if (key.Key == ConsoleKey.DownArrow)
                {
                    var items = _renderer._sidebarPanel.GetFlatVisibleItems();
                    if (items.Count > 0)
                    {
                        _renderer._sidebarPanel.SelectedIndex = Math.Min(items.Count - 1, _renderer._sidebarPanel.SelectedIndex + 1);
                        int currentHeight = _renderer.LastHeight > 0 ? _renderer.LastHeight : 24;
                        int maxVisible = Math.Max(3, currentHeight - 1 - 2 - 2);
                        if (_renderer._sidebarPanel.SelectedIndex >= _renderer.SidebarScrollRow + maxVisible)
                        {
                            _renderer.SidebarScrollRow = _renderer._sidebarPanel.SelectedIndex - maxVisible + 1;
                        }
                    }
                    _renderer.ForceFullRepaint();
                    return;
                }
                if (key.Key == ConsoleKey.LeftArrow)
                {
                    _renderer._sidebarPanel.HandleLeft();
                    return;
                }
                if (key.Key == ConsoleKey.RightArrow)
                {
                    await _renderer._sidebarPanel.HandleRight();
                    return;
                }
                if (key.Key == ConsoleKey.Enter)
                {
                    await _renderer._sidebarPanel.HandleEnter(_editor);
                    return;
                }
                if (key.Key == ConsoleKey.Spacebar && key.Modifiers == 0)
                {
                    await _renderer._sidebarPanel.HandleEnter(_editor);
                    return;
                }
                if (key.Key == ConsoleKey.Tab)
                {
                    await _renderer._sidebarPanel.ToggleModeAsync(_editor.CurrentScriptText);
                    return;
                }
                if (key.Key == ConsoleKey.I && key.Modifiers == 0)
                {
                    _renderer._sidebarPanel.InsertSelected(_editor);
                    return;
                }
                if (key.Key == ConsoleKey.Escape)
                {
                    _renderer.Focus = EditorFocus.Editor;
                    _renderer.ForceFullRepaint();
                    return;
                }

                // If it's a character typing key with no modifiers, ignore it
                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) && key.Modifiers == 0)
                {
                    return;
                }
            }

            if (_renderer.Focus == EditorFocus.Output)
            {
                int count = _renderer.OutputEntries.Count;
                if (key.Key == ConsoleKey.UpArrow)
                {
                    _renderer.OutputSelectedIndex = Math.Max(0, _renderer.OutputSelectedIndex - 1);
                    if (_renderer.OutputSelectedIndex < _renderer.OutputScrollRow) _renderer.OutputScrollRow = _renderer.OutputSelectedIndex;
                    _renderer.ForceFullRepaint(); return;
                }
                if (key.Key == ConsoleKey.DownArrow)
                {
                    _renderer.OutputSelectedIndex = Math.Min(Math.Max(0, count - 1), _renderer.OutputSelectedIndex + 1);
                    _renderer.ForceFullRepaint(); return;
                }
                if (key.Key == ConsoleKey.Enter) { await _editor.OpenSelectedOutput(); return; }
                if (key.Key == ConsoleKey.C && key.Modifiers == 0) { await _editor.CopySelectedOutput(); return; }
                if (key.Key == ConsoleKey.Escape) { _renderer.Focus = EditorFocus.Editor; _renderer.ForceFullRepaint(); return; }
                // Swallow plain typing while the Output list is focused.
                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) && key.Modifiers == 0) return;
            }

            if (key.Key == ConsoleKey.Spacebar && key.Modifiers == 0)
            {
                _editor.MarkDirty();
                if (_buffer.SelectionStartLine.HasValue) _buffer.DeleteSelection();
                _buffer.InsertChar(' ');
                _renderer.AutocompleteVisible = false;
                return;
            }

            if (_renderer.AutocompleteVisible)
            {
                if (_autocomplete.HandleKey(key)) return;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                _renderer.IsBottomMaximized = false;
                if (_renderer.SnippetModeActive)
                {
                    ExitSnippetMode();
                    return;
                }
                if (_buffer.SelectionStartLine.HasValue || _buffer.IsMultiLineMode)
                {
                    _buffer.ClearSelection();
                    _renderer.ForceFullRepaint();
                    _renderer.ShowStatus("Selection cleared.");
                    return;
                }
                if (!string.IsNullOrEmpty(_renderer.FindTerm))
                {
                    _editor.ClearFind();
                    _renderer.ForceFullRepaint();
                    return;
                }
                return;
            }

            // Shift+F1 - Help at cursor (function/keyword help)
            if (key.Key == ConsoleKey.F1 && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                await _editor.ShowHelpAtCursor();
                return;
            }

            // Ctrl+L - Lineage at cursor
            if (key.Key == ConsoleKey.L && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                await _editor.ShowLineageAtCursor();
                return;
            }

            // F1 - Help
            if (key.Key == ConsoleKey.F1)
            {
                await _editor.ShowHelp();
                return;
            }

            // Ctrl+Space for Autocomplete
            if ((key.Key == ConsoleKey.Spacebar && key.Modifiers.HasFlag(ConsoleModifiers.Control)) || (key.KeyChar == '\0' && key.Key == ConsoleKey.Spacebar))
            {
                _renderer.AutocompleteVisible = false;
                await _autocomplete.TrySuggestAsync();
                return;
            }

            // F5 - Run
            if (key.Key == ConsoleKey.F5)
            {
                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift)) await _editor.RunStatementAtCursor(); 
                else if (key.Modifiers.HasFlag(ConsoleModifiers.Control)) await _editor.RunSelectedText();
                else await _editor.RunScript();
                return;
            }

            // Shortcuts
            if (key.Key == ConsoleKey.F2)
            {
                if (_renderer.HelpVisible)
                {
                    _renderer.HelpPageIndex = _renderer.HelpPageIndex == 0 ? 1 : 0;
                    _renderer.ForceFullRepaint();
                    return;
                }
                await _editor.SaveScript(key.Modifiers.HasFlag(ConsoleModifiers.Shift));
                return;
            }
            if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.HandleExit(); return; }
            if (key.Key == ConsoleKey.A && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _buffer.SelectAll(); return; }
            if (key.Key == ConsoleKey.Z && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.Undo(); return; }
            if (key.Key == ConsoleKey.Y && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.Redo(); return; }
            if (key.Key == ConsoleKey.S && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.SaveScript(key.Modifiers.HasFlag(ConsoleModifiers.Shift)); return; }
            if (key.Key == ConsoleKey.O && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await ShowOpenPrompt(); return; }
            if (key.Key == ConsoleKey.N && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.NewFile(); return; }
            if (key.Key == ConsoleKey.T && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.NewTab(); return; }
            if (key.Key == ConsoleKey.W && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.CloseActiveTab(); return; }
            if (key.Key == ConsoleKey.LeftArrow && key.Modifiers.HasFlag(ConsoleModifiers.Alt))
            {
                int prevIndex = (_editor._activeTabIndex - 1 + _editor._tabs.Count) % _editor._tabs.Count;
                _editor.SwitchToTab(prevIndex);
                return;
            }
            if (key.Key == ConsoleKey.RightArrow && key.Modifiers.HasFlag(ConsoleModifiers.Alt))
            {
                int nextIndex = (_editor._activeTabIndex + 1) % _editor._tabs.Count;
                _editor.SwitchToTab(nextIndex);
                return;
            }
            // Alt+P is the default (Windows Terminal grabs Ctrl+Shift+P for its own palette);
            // Ctrl+Shift+P still works in terminals that don't intercept it.
            if (key.Key == ConsoleKey.P && (key.Modifiers.HasFlag(ConsoleModifiers.Alt) || (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Modifiers.HasFlag(ConsoleModifiers.Shift)))) { await _editor.ShowCommandPalette(); return; }
            if (key.Key == ConsoleKey.P && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.ExportResults(); return; }
            if (key.Key == ConsoleKey.R && key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Modifiers.HasFlag(ConsoleModifiers.Shift)) { await _editor.ServeInBrowser(); return; }
            if (key.Key == ConsoleKey.R && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor._evaluator.ClearResults(); _renderer.ShowStatus("Results cleared."); return; }
            if (key.Key == ConsoleKey.F && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (_renderer.CompareMode) await _editor.FilterComparePane(_renderer.CompareFocusIndex);
                else if (_renderer.ResultsFocus) await _editor.FilterResults();
                else await _editor.Find();
                return;
            }
            if (key.Key == ConsoleKey.F && key.Modifiers.HasFlag(ConsoleModifiers.Alt)) { _editor.FormatScript(); return; }
            if (key.Key == ConsoleKey.I && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.FormatScript(); return; }
            if (key.Key == ConsoleKey.F12) { _editor.FormatScript(); return; }
            if (key.Key == ConsoleKey.H && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.Replace(); return; }
            if (key.Key == ConsoleKey.G && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.GoToLine(); return; }
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.Copy(); return; }
            if ((key.Key == ConsoleKey.V || key.Key == ConsoleKey.U) && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.Paste(); return; }
            if (key.Key == ConsoleKey.X && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.Cut(); return; }
            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.DuplicateLine(); return; }
            if (key.Key == ConsoleKey.K && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.DeleteLine(); return; }
            if (key.Key == ConsoleKey.Home && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.GoToTop(); return; }
            if (key.Key == ConsoleKey.End && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.GoToBottom(); return; }
            if (key.Key == ConsoleKey.Oem2 && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                _editor.MarkDirty(); _editor.SaveUndoState();
                _buffer.ToggleLineComment();
                return;
            }

            // Tab / Shift+Tab — snippet placeholder navigation takes priority over indent
            if (key.Key == ConsoleKey.Tab && _renderer.SnippetModeActive)
            {
                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                    MoveToPrevPlaceholder();
                else
                    MoveToNextPlaceholder();
                return;
            }

            // Tab / Shift+Tab — indent or outdent (selection-aware, handled before selection-clear logic)
            if (key.Key == ConsoleKey.Tab)
            {
                _editor.MarkDirty(); _editor.SaveUndoState();
                _buffer.IndentSelection(key.Modifiers.HasFlag(ConsoleModifiers.Shift));
                return;
            }

            // ── Global Bottom Panel Scrolling ──
            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (TryScrollCurrentPanel(key)) return;

                // Word jump — editor only (results focus handles Ctrl+Left/Right separately)
                if (!_renderer.ResultsFocus && key.Key == ConsoleKey.LeftArrow)
                {
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                    {
                        if (!_buffer.SelectionStartLine.HasValue) { _buffer.SelectionStartLine = _buffer.CursorLine; _buffer.SelectionStartCol = _buffer.CursorColumn; }
                    }
                    else { _buffer.SelectionStartLine = null; }
                    _buffer.WordLeft(); return;
                }
                if (!_renderer.ResultsFocus && key.Key == ConsoleKey.RightArrow)
                {
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
                    {
                        if (!_buffer.SelectionStartLine.HasValue) { _buffer.SelectionStartLine = _buffer.CursorLine; _buffer.SelectionStartCol = _buffer.CursorColumn; }
                    }
                    else { _buffer.SelectionStartLine = null; }
                    _buffer.WordRight(); return;
                }
            }


            // F6 - Focus Toggle (Switches between Editor, Sidebar, and the active lower panel)
            if (key.Key == ConsoleKey.F6)
            {
                if (_renderer.Focus == EditorFocus.Editor)
                {
                    if (_renderer.SidebarVisible)
                    {
                        _renderer.Focus = EditorFocus.Sidebar;
                    }
                    else if (_renderer.VariablesVisible)
                    {
                        _renderer.Focus = EditorFocus.Variables;
                    }
                    else if (_renderer.OutputVisible)
                    {
                        _renderer.Focus = EditorFocus.Output;
                    }
                    else if (_renderer.ResultsVisible)
                    {
                        _renderer.Focus = EditorFocus.Results;
                    }
                    else if (_renderer.PerformanceVisible)
                    {
                        _renderer.Focus = EditorFocus.Performance;
                    }
                    else
                    {
                        _renderer.Focus = _renderer.ActiveLowerTab;
                    }
                }
                else if (_renderer.Focus == EditorFocus.Sidebar)
                {
                    if (_renderer.VariablesVisible)
                    {
                        _renderer.Focus = EditorFocus.Variables;
                    }
                    else if (_renderer.OutputVisible)
                    {
                        _renderer.Focus = EditorFocus.Output;
                    }
                    else if (_renderer.ResultsVisible)
                    {
                        _renderer.Focus = EditorFocus.Results;
                    }
                    else if (_renderer.PerformanceVisible)
                    {
                        _renderer.Focus = EditorFocus.Performance;
                    }
                    else
                    {
                        _renderer.Focus = _renderer.ActiveLowerTab;
                    }
                }
                else
                {
                    if (_renderer.Focus == EditorFocus.ExecutionTree || _renderer.Focus == EditorFocus.Messages)
                        _renderer.ActiveLowerTab = _renderer.Focus;
                    _renderer.Focus = EditorFocus.Editor;
                }

                _renderer.AutocompleteVisible = false;
                _renderer.ForceFullRepaint();
                
                string focusName = _renderer.Focus switch {
                    EditorFocus.Editor => "Editor",
                    EditorFocus.Sidebar => "File Explorer",
                    EditorFocus.ExecutionTree => "Pipeline Tree",
                    EditorFocus.Messages => "Messages",
                    EditorFocus.Results => "Query Results",
                    EditorFocus.Performance => "Performance Metrics",
                    EditorFocus.Output => "Output",
                    EditorFocus.Variables => "Variables",
                    _ => "Editor"
                };
                _renderer.ShowStatus($"Focus: {focusName}");
                return;
            }

            // F9 or Ctrl+B - Toggle Sidebar/Explorer
            if (key.Key == ConsoleKey.F9 || (key.Key == ConsoleKey.B && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
            {
                _renderer.SidebarVisible = !_renderer.SidebarVisible;
                if (_renderer.SidebarVisible)
                {
                    _renderer.Focus = EditorFocus.Sidebar;
                    _renderer._sidebarPanel.Initialize(_editor._filePath);
                }
                else if (_renderer.Focus == EditorFocus.Sidebar)
                {
                    _renderer.Focus = EditorFocus.Editor;
                }
                _renderer.ForceFullRepaint();
                _renderer.ShowStatus(_renderer.SidebarVisible ? "Sidebar opened" : "Sidebar closed");
                return;
            }

            // Ctrl+M - Maximize Bottom Panel
            if (key.Key == ConsoleKey.M && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                _renderer.IsBottomMaximized = !_renderer.IsBottomMaximized;
                _renderer.ForceFullRepaint();
                _renderer.ShowStatus(_renderer.IsBottomMaximized ? "Expand: Bottom Panel" : "Collapse: Bottom Panel");
                return;
            }

            // F7 — Enter / exit compare mode
            if (key.Key == ConsoleKey.F7)
            {
                if (_renderer.CompareMode)
                {
                    _renderer.CompareMode = false;
                    _renderer.IsBottomMaximized = false;
                    _renderer.ForceFullRepaint();
                    _renderer.ShowStatus("Compare mode off.");
                }
                else
                {
                    if (_editor._evaluator.LastResultSets.Count < 2)
                    {
                        _renderer.ShowStatus("Need at least 2 result sets to compare.");
                    }
                    else
                    {
                        _renderer.CompareMode = true;
                        _renderer.CompareFocusIndex = 0;
                        _renderer.CompareScrollRows = _editor._evaluator.LastResultSets.Select(_ => 0).ToList();
                        _renderer.CompareScrollCols = _editor._evaluator.LastResultSets.Select(_ => 0).ToList();
                        _renderer.CompareFilters    = _editor._evaluator.LastResultSets.Select(_ => "").ToList();
                        _renderer.ResultsVisible    = false;
                        _renderer.PerformanceVisible = false;
                        _renderer.IsBottomMaximized = true;
                        _renderer.ForceFullRepaint();
                        _renderer.ShowStatus($"Compare mode — {_editor._evaluator.LastResultSets.Count} sets. F7: next pane  ←/→: scroll cols  Ctrl+F: filter  Escape: exit");
                    }
                }
                return;
            }

            // F8 — cycle focused pane in compare mode
            if (key.Key == ConsoleKey.F8 && _renderer.CompareMode)
            {
                _renderer.CompareFocusIndex = (_renderer.CompareFocusIndex + 1) % _editor._evaluator.LastResultSets.Count;
                _renderer.ShowStatus($"Compare: pane {_renderer.CompareFocusIndex + 1} active");
                return;
            }

            if (key.Key == ConsoleKey.F8)
            {
                _editor.NavigateDiagnostic(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
                return;
            }

            // F3 - Find Next / Shift+F3 - Find Prev while a find is active; otherwise cycle theme.
            if (key.Key == ConsoleKey.F3)
            {
                if (!string.IsNullOrEmpty(_renderer.FindTerm))
                {
                    bool found = key.Modifiers.HasFlag(ConsoleModifiers.Shift)
                        ? _editor.TryFindPrev(_renderer.FindTerm)
                        : _editor.TryFindNext(_renderer.FindTerm);
                    _renderer.ShowStatus(found
                        ? $"Find '{_renderer.FindTerm}' — F3 next · Shift+F3 prev · Esc clear"
                        : $"'{_renderer.FindTerm}' not found.");
                    return;
                }
                string newTheme = TuiTheme.CycleTheme();
                _renderer.ShowStatus($"Theme: {newTheme}");
                _renderer.ForceFullRepaint();
                return;
            }

            // F4 - Cycle lower panel: Pipeline+Messages → Results → Performance → (repeat)
            if (key.Key == ConsoleKey.F4)
            {
                // Cycle: Pipeline+Messages → Results → Performance → Output → Variables → (repeat)
                if (_renderer.ResultsVisible)
                    { _renderer.ResultsVisible = false; _renderer.PerformanceVisible = true; _renderer.ShowStatus("View: Performance Metrics"); }
                else if (_renderer.PerformanceVisible)
                    { _renderer.PerformanceVisible = false; _renderer.OutputVisible = true; _renderer.ShowStatus("View: Output"); }
                else if (_renderer.OutputVisible)
                    { _renderer.OutputVisible = false; _renderer.VariablesVisible = true; _renderer.ShowStatus("View: Variables"); }
                else if (_renderer.VariablesVisible)
                    { _renderer.VariablesVisible = false; _renderer.ShowStatus("View: Pipeline & Messages"); }
                else
                    { _renderer.ResultsVisible = true; _renderer.ShowStatus("View: Query Results"); }
                _renderer.Focus = EditorFocus.Editor;
                _renderer.ForceFullRepaint();
                return;
            }

        // Alt+R - Toggle Report Preview (Phase 5)
        if (key.Key == ConsoleKey.R && key.Modifiers.HasFlag(ConsoleModifiers.Alt))
        {
            _renderer.ReportVisible = !_renderer.ReportVisible;
            if (_renderer.ReportVisible)
            {
                _renderer.Focus = EditorFocus.Editor;
                _renderer.AutocompleteVisible = false;

                // If no report has been built yet, try running the script automatically
                if (_renderer.CurrentReportManifest == null)
                {
                    _renderer.ShowStatus("Initializing report preview...");
                    await _editor.RunScript();
                }

                if (_renderer.CurrentReportManifest != null)
                    _renderer.ShowStatus("View: Report Preview (PgUp/PgDn: Scroll | Shift+PgUp/PgDn: Pages)");
                else
                    _renderer.ShowStatus("View: Report Preview (No report definitions found)");
            }
            else
            {
                _renderer.ShowStatus("View: Editor");
            }
            _renderer.ForceFullRepaint();
            return;
        }

        if (_renderer.ReportVisible)
        {
            HandleReportKey(key);
            return;
        }

            if (_renderer.CompareMode)
                {
                HandleCompareKey(key);
                return;
            }

            if (_renderer.ResultsFocus || _renderer.Focus == EditorFocus.ExecutionTree || _renderer.Focus == EditorFocus.Messages || _renderer.Focus == EditorFocus.Performance || _renderer.Focus == EditorFocus.Variables)
            {
                HandleFocusedPanelKey(key);
                return;
            }

            // Shift+Arrow selection
            if (key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                if (!_buffer.SelectionStartLine.HasValue) 
                { 
                    _buffer.SelectionStartLine = _buffer.CursorLine; 
                    _buffer.SelectionStartCol = _buffer.CursorColumn; 
                }
            }
            else if (!IsNavigationKey(key))
            {
                _buffer.SelectionStartLine = null;
                _buffer.SelectionStartCol = null;
            }

            // Standard Navigation & Editing
            switch (key.Key)
            {
                case ConsoleKey.Home: _buffer.Home(); break;
                case ConsoleKey.End: _buffer.End(); break;
                case ConsoleKey.UpArrow:
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Alt)) _buffer.AddMultiCursor(-1);
                    else if (_buffer.CursorLine > 0) { _buffer.CursorLine--; _buffer.CursorColumn = Math.Min(_buffer.CursorColumn, _buffer.Lines[_buffer.CursorLine].Length); }
                    break;
                case ConsoleKey.DownArrow:
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Alt)) _buffer.AddMultiCursor(1);
                    else if (_buffer.CursorLine < _buffer.Lines.Count - 1) { _buffer.CursorLine++; _buffer.CursorColumn = Math.Min(_buffer.CursorColumn, _buffer.Lines[_buffer.CursorLine].Length); }
                    break;
                case ConsoleKey.LeftArrow:
                    if (_buffer.CursorColumn > 0) _buffer.CursorColumn--;
                    else if (_buffer.CursorLine > 0) { _buffer.CursorLine--; _buffer.CursorColumn = _buffer.Lines[_buffer.CursorLine].Length; }
                    break;
                case ConsoleKey.RightArrow:
                    if (_buffer.CursorColumn < _buffer.Lines[_buffer.CursorLine].Length) _buffer.CursorColumn++;
                    else if (_buffer.CursorLine < _buffer.Lines.Count - 1) { _buffer.CursorLine++; _buffer.CursorColumn = 0; }
                    break;
                case ConsoleKey.PageUp: _buffer.CursorLine = Math.Max(0, _buffer.CursorLine - 10); _buffer.CursorColumn = Math.Min(_buffer.CursorColumn, _buffer.Lines[_buffer.CursorLine].Length); break;
                case ConsoleKey.PageDown: _buffer.CursorLine = Math.Min(_buffer.Lines.Count - 1, _buffer.CursorLine + 10); _buffer.CursorColumn = Math.Min(_buffer.CursorColumn, _buffer.Lines[_buffer.CursorLine].Length); break;
                case ConsoleKey.Enter: _editor.MarkDirty(); _editor.SaveUndoState(); _buffer.NewLine(); _renderer.AutocompleteVisible = false; break;
                case ConsoleKey.Backspace: _editor.MarkDirty(); _editor.SaveUndoState(); _buffer.Backspace(); await _autocomplete.UpdateAsync(); break;
                case ConsoleKey.Delete: _editor.MarkDirty(); _editor.SaveUndoState(); _buffer.Delete(); break;
                case ConsoleKey.Tab:
                default:
                    if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                    {
                        _editor.MarkDirty();
                        if (_buffer.SelectionStartLine.HasValue) _buffer.DeleteSelection();
                        _buffer.InsertChar(key.KeyChar);
                        await _autocomplete.UpdateAsync();
                    }
                    break;
            }
        }

        private bool TryScrollCurrentPanel(ConsoleKeyInfo key)
        {
            int delta = key.Key switch
            {
                ConsoleKey.UpArrow => -1,
                ConsoleKey.DownArrow => 1,
                ConsoleKey.PageUp => -10,
                ConsoleKey.PageDown => 10,
                _ => 0
            };

            if (delta == 0) return false;

            void ScrollResult() => _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow + delta);
            void ScrollTree() => _renderer.TreeScrollRow = Math.Max(0, _renderer.TreeScrollRow + delta);
            void ScrollMessages() => _renderer.MessageScrollRow = Math.Max(0, _renderer.MessageScrollRow + delta);

            switch (_renderer.Focus)
            {
                case EditorFocus.Results:
                case EditorFocus.Performance:
                    ScrollResult();
                    return true;
                case EditorFocus.ExecutionTree:
                    ScrollTree();
                    return true;
                case EditorFocus.Messages:
                    ScrollMessages();
                    return true;
                case EditorFocus.Variables:
                    _renderer.VariablesScrollRow = Math.Max(0, _renderer.VariablesScrollRow + delta);
                    return true;
            }

            if (_renderer.PerformanceVisible || _renderer.ResultsVisible) ScrollResult();
            else ScrollMessages();

            return true;
        }

        private void HandleCompareKey(ConsoleKeyInfo key)
        {
            int idx = _renderer.CompareFocusIndex;
            while (_renderer.CompareScrollRows.Count <= idx) _renderer.CompareScrollRows.Add(0);
            while (_renderer.CompareScrollCols.Count <= idx) _renderer.CompareScrollCols.Add(0);
            while (_renderer.CompareFilters.Count    <= idx) _renderer.CompareFilters.Add("");

            // Largest column offset that still leaves at least one column visible in the focused pane.
            int maxCol = idx < _editor._evaluator.LastResultSets.Count
                ? ResultsPanel.MaxColumnOffset(_editor._evaluator.LastResultSets[idx].ColumnNames.Count)
                : 0;

            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    _renderer.IsBottomMaximized = false;
                    if (!string.IsNullOrEmpty(_renderer.CompareFilters[idx]))
                    {
                        _renderer.CompareFilters[idx] = "";
                        _renderer.CompareScrollRows[idx] = 0;
                        _renderer.ShowStatus("Filter cleared.");
                    }
                    else
                    {
                        _renderer.CompareMode = false;
                        _renderer.ForceFullRepaint();
                        _renderer.ShowStatus("Compare mode off.");
                    }
                    break;
                case ConsoleKey.UpArrow:
                    _renderer.CompareScrollRows[idx] = Math.Max(0, _renderer.CompareScrollRows[idx] - 1); break;
                case ConsoleKey.DownArrow:
                    _renderer.CompareScrollRows[idx]++; break;
                case ConsoleKey.LeftArrow:
                    _renderer.CompareScrollCols[idx] = Math.Max(0, _renderer.CompareScrollCols[idx] - 1); break;
                case ConsoleKey.RightArrow:
                    _renderer.CompareScrollCols[idx] = Math.Min(maxCol, _renderer.CompareScrollCols[idx] + 1); break;
                case ConsoleKey.PageUp:
                    _renderer.CompareScrollRows[idx] = Math.Max(0, _renderer.CompareScrollRows[idx] - 10); break;
                case ConsoleKey.PageDown:
                    _renderer.CompareScrollRows[idx] += 10; break;
                case ConsoleKey.Home:
                    _renderer.CompareScrollRows[idx] = 0;
                    _renderer.CompareScrollCols[idx] = 0; break;
            }
        }

        private void HandleFocusedPanelKey(ConsoleKeyInfo key)
        {
            if (key.Key == ConsoleKey.Escape)
            {
                _renderer.IsBottomMaximized = false;
                if (!string.IsNullOrEmpty(_renderer.FilterText))
                {
                    _renderer.FilterText = "";
                    _renderer.ResultScrollRow = 0;
                    _renderer.ShowStatus("Filter cleared.");
                }
                else
                {
                    _renderer.Focus = EditorFocus.Editor;
                    _renderer.ShowStatus("Focused: Editor");
                }
                return;
            }

            switch (_renderer.Focus)
            {
                case EditorFocus.Results:
                    HandleResultsKey(key);
                    break;
                case EditorFocus.Performance:
                    HandlePerformanceKey(key);
                    break;
                case EditorFocus.ExecutionTree:
                    HandleTreeKey(key);
                    break;
                case EditorFocus.Messages:
                    HandleMessagesKey(key);
                    break;
                case EditorFocus.Variables:
                    HandleVariablesKey(key);
                    break;
            }
        }

        private void HandleVariablesKey(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _renderer.VariablesScrollRow = Math.Max(0, _renderer.VariablesScrollRow - 1); break;
                case ConsoleKey.DownArrow:
                    _renderer.VariablesScrollRow++; break;
                case ConsoleKey.PageUp:
                    _renderer.VariablesScrollRow = Math.Max(0, _renderer.VariablesScrollRow - 10); break;
                case ConsoleKey.PageDown:
                    _renderer.VariablesScrollRow += 10; break;
                case ConsoleKey.Home:
                    _renderer.VariablesScrollRow = 0; break;
            }
        }

        private void HandleResultsKey(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow: 
                    _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow - 1); 
                    break;
                case ConsoleKey.DownArrow: 
                    _renderer.ResultScrollRow++; 
                    break;
                case ConsoleKey.LeftArrow:
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Control)) _renderer.ResultScrollCol = Math.Max(0, _renderer.ResultScrollCol - 1);
                    else { _renderer.ActiveResultSetIndex = Math.Max(0, _renderer.ActiveResultSetIndex - 1); _renderer.ResultScrollRow = 0; _renderer.ResultScrollCol = 0; _renderer.FilterText = ""; }
                    break;
                case ConsoleKey.RightArrow:
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Control)) _renderer.ResultScrollCol++;
                    else { _renderer.ActiveResultSetIndex = Math.Min(_editor._evaluator.LastResultSets.Count - 1, _renderer.ActiveResultSetIndex + 1); _renderer.ResultScrollRow = 0; _renderer.ResultScrollCol = 0; _renderer.FilterText = ""; }
                    break;
                case ConsoleKey.PageUp: _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow - 10); break;
                case ConsoleKey.PageDown: _renderer.ResultScrollRow += 10; break;
                case ConsoleKey.Home: _renderer.ResultScrollRow = 0; break;
                case ConsoleKey.End: 
                    if (_editor._evaluator.LastResultSets.Count > 0) 
                    {
                        var res = _editor._evaluator.LastResultSets[_renderer.ActiveResultSetIndex];
                        int rowCount = res.Rows.Count;
                        if (!string.IsNullOrEmpty(_renderer.FilterText))
                        {
                            rowCount = res.Rows.Count(row => res.ColumnNames.Any(c =>
                                (row[c]?.ToString() ?? "").Contains(_renderer.FilterText, StringComparison.OrdinalIgnoreCase)));
                        }
                        _renderer.ResultScrollRow = Math.Max(0, rowCount - 1); 
                    }
                    break;
                case ConsoleKey.Tab:
                    if (_editor._evaluator.LastResultSets.Count > 1) {
                        _renderer.ActiveResultSetIndex = (_renderer.ActiveResultSetIndex + 1) % _editor._evaluator.LastResultSets.Count;
                        _renderer.ResultScrollRow = 0;
                        _renderer.ShowStatus($"View: Result Set {_renderer.ActiveResultSetIndex + 1}/{_editor._evaluator.LastResultSets.Count}");
                    }
                    break;
            }
        }

        private void HandlePerformanceKey(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow: _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow - 1); break;
                case ConsoleKey.DownArrow: _renderer.ResultScrollRow++; break;
                case ConsoleKey.PageUp: _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow - 10); break;
                case ConsoleKey.PageDown: _renderer.ResultScrollRow += 10; break;
                case ConsoleKey.Home: _renderer.ResultScrollRow = 0; break;
            }
        }

        private void HandleTreeKey(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow: _renderer.TreeScrollRow = Math.Max(0, _renderer.TreeScrollRow - 1); break;
                case ConsoleKey.DownArrow: _renderer.TreeScrollRow++; break;
                case ConsoleKey.PageUp: _renderer.TreeScrollRow = Math.Max(0, _renderer.TreeScrollRow - 10); break;
                case ConsoleKey.PageDown: _renderer.TreeScrollRow += 10; break;
                case ConsoleKey.Home: _renderer.TreeScrollRow = 0; break;
            }
        }

        private void HandleMessagesKey(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.UpArrow: _renderer.MessageScrollRow = Math.Max(0, _renderer.MessageScrollRow - 1); break;
                case ConsoleKey.DownArrow: _renderer.MessageScrollRow++; break;
                case ConsoleKey.PageUp: _renderer.MessageScrollRow = Math.Max(0, _renderer.MessageScrollRow - 10); break;
                case ConsoleKey.PageDown: _renderer.MessageScrollRow += 10; break;
                case ConsoleKey.Home: _renderer.MessageScrollRow = 0; break;
                case ConsoleKey.End: _renderer.MessageScrollRow = 50000; break; // Clamp will handle it
            }
        }

        private bool IsNavigationKey(ConsoleKeyInfo key)
        {
            return key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow ||
                   key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow ||
                   key.Key == ConsoleKey.Home || key.Key == ConsoleKey.End ||
                   key.Key == ConsoleKey.Backspace || key.Key == ConsoleKey.Delete;
        }

        private void MoveToNextPlaceholder()
        {
            int searchLine = _buffer.CursorLine;
            int searchCol = _buffer.SelectionStartLine.HasValue ? (_buffer.SelectionStartCol ?? _buffer.CursorColumn) : _buffer.CursorColumn;
            // Start search after the current selection end
            int afterCol = Math.Max(searchCol, _buffer.CursorColumn) + 1;

            var next = AutocompleteController.FindNextPlaceholder(_buffer, searchLine, afterCol)
                    ?? AutocompleteController.FindNextPlaceholder(_buffer, searchLine + 1, 0);

            if (next.HasValue)
                _buffer.SelectRange(next.Value.Line, next.Value.StartCol, next.Value.EndCol);
            else
                ExitSnippetMode();
        }

        private void MoveToPrevPlaceholder()
        {
            int searchLine = _buffer.CursorLine;
            int beforeCol = _buffer.SelectionStartLine.HasValue ? (_buffer.SelectionStartCol ?? _buffer.CursorColumn) : _buffer.CursorColumn;

            var prev = AutocompleteController.FindPrevPlaceholder(_buffer, searchLine, beforeCol);

            if (prev.HasValue)
                _buffer.SelectRange(prev.Value.Line, prev.Value.StartCol, prev.Value.EndCol);
            else
                ExitSnippetMode();
        }

        private void ExitSnippetMode()
        {
            bool wasActive = _renderer.SnippetModeActive;
            _renderer.SnippetModeActive = false;
            _buffer.SelectionStartLine = null;
            _buffer.SelectionStartCol = null;
            if (wasActive) _renderer.ShowStatus("Snippet mode exited.");
        }

        private async Task ShowOpenPrompt()
        {
            var path = await _editor.ShowPrompt("Open File", "");
            if (!string.IsNullOrEmpty(path)) await _editor.LoadFile(path);
        }

        /// <summary>Processes a key press when an interactive prompt is active.</summary>
        /// <param name="key">The key information from the console.</param>
        public async Task HandlePromptKey(ConsoleKeyInfo key)
        {
            if (key.Key == ConsoleKey.Enter)
            {
                // If empty or whitespace, treat as Cancel (null) to avoid logic errors or visual drift
                if (string.IsNullOrWhiteSpace(_renderer.PromptValue))
                {
                    _editor.ResolvePrompt(null);
                    return;
                }

                _editor.ResolvePrompt(_renderer.PromptValue);
                return;
            }

            if (key.Key == ConsoleKey.Escape)
            {
                _editor.ResolvePrompt(null);
                return;
            }

            if (key.Key == ConsoleKey.Tab)
            {
                // Trigger or cycle suggestions
                if (!_renderer.PromptSuggestions.Any())
                {
                    // Only do file suggestions if the prompt looks like a path request
                    if (_renderer.PromptTitle != null && (_renderer.PromptTitle.Contains("Open") || _renderer.PromptTitle.Contains("Save") || _renderer.PromptTitle.Contains("Export") || _renderer.PromptTitle.Contains("path")))
                    {
                        _renderer.PromptSuggestions = ETLSuggestEngine.GetFileSuggestions(_renderer.PromptValue);
                        _renderer.PromptSuggestionIndex = 0;
                    }
                }
                else
                {
                    _renderer.PromptSuggestionIndex = (_renderer.PromptSuggestionIndex + 1) % _renderer.PromptSuggestions.Count;
                }

                if (_renderer.PromptSuggestions.Any())
                {
                    var suggestion = _renderer.PromptSuggestions[_renderer.PromptSuggestionIndex];
                    _renderer.PromptValue = suggestion;
                    _renderer.PromptCursor = suggestion.Length;
                }
                return;
            }

            if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow)
            {
                if (_renderer.PromptSuggestions.Any())
                {
                    if (key.Key == ConsoleKey.UpArrow) _renderer.PromptSuggestionIndex = (_renderer.PromptSuggestionIndex - 1 + _renderer.PromptSuggestions.Count) % _renderer.PromptSuggestions.Count;
                    else _renderer.PromptSuggestionIndex = (_renderer.PromptSuggestionIndex + 1) % _renderer.PromptSuggestions.Count;
                    
                    _renderer.PromptValue = _renderer.PromptSuggestions[_renderer.PromptSuggestionIndex];
                    _renderer.PromptCursor = _renderer.PromptValue.Length;
                }
                return;
            }

            // Normal typing
            if (key.Key == ConsoleKey.Backspace)
            {
                if (_renderer.PromptCursor > 0)
                {
                    _renderer.PromptValue = _renderer.PromptValue.Remove(_renderer.PromptCursor - 1, 1);
                    _renderer.PromptCursor--;
                }
            }
            else if (key.Key == ConsoleKey.Delete)
            {
                if (_renderer.PromptCursor < _renderer.PromptValue.Length)
                {
                    _renderer.PromptValue = _renderer.PromptValue.Remove(_renderer.PromptCursor, 1);
                }
            }
            else if (key.Key == ConsoleKey.LeftArrow)
            {
                _renderer.PromptCursor = Math.Max(0, _renderer.PromptCursor - 1);
            }
            else if (key.Key == ConsoleKey.RightArrow)
            {
                _renderer.PromptCursor = Math.Min(_renderer.PromptValue.Length, _renderer.PromptCursor + 1);
            }
            else if (key.Key == ConsoleKey.Home)
            {
                _renderer.PromptCursor = 0;
            }
            else if (key.Key == ConsoleKey.End)
            {
                _renderer.PromptCursor = _renderer.PromptValue.Length;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                _renderer.PromptValue = _renderer.PromptValue.Insert(_renderer.PromptCursor, key.KeyChar.ToString());
                _renderer.PromptCursor++;
                // Clear suggestions on type
                _renderer.PromptSuggestions.Clear();
            }
        }
        private void HandleReportKey(ConsoleKeyInfo key)
        {
            if (key.Key == ConsoleKey.Escape)
            {
                _renderer.ReportVisible = false;
                _renderer.ShowStatus("View: Editor");
                _renderer.ForceFullRepaint();
                return;
            }

            var manifest = _renderer.CurrentReportManifest;
            if (manifest == null || manifest.Pages.Count == 0) return;

            // Page Navigation (Shift+PgUp/PgDn or Number keys)
            if (key.Key == ConsoleKey.PageUp && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                _renderer.ActiveReportPageIndex = Math.Max(0, _renderer.ActiveReportPageIndex - 1);
                _renderer.ReportScrollRow = 0;
                _renderer.ForceFullRepaint();
                return;
            }
            if (key.Key == ConsoleKey.PageDown && key.Modifiers.HasFlag(ConsoleModifiers.Shift))
            {
                _renderer.ActiveReportPageIndex = Math.Min(manifest.Pages.Count - 1, _renderer.ActiveReportPageIndex + 1);
                _renderer.ReportScrollRow = 0;
                _renderer.ForceFullRepaint();
                return;
            }

            // Left / Right arrows switch pages.
            if (key.Key == ConsoleKey.LeftArrow) { _renderer.ReportPrevPage(); return; }
            if (key.Key == ConsoleKey.RightArrow) { _renderer.ReportNextPage(); return; }

            // Scrolling (Arrows / PgUp / PgDn)
            if (key.Key == ConsoleKey.UpArrow) { _renderer.ReportScrollRow = Math.Max(0, _renderer.ReportScrollRow - 1); }
            else if (key.Key == ConsoleKey.DownArrow) { _renderer.ReportScrollRow++; }
            else if (key.Key == ConsoleKey.PageUp) { _renderer.ReportScrollRow = Math.Max(0, _renderer.ReportScrollRow - 10); }
            else if (key.Key == ConsoleKey.PageDown) { _renderer.ReportScrollRow += 10; }
            else if (key.Key == ConsoleKey.Home) { _renderer.ReportScrollRow = 0; }
            
            else if (key.Key >= ConsoleKey.D1 && key.Key <= ConsoleKey.D9)
            {
                int index = (int)key.Key - (int)ConsoleKey.D1;
                if (index < manifest.Pages.Count)
                {
                    _renderer.ActiveReportPageIndex = index;
                    _renderer.ReportScrollRow = 0;
                    _renderer.ForceFullRepaint();
                }
            }
        }
    }
}
