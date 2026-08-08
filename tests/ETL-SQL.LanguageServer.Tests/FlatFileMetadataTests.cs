using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Core.Services;
using ETL_SQL.LSP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DocumentUri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri;
using Xunit;

namespace ETL_SQL.LanguageServer.Tests
{
    /// <summary>
    /// The Metadata Explorer showed a FLATFILE connection's table with nothing under it. The option
    /// form — FLATFILE(PATH='...', DELIMITER=',') — has no target expression, so the language server
    /// registered an empty connection string and the file was never opened.
    /// </summary>
    public class FlatFileMetadataTests : IDisposable
    {
        private readonly string _csv;

        public FlatFileMetadataTests()
        {
            _csv = Path.Combine(Path.GetTempPath(), $"etlsql_meta_{Guid.NewGuid():N}.csv");
            File.WriteAllText(_csv,
                "\"name\",\"date_of_birth\",\"date_of_death\",\"gender\"\n\"Doe, Jane\",1980-01-01,,FEMALE\n");
        }

        public void Dispose()
        {
            if (File.Exists(_csv)) File.Delete(_csv);
        }

        private async Task<MetadataManager> AnalyzeAsync(string script)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();

            var registry = new ETL_SQL.Data.ConnectorRegistry();
            registry.Register(new FlatFileConnector());

            var metadata = new MetadataManager(ETL_SQL.Common.NullLogger.Instance, registry);
            var handler = new TextDocumentHandler(loggerFactory, metadata, new DocumentStateStore());

            await handler.AnalyzeAsync(DocumentUri.From("untitled:Untitled-1"), script);
            return metadata;
        }

        private string Script =>
            $"CREATE CONNECTION pats AS FLATFILE(PATH='{_csv}', TEXT_QUALIFIER='\"', DELIMITER=',', HEADER=TRUE);";

        private static CustomMethodsHandler NewHandler(MetadataManager metadata)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var provider = services.BuildServiceProvider();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

            return new CustomMethodsHandler(
                metadata,
                loggerFactory.CreateLogger<CustomMethodsHandler>(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                new DatasetStore(loggerFactory.CreateLogger<DatasetStore>()),
                new ETL_SQL.Services.SecurityService(ETL_SQL.Common.NullLogger.Instance));
        }

        /// <summary>
        /// DUAL is injected into every connection so "SELECT 1 FROM DUAL" completes, but it is not
        /// something to browse — it showed up under every connection with a DUMMY column beneath it.
        /// </summary>
        [Fact]
        public async Task ExplorerTableListHidesTheSyntheticDualTable()
        {
            var metadata = await AnalyzeAsync(Script);

            var forCompletion = await metadata.GetTablesAsync("pats", "untitled:Untitled-1");
            Assert.Contains(forCompletion, t => t.Equals("DUAL", StringComparison.OrdinalIgnoreCase));

            var response = await NewHandler(metadata).Handle(
                new GetTablesParams { connectionName = "pats", uri = "untitled:Untitled-1" },
                System.Threading.CancellationToken.None);

            Assert.DoesNotContain(response.tables, t => t.Equals("DUAL", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(response.tables, t => t.Equals("FILE", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The explorer shows "date_of_birth::date", but a drag must insert only the name — so the
        /// type travels as its own field rather than being formatted into the column name.
        /// </summary>
        [Fact]
        public async Task ColumnDetailsAreReturnedAlongsideTheBareNames()
        {
            var metadata = await AnalyzeAsync(Script);

            var response = await NewHandler(metadata).Handle(
                new GetColumnsParams { connectionName = "pats", tableName = "FILE", uri = "untitled:Untitled-1" },
                System.Threading.CancellationToken.None);

            Assert.Equal(response.columns.Count, response.columnDetails.Count);
            Assert.Equal(response.columns, response.columnDetails.Select(d => d.name).ToList());
            Assert.All(response.columnDetails, d => Assert.DoesNotContain("::", d.name));
        }

        /// <summary>The bare-string form must keep working — it takes the other branch.</summary>
        [Fact]
        public async Task BareStringFormStillReportsItsColumns()
        {
            var metadata = await AnalyzeAsync($"CREATE CONNECTION pats AS FLATFILE('{_csv}');");

            var columns = (await metadata.GetColumnsAsync("pats", "FILE", "untitled:Untitled-1")).ToList();

            Assert.Contains("name", columns);
        }

        /// <summary>
        /// Static analysis cannot evaluate a variable, so such an option is skipped rather than
        /// guessed at. The document must still analyse instead of throwing.
        /// </summary>
        [Fact]
        public async Task NonLiteralOptionValuesDegradeInsteadOfThrowing()
        {
            var metadata = await AnalyzeAsync(
                "DECLARE @p VARCHAR = 'C:\\tmp\\x.csv';\nCREATE CONNECTION pats AS FLATFILE(PATH=@p, HEADER=TRUE);");

            Assert.Contains(metadata.GetConnections("untitled:Untitled-1"), c => c.Name == "pats");
        }

        [Fact]
        public async Task FlatFileTableReportsItsColumns()
        {
            var metadata = await AnalyzeAsync(Script);

            var columns = (await metadata.GetColumnsAsync("pats", "FILE", "untitled:Untitled-1")).ToList();

            Assert.Equal(new[] { "name", "date_of_birth", "date_of_death", "gender" }, columns);
        }

        /// <summary>The text qualifier belongs to the file format, not to the column name.</summary>
        [Fact]
        public async Task ColumnNamesAreNotWrappedInTheTextQualifier()
        {
            var metadata = await AnalyzeAsync(Script);

            var columns = (await metadata.GetColumnsAsync("pats", "FILE", "untitled:Untitled-1")).ToList();

            Assert.All(columns, c => Assert.DoesNotContain("\"", c));
        }
    }
}
