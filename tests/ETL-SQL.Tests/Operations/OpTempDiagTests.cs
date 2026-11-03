using System;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using Xunit;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Operations
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

            // The leading comma on line 2 is a syntax error the parser must report there, not swallow.
            Assert.Contains(script.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Line == 2 && d.Message.Contains("COMMA"));

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

            // @id is declared, so the undeclared-variable rule must not flag it despite the broken second line.
            Assert.DoesNotContain(lintResults, r => r.Message.Contains("@id", StringComparison.OrdinalIgnoreCase));
        }
    }
}
