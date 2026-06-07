using Xunit;
using System.Linq;
using ETL_SQL.TUI.UI;

namespace ETL_SQL.Tests.UI
{
    /// <summary>Command palette filtering: substring/subsequence matching and ordering.</summary>
    public class CommandPaletteTests
    {
        [Fact]
        public void Filter_EmptyQuery_ReturnsAll()
        {
            Assert.Equal(CommandPalette.Commands.Count, CommandPalette.Filter("").Count);
        }

        [Fact]
        public void Filter_Substring_RanksAndIncludesMatches()
        {
            var results = CommandPalette.Filter("export");
            Assert.Contains(results, c => c.Title == "Export report to Markdown");
            Assert.Contains(results, c => c.Title == "Export report to PDF");
            Assert.DoesNotContain(results, c => c.Title == "Help");
        }

        [Fact]
        public void Filter_Subsequence_Matches()
        {
            // "srvbr" is a subsequence of "Serve report in browser".
            var results = CommandPalette.Filter("srvbr");
            Assert.Contains(results, c => c.Title == "Serve report in browser");
        }

        [Fact]
        public void Filter_NoMatch_ReturnsEmpty()
        {
            Assert.Empty(CommandPalette.Filter("zzzqqq"));
        }

        [Fact]
        public void Filter_PrefersEarlierSubstring()
        {
            // "Help" should rank ahead of "Help at cursor" etc. for query "help".
            var results = CommandPalette.Filter("help");
            Assert.Equal("Help", results.First().Title);
        }

        [Fact]
        public void Includes_ReportActions()
        {
            Assert.Contains(CommandPalette.Commands, c => c.Title == "Serve folder (all reports)");
            Assert.Contains(CommandPalette.Commands, c => c.Title == "Serve report in browser");
            Assert.Contains(CommandPalette.Commands, c => c.Title == "Publish to Portal");
            Assert.Contains(CommandPalette.Commands, c => c.Title == "Reset portal connection");
        }

        [Fact]
        public void EveryCommand_HasTitleAndAction()
        {
            Assert.All(CommandPalette.Commands, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Title));
                Assert.NotNull(c.Run);
            });
        }
    }
}
