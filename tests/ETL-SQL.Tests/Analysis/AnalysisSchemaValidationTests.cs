using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using System;

namespace ETL_SQL.Tests.Analysis
{
    public class AnalysisSchemaValidationTests
    {
        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
            return parser.Parse();
        }

        [Fact]
        public async Task TestInsertIntoMissingFile_NoWarning()
        {
            var linter = new Linter();
            linter.AddRule(new SchemaValidationRule());

            var metadata = new MockFileMetadataProvider();
            var context = new DefaultLintContext { Metadata = metadata };

            var sql = @"
                CREATE CONNECTION MockGenerator ON FLATFILE('test.csv');
                INSERT INTO MockGenerator.FILE (TransactionID, Amount) VALUES ('TXN1', 100);
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, context);

            // Should have NO warnings for the columns because FLATFILE is a file connector and schema is empty
            var warnings = results.Where(r => r.Message.Contains("not found in target table")).ToList();
            Assert.Empty(warnings);
        }

        [Fact]
        public async Task TestInsertIntoExistingFile_WithWarningIfMissing()
        {
            var linter = new Linter();
            linter.AddRule(new SchemaValidationRule());

            var metadata = new MockFileMetadataProvider();
            metadata.Columns["FILE"] = new List<string> { "TransactionID" };
            
            var context = new DefaultLintContext { Metadata = metadata };

            var sql = @"
                CREATE CONNECTION MockGenerator ON FLATFILE('test.csv');
                INSERT INTO MockGenerator.FILE (TransactionID, Amount) VALUES ('TXN1', 100);
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, context);

            // Should have 1 warning for 'Amount' because 'FILE' already exists and has columns, but 'Amount' is missing
            var warnings = results.Where(r => r.Message.Contains("Column 'Amount' not found")).ToList();
            Assert.Single(warnings);
        }

        [Fact]
        public async Task TestInsertIntoDatabaseTable_WithWarningIfMissing()
        {
            var linter = new Linter();
            linter.AddRule(new SchemaValidationRule());

            var metadata = new MockDbMetadataProvider();
            metadata.Tables.Add("Users");
            metadata.Columns["Users"] = new List<string> { "ID" };
            
            var context = new DefaultLintContext { Metadata = metadata };

            var sql = @"
                CREATE CONNECTION MyDb ON MSSQL();
                INSERT INTO MyDb.Users (ID, Name) VALUES (1, 'Bob');
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, context);

            // Should have 1 warning for 'Name' even if table exists but column is missing in DB
            var warnings = results.Where(r => r.Message.Contains("Column 'Name' not found")).ToList();
            Assert.Single(warnings);
        }

        [Fact]
        public async Task TestSelectFromMissingFile_RaisesWarning()
        {
            var linter = new Linter();
            linter.AddRule(new SchemaValidationRule());

            var metadata = new MockFileMetadataProvider();
            var context = new DefaultLintContext { Metadata = metadata, DocumentUri = "file:///C:/test_dir/script.etlsql" };

            var missingFile = $"missing-file-{Guid.NewGuid()}.csv";
            var sql = $@"
                CREATE CONNECTION MockGenerator ON FLATFILE('{missingFile}');
                SELECT * FROM MockGenerator.FILE;
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, context);

            // Should have 1 warning about file not existing
            var warnings = results.Where(r => r.Message.Contains($"File '{missingFile}' for connection 'MockGenerator' does not exist.")).ToList();
            Assert.Single(warnings);
        }

        [Fact]
        public async Task TestInsertIntoMissingFile_NoMissingFileWarning()
        {
            var linter = new Linter();
            linter.AddRule(new SchemaValidationRule());

            var metadata = new MockFileMetadataProvider();
            var context = new DefaultLintContext { Metadata = metadata, DocumentUri = "file:///C:/test_dir/script.etlsql" };

            var missingFile = $"missing-file-{Guid.NewGuid()}.csv";
            var sql = $@"
                CREATE CONNECTION MockGenerator ON FLATFILE('{missingFile}');
                INSERT INTO MockGenerator.FILE (TransactionID, Amount) VALUES ('TXN1', 100);
            ";

            var script = Parse(sql);
            var results = await linter.AnalyzeAsync(script, context);

            // Should NOT have a warning about file not existing
            var warnings = results.Where(r => r.Message.Contains("does not exist")).ToList();
            Assert.Empty(warnings);
        }

        public class MockFileMetadataProvider : IMetadataProvider
        {
            public Dictionary<string, List<string>> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Task<IEnumerable<string>> GetTablesAsync(string connectionName) => Task.FromResult<IEnumerable<string>>(new[] { "FILE" });
            public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) => Task.FromResult(Columns.TryGetValue(tableName, out var cols) ? cols.AsEnumerable() : Enumerable.Empty<string>());
            public IEnumerable<string> GetConnections() => new[] { "MockGenerator" };
            public string? GetConnectionType(string connectionName) => "FLATFILE";
        }

        public class MockDbMetadataProvider : IMetadataProvider
        {
            public List<string> Tables { get; set; } = new();
            public Dictionary<string, List<string>> Columns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Task<IEnumerable<string>> GetTablesAsync(string connectionName) => Task.FromResult<IEnumerable<string>>(Tables);
            public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) => Task.FromResult(Columns.TryGetValue(tableName, out var cols) ? cols.AsEnumerable() : Enumerable.Empty<string>());
            public IEnumerable<string> GetConnections() => new[] { "MyDb" };
            public string? GetConnectionType(string connectionName) => "MSSQL";
        }
    }
}
