using System;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// What the host terminal can do, so rendering can degrade gracefully: honor NO_COLOR, fall
    /// back to ASCII glyphs on non-Unicode/dumb terminals, and pick a usable minimum size.
    /// </summary>
    public sealed class TerminalCapabilities
    {
        /// <summary>The smallest editor that renders without overlapping panels.</summary>
        public const int MinWidth = 40;
        public const int MinHeight = 10;

        public bool Color { get; }
        public bool Unicode { get; }

        private TerminalCapabilities(bool color, bool unicode)
        {
            Color = color;
            Unicode = unicode;
        }

        /// <summary>Process-wide capabilities, detected once from the environment.</summary>
        public static TerminalCapabilities Current { get; set; } = Detect(Environment.GetEnvironmentVariable);

        /// <summary>
        /// Detects capabilities from environment variables (injectable for tests):
        /// NO_COLOR disables colour (https://no-color.org); ETLSQL_TUI_ASCII forces ASCII glyphs;
        /// TERM=dumb disables both.
        /// </summary>
        public static TerminalCapabilities Detect(Func<string, string?> env)
        {
            bool dumb = string.Equals(env("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);
            bool noColor = !string.IsNullOrEmpty(env("NO_COLOR"));
            bool forceAscii = !string.IsNullOrEmpty(env("ETLSQL_TUI_ASCII"));

            return new TerminalCapabilities(color: !noColor && !dumb, unicode: !forceAscii && !dumb);
        }

        /// <summary>Returns <paramref name="unicode"/> when the terminal supports it, else <paramref name="ascii"/>.</summary>
        public string Glyph(string unicode, string ascii) => Unicode ? unicode : ascii;

        /// <summary>The panel border to use — rounded box-drawing on Unicode terminals, ASCII elsewhere.</summary>
        public Spectre.Console.BoxBorder Box() =>
            Unicode ? Spectre.Console.BoxBorder.Rounded : Spectre.Console.BoxBorder.Ascii;

        /// <summary>The table border to use — rounded box-drawing on Unicode terminals, ASCII elsewhere.</summary>
        public Spectre.Console.TableBorder Table() =>
            Unicode ? Spectre.Console.TableBorder.Rounded : Spectre.Console.TableBorder.Ascii;
    }
}
