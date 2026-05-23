using Xunit;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Connectors.MockDb;

namespace ETL_SQL.Tests.Statements.Statements
{
    public class SecurityStatementTests
    {
        private Evaluator CreateEvaluator()
        {
            // Use the shared service provider from TestSetup
            var eval = Program.ServiceProvider.GetRequiredService<Evaluator>();
            
            // Reset state between tests (SecurityService is a singleton)
            eval.SecurityService.MasterPassword = null;
            eval.ScriptPassword = null;
            eval.AllowPlaintextSecrets = false;
            eval.NoSaveSensitive = false;
            eval.NoSaveConnection = false;
            eval.ConnectionEncryption = false;
            
            return eval;
        }

        [Fact]
        [Trait("Category", "Smoke.Security")]
        public async Task TestUsePassword_SetsContext()
        {
            var eval = CreateEvaluator();
            var sql = "USE PASSWORD = 'secret_key';";
            
            var script = TestHelpers.Parse(sql);
            await eval.Evaluate(script);
            
            Assert.Equal("secret_key", eval.ScriptPassword);
        }

        [Fact]
        public async Task TestSetShowPassword_SetsContext()
        {
            var eval = CreateEvaluator();

            var scriptOn = TestHelpers.Parse("SET SHOW_PASSWORD = ON;");
            await eval.Evaluate(scriptOn);
            Assert.True(eval.ShowPassword);

            var scriptOff = TestHelpers.Parse("SET SHOW_PASSWORD OFF;");
            await eval.Evaluate(scriptOff);
            Assert.False(eval.ShowPassword);
        }

        [Fact]
        public async Task TestSetShowSecrets_SetsContext()
        {
            var eval = CreateEvaluator();

            var scriptOn = TestHelpers.Parse("SET SHOW_SECRETS = ON;");
            await eval.Evaluate(scriptOn);
            Assert.True(eval.ShowPassword);

            var scriptOff = TestHelpers.Parse("SET SHOW_SECRETS OFF;");
            await eval.Evaluate(scriptOff);
            Assert.False(eval.ShowPassword);
        }

        [Fact]
        public async Task TestSetAllowPlaintextSecrets_SetsContext()
        {
            var eval = CreateEvaluator();

            var scriptOn = TestHelpers.Parse("SET ALLOW_PLAINTEXT_SECRETS = ON;");
            await eval.Evaluate(scriptOn);
            Assert.True(eval.AllowPlaintextSecrets);

            var scriptOff = TestHelpers.Parse("SET ALLOW_PLAINTEXT_SECRETS OFF;");
            await eval.Evaluate(scriptOff);
            Assert.False(eval.AllowPlaintextSecrets);
        }

        [Fact]
        public async Task TestSavePolicySettings_SetContext()
        {
            var eval = CreateEvaluator();

            await eval.Evaluate(TestHelpers.Parse(@"
                SET NO_SAVE_SENSITIVE = ON;
                SET NO_SAVE_CONNECTION = ON;
                SET CONNECTION_ENCRYPTION = ON;
            "));

            Assert.True(eval.NoSaveSensitive);
            Assert.True(eval.NoSaveConnection);
            Assert.True(eval.ConnectionEncryption);

            await eval.Evaluate(TestHelpers.Parse(@"
                SET NO_SAVE_SENSITIVE OFF;
                SET NO_SAVE_CONNECTION OFF;
                SET CONNECTION_ENCRYPTION OFF;
            "));

            Assert.False(eval.NoSaveSensitive);
            Assert.False(eval.NoSaveConnection);
            Assert.False(eval.ConnectionEncryption);
        }

        [Fact]
        public void TestUsePassword_ToSql_Masking()
        {
            var stmt = new UsePasswordStatement("my_pass");
            
            Assert.Equal("USE PASSWORD = '********';", stmt.ToSql(true));
            Assert.Equal("USE PASSWORD = 'my_pass';", stmt.ToSql(false));
        }

        [Fact]
        [Trait("Category", "Smoke.Security")]
        public async Task TestCreateConnection_DecryptsWithScriptPassword()
        {
            var eval = CreateEvaluator();
            var plain = "Server=myServer;Database=myDb;";
            var pass = "script_pass";
            var enc = CryptoUtils.Encrypt(plain, pass);
            
            // We need to ensure MOCKDB is registered, which it should be in TestSetup/DependencyInjectionSetup
            var sql = $@"
                USE PASSWORD = '{pass}';
                CREATE CONNECTION test_security_conn ON MOCKDB('{enc}');
            ";
            
            var script = TestHelpers.Parse(sql);
            await eval.Evaluate(script);
            
            Assert.True(eval.Connections.ContainsKey("test_security_conn"));
            var ds = eval.Connections["test_security_conn"];
            Assert.NotNull(ds);
            
            // Cast to MockSqlDataSource to verify the decrypted connection string
            var mockDs = Assert.IsType<MockSqlDataSource>(ds);
            Assert.Equal(plain, mockDs.ConnectionString); 
        }

        [Fact]
        public async Task TestCreateConnection_FailsWithWrongPassword()
        {
            var eval = CreateEvaluator();
            var plain = "Server=myServer;Database=myDb;";
            var enc = CryptoUtils.Encrypt(plain, "right_pass");
            
            var sql = $@"
                USE PASSWORD = 'wrong_pass';
                CREATE CONNECTION test_fail_conn ON MOCKDB('{enc}');
            ";
            
            var script = TestHelpers.Parse(sql);
            var ex = await Assert.ThrowsAsync<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() => eval.Evaluate(script));
            Assert.Contains("Failed to decrypt", ex.Message);
        }

        [Fact]
        [Trait("Category", "Smoke.Security")]
        public async Task ResolvePath_StripsWindowsCopyAsPathQuotes()
        {
            // Windows "Copy as path" wraps paths in double-quotes: "C:\tmp\file.csv"
            // When pasted into a connection string the quotes must be silently stripped.
            var eval = ETL_SQL.App.DependencyInjectionSetup.BuildServiceProvider()
                           .GetRequiredService<Evaluator>();
            eval.SecurityService.IsTestMode = true;

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"qp_{System.Guid.NewGuid():N}.csv");
            string quotedPath = $"\"{path}\""; // simulate Windows Copy-as-Path

            // ResolvePath should strip the surrounding quotes and return the bare path
            string resolved = eval.ResolvePath(quotedPath);
            Assert.Equal(path, resolved);
        }

        [Fact]
        public void ResolvePath_UsesWorkingDirectory_WhenCurrentScriptPathIsOrchestratorBundleUri()
        {
            var eval = ETL_SQL.App.DependencyInjectionSetup.BuildServiceProvider()
                           .GetRequiredService<Evaluator>();
            eval.SecurityService.IsTestMode = true;

            var workingDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"orch_{System.Guid.NewGuid():N}");
            eval.WorkingDirectory = workingDirectory;
            eval.CurrentScriptPath = "orch://manufacturing-integration@1/generate_manufacturing.etlsql";

            var resolved = eval.ResolvePath(System.IO.Path.Combine("relative", "output.csv"));

            Assert.Equal(
                System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDirectory, "relative", "output.csv")),
                resolved);
        }
    }
}
