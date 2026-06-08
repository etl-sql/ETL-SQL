using Xunit;
using System.Collections.Generic;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Reduced-capability terminal detection (NO_COLOR / dumb / forced ASCII).</summary>
    public class TerminalCapabilitiesTests
    {
        private static System.Func<string, string?> Env(params (string k, string v)[] vars)
        {
            var map = new Dictionary<string, string>();
            foreach (var (k, v) in vars) map[k] = v;
            return name => map.TryGetValue(name, out var v) ? v : null;
        }

        [Fact]
        public void Default_Environment_IsFullColorUnicode()
        {
            var c = TerminalCapabilities.Detect(Env());
            Assert.True(c.Color);
            Assert.True(c.Unicode);
        }

        [Fact]
        public void NoColor_DisablesColourOnly()
        {
            var c = TerminalCapabilities.Detect(Env(("NO_COLOR", "1")));
            Assert.False(c.Color);
            Assert.True(c.Unicode);
        }

        [Fact]
        public void ForcedAscii_DisablesUnicodeOnly()
        {
            var c = TerminalCapabilities.Detect(Env(("ETLSQL_TUI_ASCII", "1")));
            Assert.True(c.Color);
            Assert.False(c.Unicode);
        }

        [Fact]
        public void DumbTerminal_DisablesBoth()
        {
            var c = TerminalCapabilities.Detect(Env(("TERM", "dumb")));
            Assert.False(c.Color);
            Assert.False(c.Unicode);
        }

        [Fact]
        public void Glyph_PicksByUnicodeSupport()
        {
            Assert.Equal("▼", TerminalCapabilities.Detect(Env()).Glyph("▼", "-"));
            Assert.Equal("-", TerminalCapabilities.Detect(Env(("ETLSQL_TUI_ASCII", "1"))).Glyph("▼", "-"));
        }

        [Fact]
        public void MinimumSize_IsPositive()
        {
            Assert.True(TerminalCapabilities.MinWidth > 0 && TerminalCapabilities.MinHeight > 0);
        }
    }
}
