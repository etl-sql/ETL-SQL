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
            if (key.Key == ConsoleKey.O && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await ShowOpenPrompt(); return; }
            if (key.Key == ConsoleKey.N && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.NewFile(); return; }
            if (key.Key == ConsoleKey.R && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor._evaluator.ClearResults(); _renderer.ShowStatus("Results cleared."); return; }
            if (key.Key == ConsoleKey.F && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.Find(); return; }
            if (key.Key == ConsoleKey.F && key.Modifiers.HasFlag(ConsoleModifiers.Alt)) { _editor.FormatScript(); return; }
            if (key.Key == ConsoleKey.I && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.FormatScript(); return; }
            if (key.Key == ConsoleKey.H && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.Replace(); return; }
            if (key.Key == ConsoleKey.G && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.GoToLine(); return; }
            if (key.Key == ConsoleKey.P && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { await _editor.ExportResults(); return; }
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.Copy(); return; }
            if ((key.Key == ConsoleKey.V || key.Key == ConsoleKey.U) && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.Paste(); return; }
            if (key.Key == ConsoleKey.X && key.Modifiers.HasFlag(ConsoleModifiers.Control)) 
            {
                if (_buffer.SelectionStartLine.HasValue) _editor.Cut();
                else await _editor.HandleExit(); // Nano style Exit if no selection
                return; 
            }
            if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.DuplicateLine(); return; }
            if (key.Key == ConsoleKey.K && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.DeleteLine(); return; }
            if (key.Key == ConsoleKey.Home && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.GoToTop(); return; }
            if (key.Key == ConsoleKey.End && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { _editor.GoToBottom(); return; }

            // ── Global Bottom Panel Scrolling ──
            if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                if (key.Key == ConsoleKey.UpArrow) { _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow - 1); return; }
                if (key.Key == ConsoleKey.DownArrow) { _renderer.ResultScrollRow++; return; }
                if (key.Key == ConsoleKey.PageUp) { _renderer.ResultScrollRow = Math.Max(0, _renderer.ResultScrollRow - 10); return; }
                if (key.Key == ConsoleKey.PageDown) { _renderer.ResultScrollRow += 10; return; }
            }

            // F3 - Focus Toggle
            if (key.Key == ConsoleKey.F3)
            {
                _renderer.ResultsFocus = !_renderer.ResultsFocus;
                _renderer.AutocompleteVisible = false;
                _renderer.ForceFullRepaint();
                _renderer.ShowStatus(_renderer.ResultsFocus ? "Focused: Results (Use arrows/PgUp/PgDn to scroll)" : "Focused: Editor");
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

            // F4 - View Toggle (Results -> Perf -> Tree)
            if (key.Key == ConsoleKey.F4)
            {
                if (!_renderer.PerformanceVisible && !_renderer.TreeVisible) { _renderer.PerformanceVisible = true; _renderer.ShowStatus("View: Performance Metrics"); }
                else if (_renderer.PerformanceVisible) { _renderer.PerformanceVisible = false; _renderer.TreeVisible = true; _renderer.ShowStatus("View: Execution Tree"); }
                else { _renderer.TreeVisible = false; _renderer.ShowStatus("View: Query Results"); }
                _renderer.ForceFullRepaint();
                return;
            }

            // F6 - Tree Toggle
            if (key.Key == ConsoleKey.F6)
            {
                _renderer.TreeVisible = !_renderer.TreeVisible;
                _renderer.PerformanceVisible = false;
                _renderer.ForceFullRepaint();
                _renderer.ShowStatus(_renderer.TreeVisible ? "View: Execution Tree" : "View: Query Results");
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
                    else { _renderer.ActiveResultSetIndex = Math.Max(0, _renderer.ActiveResultSetIndex - 1); _renderer.ResultScrollRow = 0; _renderer.ResultScrollCol = 0; }
                    break;
                case ConsoleKey.RightArrow: 
                    if (key.Modifiers.HasFlag(ConsoleModifiers.Control)) _renderer.ResultScrollCol++;
                    else { _renderer.ActiveResultSetIndex = Math.Min(_editor._evaluator.LastResultSets.Count - 1, _renderer.ActiveResultSetIndex + 1); _renderer.ResultScrollRow = 0; _renderer.ResultScrollCol = 0; }
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
                    _renderer.PromptSuggestions = ETLSuggestEngine.GetFileSuggestions(_renderer.PromptValue);
                    _renderer.PromptSuggestionIndex = 0;
                }
                else
                {
                    _renderer.PromptSuggestionIndex = (_renderer.PromptSuggestionIndex + 1) % _renderer.PromptSuggestions.Count;
                }

                if (_renderer.PromptSuggestions.Any())
                {
                    _renderer.PromptValue = _renderer.PromptSuggestions[_renderer.PromptSuggestionIndex];
                    _renderer.PromptCursor = _renderer.PromptValue.Length;
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
