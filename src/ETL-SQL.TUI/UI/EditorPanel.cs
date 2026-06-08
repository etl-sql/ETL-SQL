using System;
using System.Linq;
using Spectre.Console;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.TUI.UI
{
    public class EditorPanel : IUIComponent
    {
        private readonly EditorBuffer _buffer;
        private readonly EditorRenderer _renderer;

        public EditorPanel(EditorBuffer buffer, EditorRenderer renderer)
        {
            _buffer = buffer;
            _renderer = renderer;
        }

        public void Render(IConsoleInterface console, int x, int y, int width, int height, int scrollRow = 0)
        {
            int gutterWidth = (_buffer.Lines.Count).ToString().Length + 2;
            int editorWidth = width - gutterWidth - 1;

            // Pre-calculate multiline comment state for the start of the viewport
            bool inMultiline = false;
            for (int i = 0; i < _renderer.ScrollLine; i++)
            {
                string l = _buffer.Lines[i];
                int pos = 0;
                while (pos < l.Length)
                {
                    int next = l.IndexOf(inMultiline ? "*/" : "/*", pos);
                    if (next < 0) break;
                    inMultiline = !inMultiline;
                    pos = next + 2;
                }
            }

            for (int i = 0; i < height; i++)
            {
                int row = y + i;
                int lineIdx = i + _renderer.ScrollLine;
                
                console.ClearLine(x, row, width);

                if (lineIdx < _buffer.Lines.Count)
                {
                    var fullLine = _buffer.Lines[lineIdx];
                    string highlighted = RenderLineWithSelection(lineIdx, fullLine, editorWidth, ref inMultiline);
                    
                    _renderer.SetLinePhysicalShift(lineIdx, 0);

                    console.SetCursorPosition(x, row);
                    // Gutter: right-aligned line number, then a marker glyph (or a space) in the
                    // trailing cell so a diagnostic line shows ✗/⚠/• without shifting the text.
                    string num = (lineIdx + 1).ToString().PadLeft(gutterWidth - 1);
                    if (_renderer.DiagnosticLines.TryGetValue(lineIdx + 1, out var level))
                        console.Markup($"[{TuiTheme.Instance.Editor.Gutter}]{num}[/][{DiagnosticGutter.Color(level)}]{DiagnosticGutter.Glyph(level)}[/]");
                    else
                        console.Markup($"[{TuiTheme.Instance.Editor.Gutter}]{num} [/]");
                    console.Markup(highlighted);
                }
            }
        }

        /// <summary>
        /// Markup for the visible window of a line with every occurrence of the active find term
        /// wrapped in a highlight style; null when there's no active term or no match in view.
        /// </summary>
        private string? RenderFindHighlight(string fullLine, int editorWidth)
        {
            var term = _renderer.FindTerm;
            if (string.IsNullOrEmpty(term)) return null;

            string visible = fullLine.Length > _renderer.ScrollCol
                ? fullLine.Substring(_renderer.ScrollCol, Math.Min(fullLine.Length - _renderer.ScrollCol, editorWidth))
                : "";
            if (visible.Length == 0) return null;

            int idx = visible.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var sb = new System.Text.StringBuilder();
            int pos = 0;
            while (idx >= 0)
            {
                sb.Append(Markup.Escape(visible.Substring(pos, idx - pos)));
                sb.Append("[black on yellow]").Append(Markup.Escape(visible.Substring(idx, term.Length))).Append("[/]");
                pos = idx + term.Length;
                idx = visible.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase);
            }
            sb.Append(Markup.Escape(visible.Substring(pos)));
            return sb.ToString();
        }

        private string RenderLineWithSelection(int lineIdx, string fullLine, int editorWidth, ref bool inMultiline)
        {
            bool startsInComment = inMultiline;
            // HighlightLine handles clipping and updates inMultiline (via out endsInMultiline)
            string highlighted = ETLSuggestEngine.HighlightLine(fullLine, _renderer.ScrollCol, editorWidth, startsInComment, out inMultiline);

            // Find-match highlight takes precedence over syntax colour on lines that contain a hit
            // (no active selection). Mirrors the plain-text fallback used for selection below.
            if (!_buffer.SelectionStartLine.HasValue)
            {
                var findHl = RenderFindHighlight(fullLine, editorWidth);
                if (findHl != null) return findHl;
            }

            if (!_buffer.SelectionStartLine.HasValue && !_buffer.IsMultiLineMode) return highlighted;

            // Handle selection/cursors (this part still needs to work with the highlighted result)
            // For simplicity in this TUI, we'll re-run highlighting if there's a selection to avoid complex markup splicing.
            // But we must ensure the 'inMultiline' state is preserved correctly.
            
            var secondary = _buffer.SecondaryCursors.FirstOrDefault(c => c.Line == lineIdx);
            bool hasSecondary = _buffer.IsMultiLineMode && _buffer.SecondaryCursors.Any(c => c.Line == lineIdx);

            if (!_buffer.SelectionStartLine.HasValue)
            {
                if (!hasSecondary) return highlighted;
                
                int relCol = secondary.Col - _renderer.ScrollCol;
                if (relCol < 0 || relCol >= editorWidth || secondary.Col >= fullLine.Length) return highlighted;

                // Naive cursor injection: we re-highlight segments around the cursor
                string b = fullLine.Substring(0, secondary.Col);
                string c = fullLine[secondary.Col].ToString();
                if (string.IsNullOrWhiteSpace(c)) c = " ";
                string a = fullLine.Substring(secondary.Col + 1);

                bool dummy;
                string hb = ETLSuggestEngine.HighlightLine(b, _renderer.ScrollCol, editorWidth, startsInComment, out dummy);
                string hc = $"[{TuiTheme.Instance.Editor.SecondaryCursor}]" + Markup.Escape(c) + "[/]";
                string ha = ETLSuggestEngine.HighlightLine(a, Math.Max(0, _renderer.ScrollCol - secondary.Col - 1), editorWidth, dummy, out _);

                return hb + hc + ha;
            }

            var (startL, startC, endL, endC) = _buffer.GetSelectionBounds();
            if (lineIdx < startL || lineIdx > endL) return highlighted;

            int lineStart = (lineIdx == startL) ? startC : 0;
            int lineEnd = (lineIdx == endL) ? endC : fullLine.Length;

            int relStart = Math.Max(0, lineStart - _renderer.ScrollCol);
            int relEnd = Math.Min(editorWidth, lineEnd - _renderer.ScrollCol);

            // If selection is off-screen
            if (lineEnd <= _renderer.ScrollCol || lineStart >= _renderer.ScrollCol + editorWidth) return highlighted;

            // Complex selection rendering over highlighted text is difficult without a full token model.
            // For now, we'll fall back to plain selection if it's active on this line to ensure it's visible.
            string visibleLine = fullLine.Length > _renderer.ScrollCol 
                ? fullLine.Substring(_renderer.ScrollCol, Math.Min(fullLine.Length - _renderer.ScrollCol, editorWidth)) 
                : "";

            int vRelStart = Math.Max(0, lineStart - _renderer.ScrollCol);
            int vRelEnd = Math.Min(visibleLine.Length, lineEnd - _renderer.ScrollCol);

            string vBeforeS = visibleLine.Substring(0, vRelStart);
            string vSelectedS = visibleLine.Substring(vRelStart, vRelEnd - vRelStart);
            string vAfterS = visibleLine.Substring(vRelEnd);

            bool d1, d2;
            return ETLSuggestEngine.HighlightLine(fullLine.Substring(0, lineStart), _renderer.ScrollCol, editorWidth, startsInComment, out d1)
                 + $"[{TuiTheme.Instance.Editor.Selection}]" + Markup.Escape(vSelectedS) + "[/]"
                 + ETLSuggestEngine.HighlightLine(fullLine.Substring(lineEnd), 0, editorWidth, d1, out d2);
        }
    }
}
