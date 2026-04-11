using System;
using System.Linq;
using Spectre.Console;
using ETL_SQL.Core.Parser;

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

        public void Render(IConsoleInterface console, int x, int y, int width, int height)
        {
            var aliases = ETLSuggestEngine.ParseAliases(_buffer.GetText());
            int gutterWidth = (_buffer.Lines.Count).ToString().Length + 2;
            int editorWidth = width - gutterWidth - 1;

            for (int i = 0; i < height; i++)
            {
                int row = y + i;
                int lineIdx = i + _renderer.ScrollLine;
                
                console.SetCursorPosition(x, row);
                console.Write(new string(' ', width));

                if (lineIdx < _buffer.Lines.Count)
                {
                    var line = _buffer.Lines[lineIdx];
                    string visibleLine = line.Length > _renderer.ScrollCol 
                        ? line.Substring(_renderer.ScrollCol, Math.Min(line.Length - _renderer.ScrollCol, editorWidth)) 
                        : "";
                    
                    string highlighted = RenderLineWithMarkers(lineIdx, visibleLine, editorWidth, aliases);

                    console.SetCursorPosition(x, row);
                    console.Markup($"[grey]{(lineIdx + 1).ToString().PadLeft(gutterWidth - 1)} [/]");
                    console.Markup(highlighted);
                }
            }
        }

        private string RenderLineWithMarkers(int lineIdx, string visibleLine, int editorWidth, System.Collections.Generic.IDictionary<string, AliasInfo> aliases)
        {
            var secondary = _buffer.SecondaryCursors.FirstOrDefault(c => c.Line == lineIdx);
            bool hasSecondary = _buffer.IsMultiLineMode && _buffer.SecondaryCursors.Any(c => c.Line == lineIdx);

            if (!_buffer.SelectionStartLine.HasValue)
            {
                if (!hasSecondary) return ETLSuggestEngine.HighlightLine(visibleLine, aliases);
                int relCol = secondary.Col - _renderer.ScrollCol;
                if (relCol < 0 || relCol >= visibleLine.Length) return ETLSuggestEngine.HighlightLine(visibleLine, aliases);

                string b = visibleLine.Substring(0, relCol);
                string c = visibleLine[relCol].ToString();
                if (string.IsNullOrWhiteSpace(c)) c = " ";
                string a = visibleLine.Substring(relCol + 1);

                return ETLSuggestEngine.HighlightLine(b, aliases) + "[reverse]" + Markup.Escape(c) + "[/]" + ETLSuggestEngine.HighlightLine(a, aliases);
            }

            var (startL, startC, endL, endC) = _buffer.GetSelectionBounds();
            if (lineIdx < startL || lineIdx > endL) return ETLSuggestEngine.HighlightLine(visibleLine, aliases);

            int lineStart = (lineIdx == startL) ? startC : 0;
            int lineEnd = (lineIdx == endL) ? endC : _buffer.Lines[lineIdx].Length;

            int relStart = Math.Max(0, lineStart - _renderer.ScrollCol);
            int relEnd = Math.Min(visibleLine.Length, lineEnd - _renderer.ScrollCol);

            if (relEnd < 0 || relStart >= visibleLine.Length) return ETLSuggestEngine.HighlightLine(visibleLine, aliases);

            string beforeS = visibleLine.Substring(0, relStart);
            string selectedS = visibleLine.Substring(relStart, relEnd - relStart);
            string afterS = visibleLine.Substring(relEnd);

            return ETLSuggestEngine.HighlightLine(beforeS, aliases) + "[black on white]" + Markup.Escape(selectedS) + "[/]" + ETLSuggestEngine.HighlightLine(afterS, aliases);
        }
    }
}
