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
            
            return eval;
        }

        [Fact]
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
            
            var scriptOn = TestHelpers.Parse("SET SHOW_PASSWORD ON;");
            await eval.Evaluate(scriptOn);
            Assert.True(eval.ShowPassword);
            
            var scriptOff = TestHelpers.Parse("SET SHOW_PASSWORD OFF;");
            await eval.Evaluate(scriptOff);
            Assert.False(eval.ShowPassword);
        }

        [Fact]
        public void TestUsePassword_ToSql_Masking()
        {
            var stmt = new UsePasswordStatement("my_pass");
            
            Assert.Equal("USE PASSWORD = '********';", stmt.ToSql(true));
            Assert.Equal("USE PASSWORD = 'my_pass';", stmt.ToSql(false));
        }

        [Fact]
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
    }
}
