using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Execution;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Parser.Components;
using ETL_SQL.Engine;
using ETL_SQL.Reporting;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests
{
    public class ReportMetadataTests
    {
        [Fact]
        public async Task SetReport_Global_Overrides_Are_Populated_In_Manifest()
        {
            // 1. Arrange
            var script = @"
                SET REPORT TITLE = 'Overridden Title';
                SET REPORT CSS = 'body { color: blue; }';
                SET REPORT JS = 'console.log(1)';
                SET REPORT HEAD = '<meta>';
                SET REPORT BODY = '<div>Start</div>';
                SET REPORT FOOTER = '<div>End</div>';
                SET REPORT FAVICON = 'fav.ico';
                SET REPORT LOGO = 'logo.png';
                SET REPORT BACKGROUND = 'red';
                SET REPORT THEME = 'dark';
                SET REPORT NAVIGATION = 'Side';
            ";

            var lexer = new Lexer(script);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, script);
            var parsedScript = parser.Parse();

            // Use a real Evaluator (mocking required services)
            var handlers = new List<IStatementHandler> {
                new ETL_SQL.Engine.Handlers.SetReportMetadataStatementHandler()
            };

            var mockLogger = new Mock<ETL_SQL.Common.ILogger>();
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockFunctions = new Mock<ETL_SQL.Core.Functions.IFunctionRegistry>();
            var mockLineage = new Mock<ILineageTracker>();
            var globalMetadata = new Dictionary<string, string>();
            mockLineage.Setup(l => l.GlobalMetadata).Returns(globalMetadata);

            var mockDocker = new Mock<IDockerManager>();
            var mockConnectorRegistry = new Mock<IConnectorRegistry>();
            var mockSessionManager = new Mock<ISessionStateManager>();
            var security = new SecurityService(mockLogger.Object);

            var evaluator = new Evaluator(
                handlers,
                mockServiceProvider.Object,
                mockFunctions.Object,
                mockLineage.Object,
                mockDocker.Object,
                mockConnectorRegistry.Object,
                mockSessionManager.Object,
                security,
                mockLogger.Object,
                new ETL_SQL.Core.Metadata.LanguageHelpRegistry()
            );

            // 2. Act
            await evaluator.Evaluate(parsedScript);

            var builder = new ManifestBuilder(evaluator);
            var manifest = await builder.BuildAsync("test.rptsql");

            // 3. Assert
            Assert.Equal("Overridden Title", manifest.Title);
            Assert.Equal("body { color: blue; }", manifest.Css);
            Assert.Equal("console.log(1)", manifest.Js);
            Assert.Equal("<meta>", manifest.HtmlHead);
            Assert.Equal("<div>Start</div>", manifest.HtmlBody);
            Assert.Equal("<div>End</div>", manifest.HtmlFooter);
            Assert.Equal("fav.ico", manifest.Favicon);
            Assert.Equal("logo.png", manifest.Logo);
            Assert.Equal("red", manifest.Background);
            Assert.Equal("dark", manifest.Theme);
            Assert.Equal("Side", manifest.Navigation);
        }
    }
}
