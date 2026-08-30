using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Editor support for data-quality rules: the rule vocabulary completes after <c>EXPECT</c> and
    /// the column actions after <c>ON FAILURE</c>, while the comment-tag path offers neither —
    /// a completion list is where an author discovers a language, so it must not teach a form that
    /// no longer enforces anything.
    /// </summary>
    public class DataQualityCompletionTests
    {
        [Fact]
        public async Task ExpectClause_CompletesRuleStarters()
        {
            var suggestions = await Suggest("SELECT Id EXPECT ");

            Assert.Contains(suggestions, s => s.Text == "NOT NULL");
            Assert.Contains(suggestions, s => s.Text == "UNIQUE");
            Assert.Contains(suggestions, s => s.Text == "EXISTS IN ");
            Assert.Contains(suggestions, s => s.Text == "MATCHES ");
        }

        [Fact]
        public async Task ExpectClause_FiltersByPartialInput()
        {
            var suggestions = await Suggest("SELECT Id EXPECT UNIQ");

            Assert.Contains(suggestions, s => s.Text == "UNIQUE");
            Assert.Contains(suggestions, s => s.Text == "UNIQUE_FIRST BY ");
            Assert.DoesNotContain(suggestions, s => s.Text == "NOT NULL");
        }

        [Fact]
        public async Task OnFailure_CompletesTheColumnActions()
        {
            var suggestions = await Suggest("SELECT Id EXPECT NOT NULL ON FAILURE ");

            Assert.Contains(suggestions, s => s.Text == "THROW");
            Assert.Contains(suggestions, s => s.Text == "WARN");
            Assert.Contains(suggestions, s => s.Text == "QUARANTINE");
        }

        [Fact]
        public async Task OnFailure_FiltersByPartialInput()
        {
            var suggestions = await Suggest("SELECT Id EXPECT NOT NULL ON FAILURE QUA");

            Assert.Contains(suggestions, s => s.Text == "QUARANTINE");
            Assert.DoesNotContain(suggestions, s => s.Text == "WARN");
        }

        [Fact]
        public async Task RuleVocabulary_IsNotOfferedInsideAComment()
        {
            // Inside /* … */ this text is documentation, not grammar.
            var suggestions = await Suggest("SELECT Id /* EXPECT ");
            Assert.DoesNotContain(suggestions, s => s.Text == "NOT NULL");
        }

        [Fact]
        public async Task ExpectAndFail_AreNotOfferedAsTagValues()
        {
            // Writing a rule as a tag is a lint Error; completing one would walk the author into it.
            var expect = await Suggest("SELECT Id /* @expect: '");
            Assert.DoesNotContain(expect, s => s.Text == "NOT NULL");

            // A bare THROW still comes from the general keyword list; the tag-value path is what
            // must stay silent, and it emits documented suggestions.
            var fail = await Suggest("SELECT Id /* @fail: '");
            Assert.DoesNotContain(fail, s => s.Text == "THROW" && s.Documentation is { Length: > 0 });
        }

        [Fact]
        public async Task DescriptiveTagValues_StillComplete()
        {
            // The tag pipeline is untouched for the tags that describe rather than enforce.
            var suggestions = await Suggest("SELECT Id /* @classification: '");
            Assert.Contains(suggestions, s => s.Text.Equals("confidential", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task FreeFormTag_HasNoValueCompletions()
        {
            // @owner is a free-form string tag: no enum values and no rule starters. (A bare
            // "THROW" may still appear from the general SQL keyword list — the tag-value path
            // emits the quoted form, so assert on that.)
            var suggestions = await Suggest("SELECT Id /* @owner: ");
            Assert.DoesNotContain(suggestions, s => s.Text == "'THROW'");
            Assert.DoesNotContain(suggestions, s => s.Text == "NOT NULL");
        }

        private static async Task<System.Collections.Generic.List<Suggestion>> Suggest(
            string scriptBefore, string prefix = "")
        {
            var metadata = new Mock<IMetadataManager>();
            var service = new LanguageService(metadata.Object);
            return await service.GetSuggestionsAsync(new SuggestionContext
            {
                Prefix = prefix,
                ScriptBefore = scriptBefore,
                FullScript = scriptBefore,
            });
        }
    }
}
