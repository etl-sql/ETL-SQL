using Xunit;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace ETL_SQL.Tests
{
    public class BulkInsertColumnsTest
    {
        [Fact]
        public async Task TestBulkInsertWithColumnMapping()
        {
            var provider = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = provider.GetRequiredService<Evaluator>();
            
            // Find project root by looking for the .slnx file
            string currentDir = Directory.GetCurrentDirectory();
            while (currentDir != null && !File.Exists(Path.Combine(currentDir, "ETL-SQL.slnx")))
            {
                currentDir = Directory.GetParent(currentDir)?.FullName;
            }
            string projectRoot = currentDir ?? Directory.GetCurrentDirectory();
            string dataPath = Path.Combine(projectRoot, "TestData", "test_bulk_mapped.csv").Replace("\\", "/");
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
            
            // Map validation
            Assert.Equal("Bob", row["Name"]);
            Assert.Equal("New York", row["Location"]);
            Assert.Equal("30", row["Age"]?.ToString());
        }
    }
}
