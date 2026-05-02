using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core;
using ETL_SQL.Engine;
using ETL_SQL.TUI.UI;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Integration.UI
{
    public class TuiLogicTests
    {
        [Fact]
        public async Task KeywordProvider_DoesNotAppendTrailingSpace()
        {
            var engine = new SuggestionEngine();
            var ctx = new SuggestionContext { Prefix = "SEL" };

            var results = (await engine.GetSuggestionsAsync(ctx)).ToList();
            var selectSuggestion = results.FirstOrDefault(s => s.Text.Trim() == "SELECT");

            Assert.NotNull(selectSuggestion);
            Assert.False(selectSuggestion.Text.EndsWith(" "), "Keyword suggestion must NOT end with a space to prevent double-spacing bugs.");
        }
    }
}
