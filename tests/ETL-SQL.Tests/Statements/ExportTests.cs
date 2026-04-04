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
using Spectre.Console;

namespace ETL_SQL.Tests
{
    public class ExportTests
    {

        [Fact]
        public async Task TestJsonExport()
        {
            var sql = @"
                CREATE CONNECTION src ON JSON ('test_data.json');
                CREATE CONNECTION dest ON JSON ('export.json');

                -- Create dummy data
                SELECT 1 AS ID, 'Alice' AS Name, 30 AS Age
                INTO #data
                UNION ALL
                SELECT 2 AS ID, 'Bob' AS Name, 25 AS Age;

                INSERT INTO dest
                SELECT * FROM #data FOR JSON PATH;
            ";

            await File.WriteAllTextAsync("test_data.json", "[{\"ID\": 0}]");
            
            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var tokens = new Lexer(sql).Tokenize();
            var script = new Parser(tokens).Parse();
            await evaluator.Evaluate(script);

            Assert.True(File.Exists("export.json"), "JSON export file not created");
            var content = await File.ReadAllTextAsync("export.json");
            Assert.Contains("\"ID\": 1", content);
            Assert.Contains("\"Name\": \"Alice\"", content);

            File.Delete("test_data.json");
            File.Delete("export.json");
        }

        [Fact]
        public async Task TestAdvancedJsonExport()
        {
            var sql = @"
                CREATE CONNECTION dest ON JSON ('export_adv.json');

                SELECT 1 AS [User.ID], 'Alice' AS [User.Info.Name], 'New York' AS [User.Info.Address.City], 'Active' AS Status
                INTO #data;

                INSERT INTO dest
                SELECT * FROM #data FOR JSON PATH, ROOT('Users');
            ";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var tokens = new Lexer(sql).Tokenize();
            var script = new Parser(tokens).Parse();
            await evaluator.Evaluate(script);

            Assert.True(File.Exists("export_adv.json"), "Advanced JSON export file not created");
            var content = await File.ReadAllTextAsync("export_adv.json");
            
            Assert.Contains("\"Users\":", content);
            Assert.Contains("\"User\": {", content);
            Assert.Contains("\"Info\": {", content);
            Assert.Contains("\"Address\": {", content);
            Assert.Contains("\"City\": \"New York\"", content);

            File.Delete("export_adv.json");
        }

        [Fact]
        public async Task TestXmlExport()
        {
            var sql = @"
                CREATE CONNECTION dest ON XML ('export.xml');

                SELECT 1 AS [ID], 'Alice' AS [Details.Name], 'NY' AS [Details.Loc]
                INTO #data;

                INSERT INTO dest
                SELECT * FROM #data FOR XML PATH, ROOT('People');
            ";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var tokens = new Lexer(sql).Tokenize();
            var script = new Parser(tokens).Parse();
            await evaluator.Evaluate(script);

            Assert.True(File.Exists("export.xml"), "XML export file not created");
            var content = await File.ReadAllTextAsync("export.xml");
            
            Assert.Contains("<People>", content);
            Assert.Contains("<Details>", content);
            Assert.Contains("<Name>Alice</Name>", content);

            File.Delete("export.xml");
        }

        [Fact]
        public async Task TestCsvExport()
        {
            var sql = @"
                CREATE CONNECTION dest ON FLATFILE ('export.csv');

                SELECT 1 AS ID, 'Alice' AS Name
                INTO #data;

                INSERT INTO dest
                SELECT * FROM #data;
            ";

            var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            var tokens = new Lexer(sql).Tokenize();
            var script = new Parser(tokens).Parse();
            await evaluator.Evaluate(script);

            Assert.True(File.Exists("export.csv"), "CSV export file not created");
            var content = await File.ReadAllTextAsync("export.csv");
            Assert.Contains("ID,Name", content);
            Assert.Contains("1,Alice", content);

            File.Delete("export.csv");
        }
    }
}
