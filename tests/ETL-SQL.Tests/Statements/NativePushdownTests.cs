using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Linting.Rules;
using Moq;

namespace ETL_SQL.Tests.Statements
{
    public class NativePushdownTests
    {
        [Fact]
        public async Task Linter_Should_Flag_Missing_End_In_Execute()
        {
            var sql = @"
EXECUTE [MockDB] BEGIN
    SELECT * FROM TestTable
"; // Missing END

            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
            var script = parser.Parse();

            var linter = new Linter();
            linter.AddRule(new BeginEndBalanceRule());

            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal("BeginEndBalance", results[0].RuleName);
            Assert.Equal(LintSeverity.Error, results[0].Severity);
            Assert.Contains("Mismatched BEGIN and END", results[0].Message);
        }

        [Fact]
        public void Linter_Should_Flag_Missing_Begin_In_Execute()
        {
            var sql = @"
EXECUTE [MockDB] 
    SELECT * FROM TestTable
END;
"; // Missing BEGIN before the block
            
            // This is actually a parser SyntaxException because EXECUTE <conn> must be followed by BEGIN or a string literal.
            // But if it's treated as a generic block error:
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
            var script = parser.Parse();

            // Parser produces a syntax error when a bare END is encountered without a matching BEGIN
            Assert.Contains(script.Diagnostics, d => d.Message.Contains("Unexpected token END") || d.Message.Contains("Expected BEGIN"));
        }

        [Fact]
        public async Task Linter_Should_Pass_Balanced_Execute()
        {
            var sql = @"
EXECUTE [MockDB] BEGIN
    SELECT * FROM TestTable;
END;
"; // Balanced

            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
            var script = parser.Parse();

            var linter = new Linter();
            linter.AddRule(new BeginEndBalanceRule());

            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public void Parser_Should_Parse_Execute_With_Parameters()
        {
            var sql = @"
DECLARE @id = 5;
DECLARE @name = 'Test';
EXECUTE [MockDB] WITH (@id, @name) BEGIN
    SELECT * FROM Users WHERE Id = ? AND Name = ?;
END;
";
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
            var script = parser.Parse();

            var execStmt = script.Statements.OfType<ExecutePushdownStatement>().FirstOrDefault();
            Assert.NotNull(execStmt);
            Assert.False(execStmt.HasUnbalancedBlocks);
            Assert.Equal(2, execStmt.Parameters.Count);
            
            var p1 = Assert.IsType<VariableExpression>(execStmt.Parameters[0]);
            Assert.Equal("@id", p1.Name);
            
            var p2 = Assert.IsType<VariableExpression>(execStmt.Parameters[1]);
            Assert.Equal("@name", p2.Name);
            
            Assert.Contains("SELECT * FROM Users WHERE Id = ? AND Name = ?", execStmt.SqlText);
        }
    }
}
