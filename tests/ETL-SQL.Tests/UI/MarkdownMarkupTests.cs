using Xunit;
using Spectre.Console;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>
    /// The info overlay renders lightweight markdown as Spectre markup. Whatever it emits
    /// must be parseable (brackets in content escaped) and apply the expected styles.
    /// </summary>
    public class MarkdownMarkupTests
    {
        [Theory]
        [InlineData("# Connections")]
        [InlineData("## Syntax")]
        [InlineData("**Relational databases**")]
        [InlineData("- `MSSQL` / `SQLSERVER`: Microsoft SQL Server")]
        [InlineData("```sql")]
        [InlineData("plain text with [brackets] and ] stray")]
        public void MarkdownToMarkup_AlwaysParseable(string line)
        {
            string markup = EditorRenderer.MarkdownToMarkup(line);
            var ex = Record.Exception(() => new Markup(markup));
            Assert.Null(ex);
        }

        [Fact]
        public void MarkdownToMarkup_AppliesStyles()
        {
            Assert.Contains("[bold yellow]", EditorRenderer.MarkdownToMarkup("## Syntax"));
            Assert.Contains("[bold]", EditorRenderer.MarkdownToMarkup("**bold**"));
            Assert.Contains("[cyan]", EditorRenderer.MarkdownToMarkup("use `CODE` here"));
            Assert.Equal("[grey]────────[/]", EditorRenderer.MarkdownToMarkup("```sql"));
        }

        [Fact]
        public void MarkdownToMarkup_MakesBareUrlsClickable()
        {
            string markup = EditorRenderer.MarkdownToMarkup("http://localhost:5050/");
            Assert.Contains("[link=http://localhost:5050/]", markup);
            var ex = Record.Exception(() => new Markup(markup));
            Assert.Null(ex);
        }
    }
}
