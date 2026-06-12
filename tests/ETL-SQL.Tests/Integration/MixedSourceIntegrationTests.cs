using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Data;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Xunit;

namespace ETL_SQL.Tests.Integration
{
    [Trait("Category", "Integration")]
    [Collection("Database collection")]
    public class MixedSourceIntegrationTests
    {
        private readonly DatabaseFixture _fixture;

        public MixedSourceIntegrationTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task TestJsonToParquetToMsSql()
        {
            AnsiConsole.MarkupLine("  - Scenario: JSON -> Parquet -> MSSQL...");

            string jsonPath = Path.Combine(AppContext.BaseDirectory, "users.json");
            string parquetPath = Path.Combine(AppContext.BaseDirectory, "users.parquet");

            // 1. Generate JSON data
            var users = Enumerable.Range(1, 1000).Select(i => new { Id = i, Name = $"User {i}", JoinDate = DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd HH:mm:ss") });
            var jsonContent = "[" + string.Join(",", users.Select(u => $"{{\"Id\":{u.Id},\"Name\":\"{u.Name}\",\"JoinDate\":\"{u.JoinDate}\"}}")) + "]";
            await File.WriteAllTextAsync(jsonPath, jsonContent);

            try
            {
                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                string script = $@"
                    CREATE CONNECTION json_src AS JSON('{jsonPath.Replace("\\", "/")}');
                    CREATE CONNECTION parq_dest AS PARQUET('{parquetPath.Replace("\\", "/")}');

                    -- JSON to Parquet
                    INSERT INTO parq_dest SELECT * FROM json_src;

                    CREATE CONNECTION db AS MSSQL('{_fixture.SqlConnectionString}');

                    EXECUTE db BEGIN
                        DROP TABLE IF EXISTS MixedUsers;
                        CREATE TABLE MixedUsers (Id INT, Name VARCHAR(100), JoinDate DATETIME);
                    END;

                    -- Parquet to MSSQL
                    INSERT INTO db.MixedUsers SELECT * FROM parq_dest;

                    SELECT COUNT(*) as Total FROM db.MixedUsers;
                ";

                await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

                int count = Convert.ToInt32(eval.LastResult?.Rows[0]["TOTAL"] ?? eval.LastResult?.Rows[0]["total"] ?? 0);
                Assert.Equal(1000, count);
            }
            finally
            {
                if (File.Exists(jsonPath)) File.Delete(jsonPath);
                if (File.Exists(parquetPath)) File.Delete(parquetPath);
            }
        }

        [Fact]
        public async Task TestCsvToPostgresToJson()
        {
            AnsiConsole.MarkupLine("  - Scenario: CSV -> Postgres -> JSON...");

            string csvPath = Path.Combine(AppContext.BaseDirectory, "data.csv");
            string jsonOutPath = Path.Combine(AppContext.BaseDirectory, "output.json");

            // 1. Generate CSV data
            await File.WriteAllTextAsync(csvPath, "id,category,amount\n1,A,10.5\n2,B,20.0\n3,A,15.75");

            try
            {
                var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
                string script = $@"
                    CREATE CONNECTION csv_src AS FLATFILE('{csvPath.Replace("\\", "/")}', HEADER = ON);
                    CREATE CONNECTION db AS POSTGRES('{_fixture.PostgresConnectionString}');

                    EXECUTE db BEGIN
                        DROP TABLE IF EXISTS mixed_categories;
                        CREATE TABLE mixed_categories (id INT, category VARCHAR(10), amount DECIMAL);
                    END;

                    -- CSV to Postgres
                    INSERT INTO db.mixed_categories SELECT CAST(id AS INT) as id, category, CAST(amount AS DECIMAL) as amount FROM csv_src;

                    CREATE CONNECTION json_dest AS JSON('{jsonOutPath.Replace("\\", "/")}');

                    -- Postgres to JSON
                    INSERT INTO json_dest SELECT * FROM db.mixed_categories;
                ";

                await eval.Evaluate(new Parser(new Lexer(script).Tokenize()).Parse());

                // Verify JSON FLATFILE exists and has content
                Assert.True(File.Exists(jsonOutPath), "Final JSON output file not found.");
                var jsonContent = await File.ReadAllTextAsync(jsonOutPath);
                Assert.False(string.IsNullOrEmpty(jsonContent), "Final JSON output file is empty.");

                // Check row count in database to be sure
                await eval.Evaluate(new Parser(new Lexer("SELECT COUNT(*) as Total FROM db.mixed_categories;").Tokenize()).Parse());
                int count = Convert.ToInt32(eval.LastResult?.Rows[0]["TOTAL"] ?? 0);
                Assert.Equal(3, count);
            }
            finally
            {
                if (File.Exists(csvPath)) File.Delete(csvPath);
                if (File.Exists(jsonOutPath)) File.Delete(jsonOutPath);
            }
        }
    }
}
