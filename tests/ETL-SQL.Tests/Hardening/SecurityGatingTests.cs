using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Parser;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class SecurityGatingTests
    {
        [Fact]
        public async Task SpillSecurityRule_EnforcesRecursionLimit()
        {
            var rule = new SpillSecurityRule();

            // Build a deeply nested structure exceeding depth 50
            Statement currentBlock = new BlockStatement(new List<Statement> { new PrintStatement(new List<Expression> { new LiteralExpression("Deep", TokenType.STRING) }) });
            for (int i = 0; i <= 51; i++) // Create 52 levels of nesting
            {
                currentBlock = new BlockStatement(new List<Statement> { currentBlock });
            }

            var script = new Script();
            script.Statements.Add(currentBlock);

            var contextMock = new Mock<ILintContext>();

            var ex = await Assert.ThrowsAsync<ETL_SQL.Services.SecurityException>(() => rule.AnalyzeAsync(script, contextMock.Object));
            Assert.Contains("maximum allowed security depth (50)", ex.Message);
        }

        [Fact]
        [Trait("Category", "Smoke.Security")]
        public async Task CredentialLeakRule_DetectsObfuscatedLeak()
        {
            var rule = new CredentialLeakRule();

            // Attempting to obfuscate credential leakage
            string code = @"
            DECLARE @my_secret ENCRYPTED = 'xyz';
            
            -- String concatenation to avoid straight search
            PRINT 'The pass is: ' + @my_secret;

            -- Function usage
            PRINT SUBSTRING(@my_secret, 1, 5);

            -- Multiple concatenation 
            PRINT 'a' + 'b' + @my_secret + 'c';
            ";

            var lexer = new Lexer(code);
            var parser = new Parser(lexer.Tokenize());
            var script = parser.Parse();

            var contextMock = new Mock<ILintContext>();
            var results = (await rule.AnalyzeAsync(script, contextMock.Object)).ToList();

            // Should catch all 3 print statements leaking the encrypted var
            Assert.Equal(3, results.Count);
            foreach (var result in results)
            {
                Assert.Contains("potential credential leak", result.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
