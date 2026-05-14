using Xunit;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace ETL_SQL.Tests.Statements
{
    public class BulkInsertColumnsTest
    {
        private static string FindProjectRoot()
        {
            string currentDir = Directory.GetCurrentDirectory();
            while (currentDir != null && !File.Exists(Path.Combine(currentDir, "ETL-SQL.slnx")))
                currentDir = Directory.GetParent(currentDir)?.FullName;
            return currentDir ?? Directory.GetCurrentDirectory();
        }

        [Fact]
        public async Task TestBulkInsertWithColumnMapping()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();

            string dataPath = Path.Combine(FindProjectRoot(), "TestData", "test_bulk_mapped.csv").Replace("\\", "/");
            string script = $@"
                CREATE TABLE #Target (
                    Name VARCHAR(100),
                    Age INT,
                    Location VARCHAR(100)
                );

                BULK INSERT #Target (Name, Location, Age)
                FROM '{dataPath}'
                WITH (FORMAT = 'CSV');

                SELECT * FROM #Target;
            ";

            var parser = new Parser(new Lexer(script).Tokenize());
            var ast = parser.Parse();

            await evaluator.Evaluate(ast);

            Assert.NotNull(evaluator.LastResult);
            Assert.Equal(3, evaluator.LastResult.Rows.Count);
            var row = evaluator.LastResult.Rows[0];

            Assert.Equal("Bob", row["Name"]);
            Assert.Equal("New York", row["Location"]);
            Assert.Equal("30", row["Age"]?.ToString());
        }

        [Fact]
        public async Task TestBulkInsert_SourceFewerColumnsThanMapping_ExtraColumnsAreNull()
        {
            // CSV has 2 columns (Name, Age); mapping specifies 3 (Name, Age, Location).
            // The positional loop stops at min(mapping, source) so Location is never set → null.
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();

            string dataPath = Path.Combine(FindProjectRoot(), "TestData", "test_bulk_few_cols.csv").Replace("\\", "/");
            string script = $@"
                CREATE TABLE #T (Name VARCHAR(100), Age INT, Location VARCHAR(100));
                BULK INSERT #T (Name, Age, Location) FROM '{dataPath}' WITH (FORMAT = 'CSV');
                SELECT * FROM #T;
            ";

            var ast = new Parser(new Lexer(script).Tokenize(), script).Parse();
            await evaluator.Evaluate(ast);

            Assert.NotNull(evaluator.LastResult);
            Assert.Equal(2, evaluator.LastResult.Rows.Count);
            var row0 = evaluator.LastResult.Rows[0];
            Assert.Equal("Alice", row0["Name"]);
            Assert.Equal("25", row0["Age"]?.ToString());
            Assert.Null(row0["Location"]);
        }

        [Fact]
        public async Task TestBulkInsert_EmptyFieldsInSource_MappedAsNull()
        {
            // CSV: "Alice,,London" — middle field is empty; should map to null/empty for Age.
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();

            string dataPath = Path.Combine(FindProjectRoot(), "TestData", "test_bulk_null_vals.csv").Replace("\\", "/");
            string script = $@"
                CREATE TABLE #T2 (Name VARCHAR(100), Age VARCHAR(10), Location VARCHAR(100));
                BULK INSERT #T2 (Name, Age, Location) FROM '{dataPath}' WITH (FORMAT = 'CSV');
                SELECT * FROM #T2;
            ";

            var ast = new Parser(new Lexer(script).Tokenize(), script).Parse();
            await evaluator.Evaluate(ast);

            Assert.NotNull(evaluator.LastResult);
            Assert.Equal(2, evaluator.LastResult.Rows.Count);

            var alice = evaluator.LastResult.Rows[0];
            Assert.Equal("Alice", alice["Name"]);
            // Age field is empty in the CSV — expect null or empty string
            Assert.True(alice["Age"] == null || alice["Age"]?.ToString() == "",
                $"Expected null or empty for Age, got: '{alice["Age"]}'");
            Assert.Equal("London", alice["Location"]);

            var bob = evaluator.LastResult.Rows[1];
            Assert.Equal("Bob", bob["Name"]);
            Assert.Equal("30", bob["Age"]?.ToString());
            // Location field is empty in the CSV — expect null or empty string
            Assert.True(bob["Location"] == null || bob["Location"]?.ToString() == "",
                $"Expected null or empty for Location, got: '{bob["Location"]}'");
        }
    }
}
