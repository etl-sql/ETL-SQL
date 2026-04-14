using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Linting;
using ETL_SQL.Core.Linting.Rules;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests
{
    public class CredentialLeakRuleTests
    {
        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            return parser.Parse();
        }

        [Fact]
        public async Task TestPrintLeak_WithPasswordVariable_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new CredentialLeakRule());

            var sql = @"
                DECLARE @password STRING = 'secret123';
                PRINT @password;
            ";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("PRINT", results[0].Message);
            Assert.Contains("@password", results[0].Message);
        }

        [Fact]
        public async Task TestEmailLeak_WithEncryptedVariable_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new CredentialLeakRule());

            var sql = @"
                DECLARE @token ENCRYPTED = 'ENC:abc...';
                SEND EMAIL TO 'admin@example.com' FROM 'app@example.com' SUBJECT 'Key leak' BODY 'The token is ' + @token;
            ";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("SEND EMAIL body", results[0].Message);
            Assert.Contains("@token", results[0].Message);
        }

        [Fact]
        public async Task TestRaiserrorLeak_WithSensitiveKey_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new CredentialLeakRule());

            var sql = @"
                DECLARE @apiKey STRING = 'xyz123';
                RAISERROR ('Invalid key: %s', 16, 1, @apiKey);
            ";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Contains("RAISERROR parameter", results[0].Message);
            Assert.Contains("@apiKey", results[0].Message);
        }

        [Fact]
        public async Task TestExecLeak_WithConnectionString_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new CredentialLeakRule());

            var sql = @"
                DECLARE @connString STRING = 'Server=myServer;User Id=myUser;Password=myPassword;';
                EXEC (@connString);
            ";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Contains("EXECUTE/dynamic SQL", results[0].Message);
            Assert.Contains("@connString", results[0].Message);
        }

        [Fact]
        public async Task TestNoLeak_WithNormalVariables_NoWarning()
        {
            var linter = new Linter();
            linter.AddRule(new CredentialLeakRule());

            var sql = @"
                DECLARE @userName STRING = 'chuck';
                DECLARE @age INT = 30;
                PRINT 'User: ' + @userName + ', Age: ' + CAST(@age AS STRING);
                SELECT * FROM MyTable WHERE Name = @userName;
            ";

            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task TestScoping_VariableRedeclaration_RespectsInnerScope()
        {
            var linter = new Linter();
            linter.AddRule(new CredentialLeakRule());

            var sql = @"
                DECLARE @publicInfo STRING = 'public-data';
                PRINT @publicInfo; 
                
                IF 1=1
                BEGIN
                    DECLARE @secretToken STRING = 'private-secret';
                    PRINT @secretToken; 
                END
            ";
            
            var script = Parse(sql);
            var results = (await linter.AnalyzeAsync(script, new DefaultLintContext())).ToList();

            // Only the inner PRINT @secretToken should trigger
            Assert.Single(results);
            Assert.Contains("@secretToken", results[0].Message);
        }
    }
}
