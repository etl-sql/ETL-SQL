using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Services;
using ETL_SQL.Common;
using ETL_SQL.Core.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Completions through <see cref="GrammarLanguageService"/> — the service the language server,
    /// Portal, TUI, and workstation editor actually register. It narrows the base service's
    /// suggestions to what the grammar state tree offers at the cursor, so a clause the tree does
    /// not model gets filtered out of every real editor even when the base service offers it.
    /// </summary>
    public class ExpectClauseGrammarCompletionTests
    {
        [Fact]
        public async Task Expect_IsOfferedAfterASelectColumn()
        {
            var suggestions = await Suggest("SELECT Id ");

            Assert.Contains(suggestions, s => s.Text.Equals("EXPECT", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task RuleStarters_SurviveTheGrammarNarrowing()
        {
            var suggestions = await Suggest("SELECT Id EXPECT ");

            Assert.Contains(suggestions, s => s.Text == "NOT NULL");
            Assert.Contains(suggestions, s => s.Text == "UNIQUE");
        }

        [Fact]
        public async Task ColumnActions_SurviveTheGrammarNarrowing()
        {
            var suggestions = await Suggest("SELECT Id EXPECT NOT NULL ON FAILURE ");

            Assert.Contains(suggestions, s => s.Text == "QUARANTINE");
            Assert.Contains(suggestions, s => s.Text == "THROW");
        }

        private static async Task<System.Collections.Generic.List<Suggestion>> Suggest(string scriptBefore)
        {
            var metadata = new Mock<IMetadataManager>();
            var service = new GrammarLanguageService(metadata.Object);
            return await service.GetSuggestionsAsync(new SuggestionContext
            {
                Prefix = "",
                ScriptBefore = scriptBefore,
                FullScript = scriptBefore,
            });
        }
    }
}
