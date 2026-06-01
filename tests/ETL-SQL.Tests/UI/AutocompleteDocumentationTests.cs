using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core.Services;
using ETL_SQL.Core.Interfaces;
using ETL_SQL.TUI.UI;
using ETL_SQL.Common;

using CoreSuggestionContext = ETL_SQL.Core.Services.SuggestionContext;
using TuiSuggestionContext = ETL_SQL.TUI.UI.SuggestionContext;
using CoreMetadataManager = ETL_SQL.Core.Services.MetadataManager;

namespace ETL_SQL.Tests.UI
{
    public class AutocompleteDocumentationTests
    {
        private class MockHelpRegistry : ILanguageHelpRegistry
        {
            public void RegisterHelp(string topic, string helpText, string? subTopic = null) { }
            public string? GetHelp(string topic, string? subTopic = null)
            {
                if (topic == "SELECT" && subTopic == null) return "The SELECT statement is used to fetch data.";
                if (topic == "FUNCTION" && subTopic == "GETDATE") return "Returns current date.";
                return null;
            }
            public IEnumerable<string> GetTopics() => new[] { "SQL", "FUNCTION" };
            public IEnumerable<string> GetSubTopics(string topic) => topic == "SQL" ? new[] { "SELECT" } : new[] { "GETDATE" };
        }

        [Fact]
        public async Task LanguageService_ReturnsKeywordDocumentation()
        {
            var registry = new MockHelpRegistry();
            var service = new LanguageService(new CoreMetadataManager(null!, ConnectorRegistry.Instance), registry);
            
            var context = new CoreSuggestionContext { Prefix = "SEL" };
            var suggestions = await service.GetSuggestionsAsync(context);
            
            var select = suggestions.FirstOrDefault(s => s.Text == "SELECT");
            Assert.NotNull(select);
            Assert.Equal("The SELECT statement is used to fetch data.", select.Documentation);
        }

        [Fact]
        public async Task LanguageService_ReturnsFunctionDocumentation()
        {
            var registry = new MockHelpRegistry();
            var service = new LanguageService(new CoreMetadataManager(null!, ConnectorRegistry.Instance), registry);
            
            var context = new CoreSuggestionContext { Prefix = "GETD" };
            var suggestions = await service.GetSuggestionsAsync(context);
            
            var getdate = suggestions.FirstOrDefault(s => s.Text == "GETDATE");
            Assert.NotNull(getdate);
            Assert.Equal("Returns current date.", getdate.Documentation);
        }

        [Fact]
        public async Task TuiBridge_PreservesDocumentation()
        {
            var registry = new MockHelpRegistry();
            var engine = new SuggestionEngine(registry);
            
            var context = new TuiSuggestionContext
            {
                Prefix = "SEL",
                Connections = new Dictionary<string, ETL_SQL.Data.IDataSource>()
            };
            
            var suggestions = await engine.GetSuggestionsAsync(context);
            var select = suggestions.FirstOrDefault(s => s.Text == "SELECT");
            
            Assert.NotNull(select);
            Assert.Equal("The SELECT statement is used to fetch data.", select.Documentation);
        }
    }
}
