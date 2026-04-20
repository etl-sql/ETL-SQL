using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Common;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace ETL_SQL.Tests.Integration
{
    public class FileConnectionTests : IDisposable
    {
        private readonly string _tempFile;

        public FileConnectionTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"test_file_{Guid.NewGuid()}.csv");
            File.WriteAllText(_tempFile, "id,name,city\n1,Alice,New York\n2,Bob,London\n3,Charlie,Paris");
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        [Fact]
        public async Task TestFlatFileConnectionWithFileAlias()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = $@"
                CREATE CONNECTION my_csv ON FLATFILE('{_tempFile.Replace("\\", "/")}') WITH (HEADER = ON);
                SELECT * FROM my_csv.FILE;
            ";

            await eval.Evaluate(new Lexer(script).TokenizeToScript());

            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Equal(3, result.Rows.Count);
            Assert.Equal("Alice", result.Rows[0]["name"]);
        }

        [Fact]
        public void TestFileConnectionLegacyAliasShouldFail()
        {
            var source = "CREATE CONNECTION my_conn ON FILE('data.csv');";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            
            // Run linter manually as Parser no longer adds this diagnostic directly
            var lintResults = ETL_SQL.Core.Linting.LinterFactory.CreateWithAllRules()
                .AnalyzeAsync(script, new ETL_SQL.Core.Linting.DefaultLintContext()).Result;

            Assert.Contains(lintResults, r => r.Message.Contains("Connection type 'FILE' is deprecated"));
        }

        [Fact]
        public async Task TestShorthandFileConnectionStillWorks()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = $@"
                CREATE CONNECTION my_csv ON FLATFILE('{_tempFile.Replace("\\", "/")}') WITH (HEADER = ON);
                SELECT * FROM my_csv;
            ";

            await eval.Evaluate(new Lexer(script).TokenizeToScript());

            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Equal(3, result.Rows.Count);
        }
    }
}
