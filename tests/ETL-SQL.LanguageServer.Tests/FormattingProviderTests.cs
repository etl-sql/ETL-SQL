using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Formatting;
using ETL_SQL.LSP;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests
{
    public class FormattingProviderTests
    {
        [Fact]
        public async Task Formatter_Should_Use_Lsp_Config_If_No_Local_File()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<FormattingProvider>>();
            var store = new DocumentStateStore();
            var configMock = new Mock<ILanguageServerConfiguration>();

            var myConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string>
                {
                    {"etlsql:format:keywordCasing", "lower"},
                    {"etlsql:format:indentSize", "2"},
                    {"etlsql:format:commaPlacement", "trailing"}
                })
                .Build();

            configMock.Setup(c => c.GetSection(It.IsAny<string>()))
                .Returns((string key) => myConfiguration.GetSection(key));

            var handler = new FormattingProvider(loggerMock.Object, store, configMock.Object);

            var uri = DocumentUri.From("untitled:Untitled-1");
            var script = "SELECT * FROM Users;";
            store.SetState(uri, script, null!, null!);

            var formatParams = new DocumentFormattingParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Options = new FormattingOptions()
            };

            // Act
            var result = await handler.Handle(formatParams, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            var textEdit = Assert.Single(result);
            // Verify lowercase keyword casing, 2-space indentation, and trailing commas settings are applied
            Assert.Contains("select", textEdit.NewText);
            Assert.Contains("  *", textEdit.NewText);
            Assert.Contains("from Users;", textEdit.NewText);
        }

        [Fact]
        public void LoadFromWorkspace_Should_Find_Config_In_Parent_Directories()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            var subDir = Path.Combine(tempDir, "sub");
            Directory.CreateDirectory(subDir);

            var configPath = Path.Combine(tempDir, ".etlsqlformat.json");
            var json = @"{ ""keywordCasing"": ""lower"", ""indentSize"": 2 }";
            File.WriteAllText(configPath, json);

            var testFile = Path.Combine(subDir, "test.etlsql");

            try
            {
                // Act
                var options = FormatterOptions.LoadFromWorkspace(testFile);

                // Assert
                Assert.NotNull(options);
                Assert.Equal("lower", options.KeywordCasing);
                Assert.Equal(2, options.IndentSize);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task Formatter_PreservesNativeMicroChartMappings()
        {
            var store = new DocumentStateStore();
            var config = new Mock<ILanguageServerConfiguration>();
            config.Setup(item => item.GetSection(It.IsAny<string>()))
                .Returns(new ConfigurationBuilder().AddInMemoryCollection().Build().GetSection("etlsql"));
            var handler = new FormattingProvider(Mock.Of<ILogger<FormattingProvider>>(), store, config.Object);
            var uri = DocumentUri.From("untitled:microcharts.rptsql");
            const string script = """
                CREATE VISUAL Kpi AS CARD (SOURCE = #kpi, MAPPINGS (VALUE = total, SPARKLINE = #daily (X = day, Y = amount, TYPE = AREA)));
                CREATE VISUAL Goals AS TABLE (SOURCE = #goals, MAPPINGS (team, attainment PROGRESS_BAR (MIN = 0, MAX = 1, COLOR = '#16A34A') AS 'Goal'));
                """;
            store.SetState(uri, script, null!, null!);

            var result = await handler.Handle(new DocumentFormattingParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Options = new FormattingOptions()
            }, CancellationToken.None);
            var formatted = Assert.Single(result).NewText;

            var ast = new ETL_SQL.Core.Parser.Parser(new ETL_SQL.Core.Parser.Lexer(formatted).Tokenize(), formatted).Parse();
            var visuals = ast.Statements.OfType<ETL_SQL.Core.CreateVisualStatement>().ToList();
            var cardMicro = Assert.Single(visuals[0].Mappings, mapping => mapping.SparklineSource is not null);
            Assert.Equal("#daily", cardMicro.SparklineSource);
            Assert.Equal("day", cardMicro.SparklineXColumn);
            Assert.Equal("amount", cardMicro.SparklineYColumn);
            var progress = Assert.Single(visuals[1].Mappings, mapping => mapping.ProgressBar);
            Assert.Equal(0m, progress.ProgressMinimum);
            Assert.Equal(1m, progress.ProgressMaximum);
            Assert.Equal("#16A34A", progress.ProgressColor);
        }
    }
}
