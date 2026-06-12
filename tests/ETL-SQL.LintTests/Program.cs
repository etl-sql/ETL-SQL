using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.LintTests
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                string sql = @"
SELECT * FROM
-- Missing table (Syntax Error recovery test)
;

SELECT InvalidCol FROM NonExistentTable;
";
                Console.WriteLine("--- Testing Lexer ---");
                var lexer = new Lexer(sql);
                var tokens = lexer.Tokenize();
                Console.WriteLine($"Lexed {tokens.Count} tokens.");

                Console.WriteLine("\n--- Testing Parser & Error Recovery ---");
                var parser = new ETL_SQL.Core.Parser.Parser(tokens);
                var script = parser.Parse();

                Console.WriteLine($"Parsed {script.Statements.Count} statements.");
                foreach (var diag in script.Diagnostics)
                {
                    Console.WriteLine($"[PARSER {diag.Severity}] {diag.Message} at {diag.Line}:{diag.Column}");
                }

                Console.WriteLine("\n--- Testing Schema Linter ---");
                var context = new DefaultLintContext
                {
                    Metadata = new MockMetadataProvider()
                };

                var linter = new Linter();
                linter.AddRule(new SchemaValidationRule());

                var results = await linter.AnalyzeAsync(script, context);
                foreach (var res in results)
                {
                    Console.WriteLine($"[LINTER {res.Severity}] {res.Message} at {res.LineNumber}:{res.ColumnNumber}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL FAILURE: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    class MockMetadataProvider : IMetadataProvider
    {
        public Task<IEnumerable<string>> GetTablesAsync(string connectionName) => Task.FromResult<IEnumerable<string>>(new[] { "ExistingTable" });
        public Task<IEnumerable<string>> GetColumnsAsync(string connectionName, string tableName) => Task.FromResult<IEnumerable<string>>(new[] { "ExistingCol" });
        public IEnumerable<string> GetConnections() => new[] { "DEFAULT" };
        public string? GetConnectionType(string connectionName) => "MSSQL";
    }
}
