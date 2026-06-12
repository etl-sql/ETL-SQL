using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    public class AnalysisScriptDiscoveryTests
    {
        [Fact]
        public async Task Linter_ShouldDiscoverTable_InsideExecutePushdownBlock()
        {
            // Arrange
            var scriptText = @"
                CREATE CONNECTION s AS MSSQL('conn_string');
                EXECUTE s
                BEGIN
                    CREATE TABLE dbo.SourceSystem(id int, description varchar(200));
                END
                SELECT * FROM s.SourceSystem;
            ";

            var lexer = new Lexer(scriptText);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, scriptText);
            var script = parser.Parse();

            var linter = new Linter();
            linter.AddRule(new SchemaValidationRule());

            // Use a mock provider that has NO tables initially
            var mockMetadata = new MockMetadataProvider();
            var context = new DefaultLintContext { Metadata = mockMetadata };

            // Act
            var results = await linter.AnalyzeAsync(script, context);

            // Assert
            // Detailed message check: we expect NO "Table not found" errors for SourceSystem
            var tableErrors = results.Where(r => r.Message.Contains("Table 'SourceSystem' not found")).ToList();
            Assert.Empty(tableErrors);
        }

        [Fact]
        public async Task Linter_ShouldDiscoverTable_InsideExecutePushdownBlock_WithRegexFallback()
        {
            // Arrange - Native SQL that ETL-SQL parser might fail on (e.g. non-standard column constraints)
            var scriptText = @"
                CREATE CONNECTION s AS MSSQL('conn_string');
                EXECUTE s
                BEGIN
                    -- This is native SQL that our parser might not like (e.g. specific dialect hint)
                    CREATE TABLE NativeTable(id int, OPTIMIZATION_HINT = 1);
                END
                SELECT * FROM s.NativeTable;
            ";

            var lexer = new Lexer(scriptText);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, scriptText);
            var script = parser.Parse();

            var linter = new Linter();
            linter.AddRule(new SchemaValidationRule());

            var mockMetadata = new MockMetadataProvider();
            var context = new DefaultLintContext { Metadata = mockMetadata };

            // Act
            var results = await linter.AnalyzeAsync(script, context);

            // Assert
            var tableErrors = results.Where(r => r.Message.Contains("NativeTable")).ToList();
            Assert.Empty(tableErrors);
        }

        private class MockMetadataProvider : IMetadataProvider
        {
            public Task<IEnumerable<string>> GetTablesAsync(string connectionName) => Task.FromResult(Enumerable.Empty<string>());
            public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) => Task.FromResult(Enumerable.Empty<string>());
            public IEnumerable<string> GetConnections() => new[] { "s" };
            public string? GetConnectionType(string connectionName) => "MSSQL";
        }
    }
}
