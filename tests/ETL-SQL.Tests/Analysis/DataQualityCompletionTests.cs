using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Editor support for the data-quality tags: @expect / @fail appear in tag-name completion with
    /// real documentation, and their values complete too (rule starters for @expect, the action
    /// enum for @fail).
    /// </summary>
    public class DataQualityCompletionTests
    {
        [Fact]
        public async Task ExpectAndFail_AppearInTagNameCompletions_WithDocumentation()
        {
            var suggestions = await Suggest("SELECT Id /* @", prefix: "@");

            var expect = Assert.Single(suggestions, s => s.Text == "@expect");
            Assert.Contains("NOT NULL", expect.Documentation);
            Assert.Contains("UNIQUE", expect.Documentation);

            var fail = Assert.Single(suggestions, s => s.Text == "@fail");
            Assert.Contains("QUARANTINE", fail.Documentation);
        }

        [Fact]
        public async Task TagNameCompletion_FiltersByPrefix()
        {
            var suggestions = await Suggest("SELECT Id /* @exp", prefix: "@exp");
            Assert.Contains(suggestions, s => s.Text == "@expect");
            Assert.DoesNotContain(suggestions, s => s.Text == "@owner");
        }

        [Fact]
        public async Task FailValue_CompletesTheActionEnum_Quoted()
        {
            var suggestions = await Suggest("SELECT Id /* @expect: 'NOT NULL'; @fail: ");

            Assert.Contains(suggestions, s => s.Text == "'THROW'");
            Assert.Contains(suggestions, s => s.Text == "'WARN'");
            Assert.Contains(suggestions, s => s.Text == "'QUARANTINE'");
        }

        [Fact]
        public async Task FailValue_InsideAnOpenQuote_CompletesBare()
        {
            var suggestions = await Suggest("SELECT Id /* @fail: '");

            Assert.Contains(suggestions, s => s.Text == "THROW");
            Assert.DoesNotContain(suggestions, s => s.Text == "'THROW'");
        }

        [Fact]
        public async Task FailValue_FiltersByPartialInput()
        {
            var suggestions = await Suggest("SELECT Id /* @fail: 'QUA");

            Assert.Contains(suggestions, s => s.Text == "QUARANTINE");
            Assert.DoesNotContain(suggestions, s => s.Text == "WARN");
        }

        [Fact]
        public async Task ExpectValue_CompletesRuleStarters()
        {
            var suggestions = await Suggest("SELECT Id /* @expect: '");

            Assert.Contains(suggestions, s => s.Text == "NOT NULL");
            Assert.Contains(suggestions, s => s.Text == "UNIQUE");
            Assert.Contains(suggestions, s => s.Text == "EXISTS IN ");
            Assert.Contains(suggestions, s => s.Text == "MATCHES ");
        }

        [Fact]
        public async Task NumberedExpectVariant_CompletesTheSameRuleStarters()
        {
            var suggestions = await Suggest("SELECT Id /* @expect_1: '");
            Assert.Contains(suggestions, s => s.Text == "NOT NULL");
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
