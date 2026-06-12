using ETL_SQL.TUI.UI;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// Prompt captions and typed values may contain '[' or ']' (file paths, search
    /// terms, the exit prompt). These must be escaped so Spectre does not parse them as
    /// markup and crash the editor (regression: "Could not find color or style 'D'").
    /// </summary>
    public class PromptMarkupTests
    {
        [Theory]
        [InlineData("Save all / Discard / Cancel? (s/d/c)", "")]
        [InlineData("[S]ave all  [D]iscard all  [C]ancel", "")]
        [InlineData("Open", "C:\\path\\[weird]\\file.etlsql")]
        [InlineData("Find", "value with ] bracket")]
        [InlineData(null, "[unterminated")]
        public void BuildPromptMarkup_ProducesParseableMarkup(string? title, string value)
        {
            string markup = EditorRenderer.BuildPromptMarkup(title, value);

            // Wrapped exactly as the renderer wraps it; must not throw.
            var ex = Record.Exception(() => new Markup($"[white on black]{markup}[/]"));
            Assert.Null(ex);
        }
    }
}
