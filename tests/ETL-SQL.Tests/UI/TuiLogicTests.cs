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
