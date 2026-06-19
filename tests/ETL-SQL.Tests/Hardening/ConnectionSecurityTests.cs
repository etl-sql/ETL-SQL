using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.Hardening
{
    public class ConnectionSecurityTests
    {
        [Fact]
        public async Task ConnectionEncryptionRule_Flags_Plaintext_ConnectionString()
        {
            var sql = "CREATE CONNECTION c AS MSSQL('Server=.;Database=DB;User=sa;Password=secret;');";
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
            var sql = "CREATE CONNECTION c AS MSSQL(PASSWORD='secret');";
            var script = Parse(sql);
            var rule = new ConnectionEncryptionRule();
            var results = await rule.AnalyzeAsync(script, new TestLintContext());

            Assert.Single(results);
            Assert.Equal("SEC-PLAIN-CONN", results.First().Code);
            Assert.Contains("plaintext password", results.First().Message);
        }

        [Fact]
        public async Task ConnectionEncryptionRule_Attaches_GovernancePolicyDecision_To_Plaintext_Findings()
        {
            var sql = "CREATE CONNECTION c AS MSSQL(PASSWORD='secret');";
            var script = Parse(sql);
            var rule = new ConnectionEncryptionRule();
            var result = Assert.Single(await rule.AnalyzeAsync(script, new TestLintContext()));

            Assert.NotNull(result.PolicyDecision);
            Assert.Equal("Engine:AllowPlaintextSecrets", result.PolicyDecision.PolicyKey);
            Assert.Equal(GovernancePolicyClassification.Forbidden, result.PolicyDecision.Classification);
            Assert.True(result.PolicyDecision.IsViolation);
            Assert.Contains("connector option PASSWORD", result.PolicyDecision.Action);
        }

        [Fact]
        public async Task ConnectionEncryptionRule_UsesParsedAst_NotCommentText()
        {
            var sql = "-- CREATE CONNECTION c AS MSSQL(PASSWORD='secret');";
            var script = Parse(sql);
            var rule = new ConnectionEncryptionRule();
            var results = await rule.AnalyzeAsync(script, new TestLintContext());

            Assert.Empty(results);
        }

        [Fact]
        public async Task ConnectionEncryptionRule_Flags_Plaintext_ApiKeyOption()
        {
            var sql = "CREATE CONNECTION c AS MSSQL(API_KEY='secret');";
            var script = Parse(sql);
            var rule = new ConnectionEncryptionRule();
            var results = await rule.AnalyzeAsync(script, new TestLintContext());

            Assert.Single(results);
            Assert.Equal("SEC-PLAIN-CONN", results.First().Code);
            Assert.Contains("plaintext password or credential", results.First().Message);
        }

        [Fact]
        public async Task ConnectionEncryptionRule_Flags_Plaintext_ApiKeyOptionNoUnderscore()
        {
            var sql = "CREATE CONNECTION c AS MSSQL(APIKEY='secret');";
            var script = Parse(sql);
            var rule = new ConnectionEncryptionRule();
            var results = await rule.AnalyzeAsync(script, new TestLintContext());

            Assert.Single(results);
            Assert.Equal("SEC-PLAIN-CONN", results.First().Code);
            Assert.Contains("plaintext password or credential", results.First().Message);
        }

        [Fact]
        public async Task ConnectionEncryptionRule_Ignores_Encrypted_Credentials()
        {
            var sql = @"
                CREATE CONNECTION c1 AS MSSQL('ENC:abc123...');
                CREATE CONNECTION c2 AS MSSQL(PASSWORD='ENC:abc123...');
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
            var sql = "CREATE CONNECTION c AS MSSQL('Server=.;Password=secret;');";
            var encrypted = service.EncryptScript(sql, "master");

            Assert.Contains("ENC:", encrypted);
            Assert.DoesNotContain("Password=secret", encrypted);
        }

        [Fact]
        public void SecurityService_EncryptScript_Transforms_Plaintext_PasswordOption()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = "CREATE CONNECTION c AS MSSQL(SERVER='.', PASSWORD='secret');";
            var encrypted = service.EncryptScript(sql, "master");

            Assert.Contains("PASSWORD='ENC:", encrypted);
            Assert.DoesNotContain("PASSWORD='secret'", encrypted);
            Assert.Contains("SERVER='.'", encrypted);
        }

        [Fact]
        public void SecurityService_EncryptScript_Transforms_Plaintext_ApiKeyOptions()
        {
            var service = new SecurityService(NullLogger.Instance);

            var sql1 = "CREATE CONNECTION c AS MSSQL(SERVER='.', API_KEY='secret');";
            var encrypted1 = service.EncryptScript(sql1, "master");
            Assert.Contains("API_KEY='ENC:", encrypted1);
            Assert.DoesNotContain("API_KEY='secret'", encrypted1);

            var sql2 = "CREATE CONNECTION c AS MSSQL(SERVER='.', APIKEY='secret');";
            var encrypted2 = service.EncryptScript(sql2, "master");
            Assert.Contains("APIKEY='ENC:", encrypted2);
            Assert.DoesNotContain("APIKEY='secret'", encrypted2);
        }

        [Fact]
        public void SecurityService_EncryptScript_Handles_Mixed_And_Multiple()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = @"
                CREATE CONNECTION c1 AS MSSQL('Password=p1');
                CREATE CONNECTION c2 AS POSTGRES(PASSWORD='p2');
                CREATE CONNECTION c3 AS FLATFILE('d.csv', ENCRYPT=OFF, PASSWORD='p3');
            ";
            var encrypted = service.EncryptScript(sql, "master");

            Assert.Contains("c1 AS MSSQL('ENC:", encrypted);
            Assert.Contains("c2 AS POSTGRES(PASSWORD='ENC:", encrypted);
            Assert.Contains("c3 AS FLATFILE('d.csv', ENCRYPT=OFF, PASSWORD='p3')", encrypted); // ENCRYPT=OFF should skip
        }

        [Fact]
        public void SecurityService_NeedsEncryption_Identifies_Plaintext()
        {
            var service = new SecurityService(NullLogger.Instance);

            Assert.True(service.NeedsEncryption("CREATE CONNECTION c AS MSSQL('Pwd=p');"));
            Assert.True(service.NeedsEncryption("CREATE CONNECTION c AS MSSQL(PASSWORD='p');"));
            Assert.False(service.NeedsEncryption("CREATE CONNECTION c AS MSSQL('ENC:p');"));
            Assert.False(service.NeedsEncryption("CREATE CONNECTION c AS MSSQL(PASSWORD='ENC:p');"));
        }

        [Fact]
        public void SecurityService_SecureScriptForSave_Rewrites_UsePasswordLiteral()
        {
            var service = new SecurityService(NullLogger.Instance);
            var secured = service.SecureScriptForSave("USE PASSWORD = 'dev-secret';\nPRINT 'ok';", "");

            Assert.Contains("USE PASSWORD PROMPT;", secured);
            Assert.DoesNotContain("dev-secret", secured);
        }

        [Fact]
        public void SecurityService_SecureScriptForSave_Preserves_UsePasswordLiteral_WhenAllowed()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = "SET ALLOW_PLAINTEXT_SECRETS = ON;\nUSE PASSWORD = 'dev-secret';";
            var secured = service.SecureScriptForSave(sql, "master");

            Assert.Contains("USE PASSWORD = 'dev-secret';", secured);
        }

        [Fact]
        public void SecurityService_AllowsPlaintextSecrets_UsesLastSetting()
        {
            var service = new SecurityService(NullLogger.Instance);

            Assert.False(service.AllowsPlaintextSecrets("SET ALLOW_PLAINTEXT_SECRETS = ON;\nSET ALLOW_PLAINTEXT_SECRETS OFF;"));
            Assert.True(service.AllowsPlaintextSecrets("SET ALLOW_PLAINTEXT_SECRETS OFF;\nSET ALLOW_PLAINTEXT_SECRETS = ON;"));
        }

        [Fact]
        public void SecurityService_SecureScriptForSave_NoSaveSensitive_ScrubsSecrets()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = @"
                SET NO_SAVE_SENSITIVE = ON;
                USE PASSWORD = 'dev-secret';
                DECLARE @token SENSITIVE = 'abc';
                CREATE CONNECTION c AS MSSQL('Server=.;Password=pw;', USERNAME='sa', PASSWORD='pw2', API_KEY='key');
            ";

            var secured = service.SecureScriptForSave(sql, "");

            Assert.Contains("USE PASSWORD PROMPT;", secured);
            Assert.Contains("DECLARE @token SENSITIVE = '<secret>'", secured);
            Assert.Contains("Password=<secret>", secured);
            Assert.Contains("PASSWORD='<secret>'", secured);
            Assert.Contains("API_KEY='<secret>'", secured);
            Assert.DoesNotContain("dev-secret", secured);
            Assert.DoesNotContain("pw2", secured);
            Assert.False(service.RequiresSavePassword(sql));
        }

        [Fact]
        public void SecurityService_SecureScriptForSave_NoSaveConnection_ScrubsConnectionDetails()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = "SET NO_SAVE_CONNECTION = ON;\nCREATE CONNECTION c AS POSTGRES('Host=db;Username=u;Password=p;', HOST='db', USERNAME='u', DATABASE='d', PASSWORD='p');";

            var secured = service.SecureScriptForSave(sql, "");

            Assert.Contains("POSTGRES('<connection>',", secured);
            Assert.Contains("HOST='<placeholder>'", secured);
            Assert.Contains("USERNAME='<placeholder>'", secured);
            Assert.Contains("DATABASE='<placeholder>'", secured);
            Assert.Contains("PASSWORD='<placeholder>'", secured);
            Assert.DoesNotContain("Host=db", secured);
            Assert.DoesNotContain("USERNAME='u'", secured);
        }

        [Fact]
        public void SecurityService_SecureScriptForSave_ConnectionEncryption_EncryptsFullConnection()
        {
            var service = new SecurityService(NullLogger.Instance);
            var sql = "SET CONNECTION_ENCRYPTION = ON;\nCREATE CONNECTION c AS POSTGRES('Host=db;Username=u;Password=p;', HOST='db', USERNAME='u', DATABASE='d', PASSWORD='p');";

            var secured = service.SecureScriptForSave(sql, "master");

            Assert.Contains("POSTGRES('ENC:", secured);
            Assert.Contains("HOST='ENC:", secured);
            Assert.Contains("USERNAME='ENC:", secured);
            Assert.Contains("DATABASE='ENC:", secured);
            Assert.Contains("PASSWORD='ENC:", secured);
            Assert.DoesNotContain("Host=db", secured);
            Assert.DoesNotContain("USERNAME='u'", secured);
        }

        [Fact]
        public void SavePolicyDefaults_CanBeReadFromConfiguration()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Engine:AllowPlaintextSecrets"] = "true",
                    ["Engine:NoSaveSensitive"] = "true",
                    ["Engine:NoSaveConnection"] = "true",
                    ["Engine:ConnectionEncryption"] = "true"
                })
                .Build();

            Assert.True(DefaultThresholds.AllowPlaintextSecrets(config));
            Assert.True(DefaultThresholds.NoSaveSensitive(config));
            Assert.True(DefaultThresholds.NoSaveConnection(config));
            Assert.True(DefaultThresholds.ConnectionEncryption(config));
        }

        [Fact]
        public void SecurityService_SavePolicyDefaults_CanBeReadFromConfiguration()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Engine:NoSaveSensitive"] = "true"
                })
                .Build();
            var service = new SecurityService(NullLogger.Instance);

            service.UpdateFromConfiguration(config);

            Assert.True(service.NoSaveSensitiveEnabled(""));
            Assert.Contains("<secret>", service.SecureScriptForSave("CREATE CONNECTION c AS MSSQL(PASSWORD='pw');", ""));
        }

        [Fact]
        public void SecurityService_GetLastOnOffSetting_IgnoresComments()
        {
            var service = new SecurityService(NullLogger.Instance);

            Assert.False(service.AllowsPlaintextSecrets("-- SET ALLOW_PLAINTEXT_SECRETS = ON;"));
            Assert.False(service.AllowsPlaintextSecrets("/* SET ALLOW_PLAINTEXT_SECRETS = ON; */"));

            Assert.True(service.AllowsPlaintextSecrets("SET ALLOW_PLAINTEXT_SECRETS = ON;\n-- SET ALLOW_PLAINTEXT_SECRETS = OFF;"));
            Assert.False(service.AllowsPlaintextSecrets("SET ALLOW_PLAINTEXT_SECRETS = OFF;\n-- SET ALLOW_PLAINTEXT_SECRETS = ON;"));
        }

        [Fact]
        public void SecurityService_ExtractLiteralUsePassword_IgnoresComments()
        {
            var service = new SecurityService(NullLogger.Instance);

            Assert.Null(service.ExtractLiteralUsePassword("-- USE PASSWORD = 'secret-pw';"));
            Assert.Null(service.ExtractLiteralUsePassword("/* USE PASSWORD = 'secret-pw'; */"));

            Assert.Equal("secret-pw", service.ExtractLiteralUsePassword("USE PASSWORD = 'secret-pw';"));
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
