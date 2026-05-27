using Xunit;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Repro
{
    public class FlatFileReproTests
    {
        [Fact]
        public async Task Repro_SelectIntoFlatFile_ShouldCreateFileWithHeaders()
        {
            // Setup
            string tempDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_Repro_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string csvPath = Path.Combine(tempDir, "Users.csv");

            try
            {
                var sql = $@"
                    CREATE CONNECTION m AS MOCKDB();
                    CREATE CONNECTION c AS FLATFILE('{csvPath.Replace("\\", "\\\\")}', ROW_DELIMITER='CRLF');
                    SELECT *
                    INTO c.FILE
                    FROM m.Users;
                ";

                var serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
                var evaluator = serviceProvider.GetRequiredService<Evaluator>();
                
                // Execute
                await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());

                // Verify
                Assert.True(File.Exists(csvPath), "File should be created");
                var content = await File.ReadAllTextAsync(csvPath);
                Assert.NotEmpty(content);
                
                // Check headers (from MockDataSeeder)
                Assert.Contains("UserID,UserName,Email", content);
                
                // Check row count (MockDataSeeder seeds 150 users)
                var lines = content.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(151, lines.Length); // 150 rows + 1 header
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task Repro_InsertValuesIntoFlatFile_ShouldWork()
        {
            // Setup
            string tempDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_Repro_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string csvPath = Path.Combine(tempDir, "Values.csv");

            try
            {
                var sql = $@"
                    CREATE CONNECTION c AS FLATFILE('{csvPath.Replace("\\", "\\\\")}', HEADER='ON');
                    INSERT INTO c.FILE (ID, Name) VALUES (1, 'Alice');
                    INSERT INTO c.FILE (ID, Name) VALUES (2, 'Bob');
                ";

                var serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
                var evaluator = serviceProvider.GetRequiredService<Evaluator>();
                
                // Execute
                await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());

                // Verify
                Assert.True(File.Exists(csvPath), "File should be created");
                var content = await File.ReadAllTextAsync(csvPath);
                
                Assert.Contains("ID,Name", content);
                Assert.Contains("1,Alice", content);
                Assert.Contains("2,Bob", content);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task Repro_SelectIntoParquet_ShouldWork()
        {
            // Setup
            string tempDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_Repro_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string pqPath = Path.Combine(tempDir, "Users.parquet");

            try
            {
                var sql = $@"
                    CREATE CONNECTION m AS MOCKDB();
                    CREATE CONNECTION p AS PARQUET('{pqPath.Replace("\\", "\\\\")}');
                    SELECT *
                    INTO p.FILE
                    FROM m.Users;
                ";

                var serviceProvider = DependencyInjectionSetup.BuildServiceProvider();
                var evaluator = serviceProvider.GetRequiredService<Evaluator>();
                
                // Execute
                await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());

                // Verify
                Assert.True(File.Exists(pqPath), "File should be created");
                Assert.True(new FileInfo(pqPath).Length > 0, "File should have data");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
