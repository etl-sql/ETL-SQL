using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.TUI.UI;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests.UI
{
    public class TuiLogicTests
    {
        [Theory]
        [InlineData("m.", 2, "m.")]
        [InlineData("m.v", 3, "m.v")]
        [InlineData("u.*", 3, "u.*")]
        [InlineData("SELECT u.*", 10, "u.*")]
        [InlineData("SELECT u.* FROM", 10, "u.*")]
        public void TerminalIdeWindow_GetWordPrefix_MatchesCorrectTokens(string line, int col, string expected)
        {
            // Act
            var prefix = TerminalIdeWindow.GetWordPrefix(line, col);

            // Assert
            Assert.Equal(expected, prefix);
        }

        [Fact]
        public async Task KeywordProvider_DoesNotAppendTrailingSpace()
        {
            // The autocomplete menu handles spacing natively. Appending a trailing space 
            // inside the Suggestion object creates "double spaces" (over compensation).
            var provider = new KeywordProvider();
            var ctx = new SuggestionContext { Prefix = "SEL" };  // prefix required to trigger keyword matching

            var results = (await provider.GetSuggestionsAsync(ctx)).ToList();
            var selectSuggestion = results.FirstOrDefault(s => s.Text.Trim() == "SELECT");

            Assert.NotNull(selectSuggestion);
            Assert.False(selectSuggestion.Text.EndsWith(" "), "Keyword suggestion must NOT end with a space to prevent double-spacing bugs.");
        }

    }
}
