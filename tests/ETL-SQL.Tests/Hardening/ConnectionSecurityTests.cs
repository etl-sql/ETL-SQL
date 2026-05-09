using Xunit;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Parser;
using ETL_SQL.Services;
using ETL_SQL.Common;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ETL_SQL.Tests.Hardening
{
    public class ConnectionSecurityTests
    {
        [Fact]
        public async Task ConnectionEncryptionRule_Flags_Plaintext_ConnectionString()
        {
            var sql = "CREATE CONNECTION c ON MSSQL('Server=.;Database=DB;User=sa;Password=secret;');";
            var script = Parse(sql);
            var rule = new ConnectionEncryptionRule();
            var results = await rule.AnalyzeAsync(script, new TestLintContext());

            Assert.Single(results);
            Assert.Equal("SEC-PLAIN-CONN", results.First().Code);
            Assert.Contains("plaintext connection string", results.First().Message);
        }

        [Fact]
        public async Task ConnectionEncryptionRule_Flags_Plaintext_PasswordOption()
        {
            var sql = "CREATE CONNECTION c ON MSSQL() WITH(PASSWORD='secret');";
            var script = Parse(sql);
            var rule = new ConnectionEncryptionRule();
            var results = await rule.AnalyzeAsync(script, new TestLintContext());

            Assert.Single(results);
            Assert.Equal("SEC-PLAIN-CONN", results.First().Code);
            Assert.Contains("plaintext password", results.First().Message);
        }

        [Fact]
        public async Task ConnectionEncryptionRule_Ignores_Encrypted_Credentials()
        {
            var sql = @"
                CREATE CONNECTION c1 ON MSSQL('ENC:abc123...');
                CREATE CONNECTION c2 ON MSSQL() WITH(PASSWORD='ENC:abc123...');
            ";
            var script = Parse(sql);
            var rule = new ConnectionEncryptionRule();
            var results = await rule.AnalyzeAsync(script, new TestLintContext());

            Assert.DoesNotContain(results, r => r.Code == "SEC-PLAIN-CONN");
        }

        [Fact]
        public void SecurityService_EncryptScript_Transforms_Plaintext_Target()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = "CREATE CONNECTION c ON MSSQL('Server=.;Password=secret;');";
            var encrypted = service.EncryptScript(sql, "master");

            Assert.Contains("ENC:", encrypted);
            Assert.DoesNotContain("Password=secret", encrypted);
        }

        [Fact]
        public void SecurityService_EncryptScript_Transforms_Plaintext_PasswordOption()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = "CREATE CONNECTION c ON MSSQL() WITH(SERVER='.', PASSWORD='secret');";
            var encrypted = service.EncryptScript(sql, "master");

            Assert.Contains("PASSWORD='ENC:", encrypted);
            Assert.DoesNotContain("PASSWORD='secret'", encrypted);
            Assert.Contains("SERVER='.'", encrypted);
        }

        [Fact]
        public void SecurityService_EncryptScript_Handles_Mixed_And_Multiple()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = @"
                CREATE CONNECTION c1 ON MSSQL('Password=p1');
                CREATE CONNECTION c2 ON POSTGRES() WITH(PASSWORD='p2');
                CREATE CONNECTION c3 ON FLATFILE('d.csv') WITH(ENCRYPT=OFF, PASSWORD='p3');
            ";
            var encrypted = service.EncryptScript(sql, "master");

            Assert.Contains("c1 ON MSSQL('ENC:", encrypted);
            Assert.Contains("c2 ON POSTGRES() WITH(PASSWORD='ENC:", encrypted);
            Assert.Contains("c3 ON FLATFILE('d.csv') WITH(ENCRYPT=OFF, PASSWORD='p3')", encrypted); // ENCRYPT=OFF should skip
        }

        [Fact]
        public void SecurityService_NeedsEncryption_Identifies_Plaintext()
        {
            var service = new SecurityService(NullLogger.Instance);
            
            Assert.True(service.NeedsEncryption("CREATE CONNECTION c ON MSSQL('Pwd=p');"));
            Assert.True(service.NeedsEncryption("CREATE CONNECTION c ON MSSQL() WITH(PASSWORD='p');"));
            Assert.False(service.NeedsEncryption("CREATE CONNECTION c ON MSSQL('ENC:p');"));
            Assert.False(service.NeedsEncryption("CREATE CONNECTION c ON MSSQL() WITH(PASSWORD='ENC:p');"));
        }

        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new ETL_SQL.Core.Parser.Parser(tokens, sql);
            return parser.Parse();
        }

        private class TestLintContext : ILintContext
        {
            public IMetadataProvider? Metadata => null;
            public string DocumentUri => "test://file.sql";
        }
    }
}
