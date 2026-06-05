using Xunit;
using ETL_SQL.TUI.UI;
using Spectre.Console;

namespace ETL_SQL.Tests.UI
{
    public class TuiThemeTests
    {
        [Fact]
        public void DefaultTheme_LoadWithoutFile_UsesDefaultTheme()
        {
            TuiTheme.Load("non-existent-theme-file-path.json");
            Assert.NotNull(TuiTheme.Instance);
            Assert.Equal("grey", TuiTheme.Instance.Editor.Gutter);
            Assert.Equal("darkorange3", TuiTheme.Instance.Syntax.String);
            Assert.Equal("yellow", TuiTheme.Instance.Ui.EditorFocusedBorder);
        }

        [Fact]
        public void GetColor_ValidString_ParsesSuccessfully()
        {
            var theme = new TuiTheme();
            var parsedColor = theme.GetColor("red", Color.Blue);
            Assert.Equal(Color.Red, parsedColor);
        }

        [Fact]
        public void GetColor_InvalidString_ReturnsFallback()
        {
            var theme = new TuiTheme();
            var parsedColor = theme.GetColor("invalid-color-name", Color.Blue);
            Assert.Equal(Color.Blue, parsedColor);
        }

        [Fact]
        public void GetStyle_ValidString_ParsesSuccessfully()
        {
            var theme = new TuiTheme();
            var parsedStyle = theme.GetStyle("bold green", new Style(Color.Blue));
            Assert.Equal(Color.Green, parsedStyle.Foreground);
            Assert.True(parsedStyle.Decoration.HasFlag(Decoration.Bold));
        }

        [Fact]
        public void GetStyle_InvalidString_ReturnsFallback()
        {
            var theme = new TuiTheme();
            var parsedStyle = theme.GetStyle("invalid-style-string", new Style(Color.Blue));
            Assert.Equal(Color.Blue, parsedStyle.Foreground);
        }
    }
}
