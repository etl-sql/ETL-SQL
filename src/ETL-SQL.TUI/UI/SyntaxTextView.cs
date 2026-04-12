using System;
using Terminal.Gui;
using NStack;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// A Terminal.Gui TextView with ETL-SQL syntax highlighting.
    /// After the base Redraw paints the text in default colors, we walk each
    /// visible line through <see cref="EtlSqlHighlighter"/> and re-apply
    /// color attributes token by token.
    /// </summary>
    public class SyntaxTextView : TextView
    {
        private readonly EtlSqlHighlighter _highlighter = new();

        public override void Redraw(Rect bounds)
        {
            base.Redraw(bounds);

            if (Application.Driver == null) return;

            var content = Text?.ToString() ?? "";
            if (string.IsNullOrEmpty(content)) return;

            var lines = content.Split('\n');
            // Use the view's current background to prevent visual leakage.
            var bg = ColorScheme?.Normal.Background ?? Color.DarkGray; 
            var fgBase = ColorScheme?.Normal.Foreground ?? Color.White;
            var normalAttr = new Terminal.Gui.Attribute(fgBase, bg);
            
            // Set the theme for the whole view to ensure consistency
            if (ColorScheme == null) ColorScheme = new ColorScheme { Normal = normalAttr, Focus = new Terminal.Gui.Attribute(Color.White, Color.Cyan) };

            for (int lineIdx = TopRow; lineIdx < lines.Length; lineIdx++)
            {
                int screenY = lineIdx - TopRow;
                if (screenY >= bounds.Height) break;

                var line = lines[lineIdx];
                var tokens = _highlighter.Tokenize(line);

                foreach (var token in tokens)
                {
                    var fg = TokenColor(token.Color);
                    // Use the view-wide background color to prevent background leakage
                    Application.Driver.SetAttribute(new Terminal.Gui.Attribute(fg, bg));

                    for (int i = 0; i < token.Length; i++)
                    {
                        int charIdx = token.Start + i;
                        if (charIdx >= line.Length) break;

                        int screenX = charIdx - LeftColumn;
                        if (screenX < 0 || screenX >= bounds.Width) continue;

                        AddRune(screenX, screenY, line[charIdx]);
                    }
                }
            }

            // Restore normal attribute so cursor rendering isn't affected
            Application.Driver.SetAttribute(normalAttr);
        }

        private static Color TokenColor(HighlightColor c) => c switch
        {
            // DML keywords (SELECT, FROM, WHERE…) — muted blue, readable
            HighlightColor.Keyword     => Color.Cyan,
            // DDL (CREATE, DROP, ALTER) — slightly warmer to stand out
            HighlightColor.DdlKeyword  => Color.Magenta,
            // Control flow (IF, WHILE, RETURN) — warm tone
            HighlightColor.ControlFlow => Color.BrightYellow,
            // String literals — green, classic terminal style
            HighlightColor.String      => Color.Green,
            // Comments — dark, unobtrusive
            HighlightColor.Comment     => Color.Gray,
            // @variables — bright green so they pop
            HighlightColor.Variable    => Color.BrightGreen,
            // [Bracketed identifiers] — same as plain text, just brackets stand out via context
            HighlightColor.Bracket     => Color.Cyan,
            // Built-in functions — brown/olive (the non-bright yellow in 16-color)
            HighlightColor.Function    => Color.Brown,
            // Data types (INT, VARCHAR…) — same muted blue as keywords
            HighlightColor.DataType    => Color.Cyan,
            _                          => Color.White
        };
        public override bool ProcessKey(KeyEvent kb)
        {
            // Allow Tab to accept suggestions if the autocomplete is visible
            if (Autocomplete != null && Autocomplete.Visible)
            {
                if (kb.Key == Key.Tab)
                {
                    // Map Tab to the configured selection key (Enter) 
                    // so Autocomplete handles it and inserts the text.
                    kb.Key = Autocomplete.SelectionKey;
                }
            }
            return base.ProcessKey(kb);
        }
    }
}
