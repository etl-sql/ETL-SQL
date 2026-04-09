using System;
using Xunit;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;
using Xunit.Abstractions;

namespace ETL_SQL.Tests
{
    public class TempDiagTests
    {
        private readonly ITestOutputHelper _output;

        public TempDiagTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task PrintDiagnostics()
        {
            string sql = "DECLARE @id int;\n         ,@name varchar(100);";
            var tokens = new Lexer(sql).Tokenize();
            var parser = new Parser(tokens, sql);
            var script = parser.Parse();

            _output.WriteLine("Parser Diagnostics:");
            foreach (var diag in script.Diagnostics)
            {
                _output.WriteLine($" - {diag.Severity}: {diag.Message} at {diag.Line}:{diag.Column}");
            }

            var linter = new Linter();
            linter.AddRule(new UndeclaredVariableRule());
            
            // Dummy context
            var ctx = new DefaultLintContext { DocumentUri = "test.sql" };
            var lintResults = await linter.AnalyzeAsync(script, ctx);

            _output.WriteLine("Linter Diagnostics:");
            foreach (var res in lintResults)
            {
                _output.WriteLine($" - {res.Severity}: {res.Message} at {res.LineNumber}:{res.ColumnNumber}");
            }
        }
    }
}
