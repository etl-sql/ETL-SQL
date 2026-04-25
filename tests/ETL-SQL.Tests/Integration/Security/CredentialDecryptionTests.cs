using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Tests.Integration.Security
{
    public class CredentialDecryptionTests
    {
        private Evaluator CreateEvaluator()
        {
            return DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        }

        private async Task Execute(Evaluator eval, string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var script = parser.Parse();
            
            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var errors = string.Join("; ", script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
                throw new Exception($"Script parsing failed: {errors}");
            }
            
            await eval.Evaluate(script);
        }

        [Fact]
        public async Task Test_CreateConnection_DecryptsOptions()
        {
            var eval = CreateEvaluator();
            var context = eval;
            
            // 1. Set password
            await Execute(eval, "USE PASSWORD = 'test-password';");
            
            // 2. Encrypt a value
            string rawValue = "secret-api-key";
            string encrypted = ETL_SQL.Common.CryptoUtils.Encrypt(rawValue, "test-password");
            
            // 3. Create connection using the encrypted value in options (SENSITIVE variable)
            string sql = $@"
                DECLARE @apiKey SENSITIVE = '{encrypted}';
                CREATE CONNECTION MySecureApi ON MOCKDB() WITH (API_KEY = @apiKey);
            ";
            
            await Execute(eval, sql);
            
            // 4. Verify the decrypted value reached the data source
            Assert.True(context.Connections.TryGetValue("MySecureApi", out var ds));
            
            if (ds.Options["API_KEY"] != rawValue)
            {
                throw new Exception($"Decryption failed for SENSITIVE variable. Expected: {rawValue}, Actual: {ds.Options["API_KEY"]}.");
            }
        }

        [Fact]
        public async Task Test_AlterConnection_DecryptsOptions()
        {
            var eval = CreateEvaluator();
            var context = eval;
            
            await Execute(eval, "USE PASSWORD = 'test-password';");
            
            string encrypted = ETL_SQL.Common.CryptoUtils.Encrypt("new-secret", "test-password");
            
            await Execute(eval, "CREATE CONNECTION MyApi ON MOCKDB() WITH (KEY = 'old');");
            await Execute(eval, $"ALTER CONNECTION MyApi WITH (KEY = '{encrypted}');");
            
            Assert.True(context.Connections.TryGetValue("MyApi", out var ds));
            Assert.Equal("new-secret", ds.Options["KEY"]);
        }
        
        [Fact]
        public async Task Test_BulkInsert_DecryptsOptions()
        {
            var eval = CreateEvaluator();

            await Execute(eval, "CREATE TABLE #Target (ID INT, Name STRING);");

            string encrypted = ETL_SQL.Common.CryptoUtils.Encrypt(",", "test-password");

            // Attempt Bulk Insert WITHOUT password — literal ENC: value without a script password set
            // should fail because there is no key to decrypt with
            try
            {
                await Execute(eval, $"BULK INSERT #Target FROM 'c:\\data.csv' WITH (FIELDTERMINATOR = '{encrypted}');");
                throw new Exception("NO EXCEPTION THROWN BY EXECUTE");
            }
            catch (ExecutionException ex)
            {
                Assert.True(ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("decrypt", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex2)
            {
                throw new Exception($"WRONG EXCEPTION TYPE: {ex2.GetType().Name} - {ex2.Message}");
            }
        }

        [Fact]
        public async Task Test_EncryptFile_DecryptsSensitivePassword()
        {
            var eval = CreateEvaluator();
            eval.SecurityService.IsTestMode = true;
            string tmp  = System.IO.Path.GetTempPath();
            string src  = System.IO.Path.Combine(tmp, $"ef_src_{Guid.NewGuid():N}.csv");
            string enc  = System.IO.Path.Combine(tmp, $"ef_enc_{Guid.NewGuid():N}.csv");
            string dec  = System.IO.Path.Combine(tmp, $"ef_dec_{Guid.NewGuid():N}.csv");
            try
            {
                System.IO.File.WriteAllText(src, "hello-secret");
                await Execute(eval, "USE PASSWORD = 'file-pwd';");

                string encryptedPwd = ETL_SQL.Common.CryptoUtils.Encrypt("file-pwd", "file-pwd");
                await Execute(eval, $@"
                    DECLARE @pwd SENSITIVE = '{encryptedPwd}';
                    ENCRYPT_FILE '{src.Replace("\\", "\\\\")}' TO '{enc.Replace("\\", "\\\\")}' PASSWORD @pwd;
                ");

                Assert.True(System.IO.File.Exists(enc), "Encrypted file not created");

                await Execute(eval, $@"
                    DECLARE @pwd2 SENSITIVE = '{encryptedPwd}';
                    DECRYPT_FILE '{enc.Replace("\\", "\\\\")}' TO '{dec.Replace("\\", "\\\\")}' PASSWORD @pwd2;
                ");

                Assert.Equal("hello-secret", System.IO.File.ReadAllText(dec));
            }
            finally
            {
                foreach (var f in new[] { src, enc, dec })
                    try { if (System.IO.File.Exists(f)) System.IO.File.Delete(f); } catch { }
            }
        }
    }
}
