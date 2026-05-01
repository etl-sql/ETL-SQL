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
            if (key.Key == ConsoleKey.Spacebar && key.Modifiers == 0)
            {
                _editor.MarkDirty();
                _buffer.InsertChar(' ');
                _renderer.AutocompleteVisible = false;
                return;
            }

            if (_renderer.AutocompleteVisible)
            {
                if (_autocomplete.HandleKey(key)) return;
            }

            if (key.Key == ConsoleKey.Escape && _buffer.IsMultiLineMode)
            {
                _buffer.ClearMultiCursors();
                _renderer.ShowStatus("Multi-line mode disabled.");
                return;
            }

            // F1 - Help
            if (key.Key == ConsoleKey.F1)
            {
                _editor.ShowHelp();
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
                else await _editor.RunScript();
                return;
            }

            // Shortcuts
            if (key.Key == ConsoleKey.Q && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.HandleExit(); return; }
            if (key.Key == ConsoleKey.A && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _buffer.SelectAll(); return; }
            if (key.Key == ConsoleKey.Z && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.Undo(); return; }
            if (key.Key == ConsoleKey.Y && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.Redo(); return; }
            if (key.Key == ConsoleKey.S && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.SaveScript(key.Modifiers.HasFlag(ConsoleModifiers.Shift)); return; }
            if (key.Key == ConsoleKey.F2) { await _editor.SaveScript(false); return; }
            if (key.Key == ConsoleKey.O && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await ShowOpenPrompt(); return; }
            if (key.Key == ConsoleKey.N && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.NewFile(); return; }
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
            if (key.Key == ConsoleKey.P && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.ExportResults(); return; }
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
                if (key.Key == ConsoleKey.UpArrow) { _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow - 1); return; }
                if (key.Key == ConsoleKey.DownArrow) { _renderer.ResultScrollRow++; return; }
                if (key.Key == ConsoleKey.PageUp) { _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow - 10); return; }
                if (key.Key == ConsoleKey.PageDown) { _renderer.ResultScrollRow += 10; return; }

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


            // F6/F3 - Focus Toggle
            if (key.Key == ConsoleKey.F6 || key.Key == ConsoleKey.F3)
            {
                _renderer.ResultsFocus = !_renderer.ResultsFocus;
                _renderer.AutocompleteVisible = false;
                _renderer.ForceFullRepaint();
                _renderer.ShowStatus(_renderer.ResultsFocus ? "Focus: Results (↑↓ ⇞ ⇟)" : "Focus: Editor");
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
                        _renderer.CompareFilters    = _editor._evaluator.LastResultSets.Select(_ => "").ToList();
                        _renderer.ResultsVisible    = false;
                        _renderer.PerformanceVisible = false;
                        _renderer.IsBottomMaximized = true;
                        _renderer.ForceFullRepaint();
                        _renderer.ShowStatus($"Compare mode — {_editor._evaluator.LastResultSets.Count} sets. F7: next pane  Ctrl+F: filter  Escape: exit");
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

            // F4 - Cycle lower panel: Pipeline+Messages → Results → Performance → (repeat)
            if (key.Key == ConsoleKey.F4)
            {
                if (!_renderer.ResultsVisible && !_renderer.PerformanceVisible)
                    { _renderer.ResultsVisible = true; _renderer.ShowStatus("View: Query Results"); }
                else if (_renderer.ResultsVisible)
                    { _renderer.ResultsVisible = false; _renderer.PerformanceVisible = true; _renderer.ShowStatus("View: Performance Metrics"); }
                else
                    { _renderer.PerformanceVisible = false; _renderer.ShowStatus("View: Pipeline & Messages"); }
                _renderer.ForceFullRepaint();
                return;
            }

            if (_renderer.CompareMode)
            {
                HandleCompareKey(key);
                return;
            }

            if (_renderer.ResultsFocus)
            {
                HandleResultsKey(key);
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

        private void HandleCompareKey(ConsoleKeyInfo key)
        {
            int idx = _renderer.CompareFocusIndex;
            while (_renderer.CompareScrollRows.Count <= idx) _renderer.CompareScrollRows.Add(0);
            while (_renderer.CompareFilters.Count    <= idx) _renderer.CompareFilters.Add("");

            switch (key.Key)
            {
                case ConsoleKey.Escape:
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
                case ConsoleKey.PageUp:
                    _renderer.CompareScrollRows[idx] = Math.Max(0, _renderer.CompareScrollRows[idx] - 10); break;
                case ConsoleKey.PageDown:
                    _renderer.CompareScrollRows[idx] += 10; break;
                case ConsoleKey.Home:
                    _renderer.CompareScrollRows[idx] = 0; break;
            }
        }

        private void HandleResultsKey(ConsoleKeyInfo key)
        {
            switch (key.Key)
            {
                case ConsoleKey.Escape:
                    if (!string.IsNullOrEmpty(_renderer.FilterText))
                    {
                        _renderer.FilterText = "";
                        _renderer.ResultScrollRow = 0;
                        _renderer.ShowStatus("Filter cleared.");
                    }
                    else
                    {
                        _renderer.ResultsFocus = false;
                        _renderer.ShowStatus("Focused: Editor");
                    }
                    break;
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
                        _renderer.ResultScrollRow = _editor._evaluator.LastResultSets[_renderer.ActiveResultSetIndex].Rows.Count; 
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

        private bool IsNavigationKey(ConsoleKeyInfo key)
        {
            return key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow || 
                   key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow || 
                   key.Key == ConsoleKey.Home || key.Key == ConsoleKey.End || 
                   key.Key == ConsoleKey.Backspace || key.Key == ConsoleKey.Delete;
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
    }
}
