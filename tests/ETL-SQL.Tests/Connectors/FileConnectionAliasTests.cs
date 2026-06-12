using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Connectors
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
                CREATE CONNECTION my_csv AS FLATFILE('{_tempFile.Replace("\\", "/")}', HEADER = ON);
                SELECT * FROM my_csv.FILE;
            ";

            await eval.Evaluate(new Lexer(script).TokenizeToScript());

            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Equal(3, result.Rows.Count);
            Assert.Equal("Alice", result.Rows[0]["name"]);
        }

        [Fact]
        public async Task TestFileConnectionLegacyAliasShouldFail()
        {
            var source = "CREATE CONNECTION my_conn AS FILE('data.csv');";
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();

            // Run linter manually as Parser no longer adds this diagnostic directly
            var lintResults = await ETL_SQL.Analysis.Linting.LinterFactory.CreateWithAllRules()
                .AnalyzeAsync(script, new ETL_SQL.Analysis.Linting.DefaultLintContext());

            Assert.Contains(lintResults, r => r.Message.Contains("Connection type 'FILE' is deprecated"));
        }

        [Fact]
        public async Task TestShorthandFileConnectionStillWorks()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = $@"
                CREATE CONNECTION my_csv AS FLATFILE('{_tempFile.Replace("\\", "/")}', HEADER = ON);
                SELECT * FROM my_csv;
            ";

            await eval.Evaluate(new Lexer(script).TokenizeToScript());

            var result = eval.LastResult;
            Assert.NotNull(result);
            Assert.Equal(3, result.Rows.Count);
        }
    }
}
