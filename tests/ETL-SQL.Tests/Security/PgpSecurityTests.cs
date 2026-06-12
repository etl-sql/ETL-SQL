using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Security
{
    public class PgpSecurityTests : IDisposable
    {
        private readonly string _testDir;

        public PgpSecurityTests()
        {
            _testDir = Path.Combine(Directory.GetCurrentDirectory(), "PgpTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        private async Task RunScriptAsync(string sql)
        {
            var services = DependencyInjectionSetup.BuildServiceProvider();
            var evaluator = services.GetRequiredService<Evaluator>();

            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, sql);
            var script = parser.Parse();

            if (script.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var errors = string.Join("\n", script.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message));
                throw new Exception($"Parsing failed with errors:\n{errors}");
            }

            await evaluator.Evaluate(script);
        }

        [Fact]
        public async Task Test_CreatePgpKeyPair_And_EncryptDecrypt()
        {
            string keyDir = Path.Combine(_testDir, "keys");
            Directory.CreateDirectory(keyDir);
            string pubKey = Path.Combine(keyDir, "public.asc");
            string privKey = Path.Combine(keyDir, "private.asc");

            string sourceFile = Path.Combine(_testDir, "data.txt");
            string encryptedFile = Path.Combine(_testDir, "data.pgp");
            string decryptedFile = Path.Combine(_testDir, "data.dec.txt");

            File.WriteAllText(sourceFile, "This is a PGP secret message.");

            string keyDirEsc = keyDir.Replace("\\", "\\\\");
            string pubKeyEsc = pubKey.Replace("\\", "\\\\");
            string privKeyEsc = privKey.Replace("\\", "\\\\");
            string sourceFileEsc = sourceFile.Replace("\\", "\\\\");
            string encryptedFileEsc = encryptedFile.Replace("\\", "\\\\");
            string decryptedFileEsc = decryptedFile.Replace("\\", "\\\\");

            // 1. Create Key Pair
            // 2. Encrypt File using Public Key
            // 3. Decrypt File using Private Key and Passphrase
            string script = $@"
                CREATE PGP_KEY_PAIR '{keyDirEsc}' 
                    WITH (BITS = 2048, IDENTITY = 'Test Identity <test@example.com>', PASSPHRASE = 'TestPass123');
                
                ENCRYPT FILE '{sourceFileEsc}' TO '{encryptedFileEsc}' PGP_KEY '{pubKeyEsc}';
                
                DECRYPT FILE '{encryptedFileEsc}' TO '{decryptedFileEsc}' PGP_KEY '{privKeyEsc}' PASSWORD 'TestPass123';
            ";

            await RunScriptAsync(script);

            Assert.True(File.Exists(pubKey));
            Assert.True(File.Exists(privKey));
            Assert.True(File.Exists(encryptedFile));
            Assert.True(File.Exists(decryptedFile));

            string decryptedContent = File.ReadAllText(decryptedFile);
            Assert.Equal("This is a PGP secret message.", decryptedContent);
        }

        [Fact]
        public async Task Test_EncryptDecrypt_NoPassphrase()
        {
            string keyDir = Path.Combine(_testDir, "keys_no_pass");
            Directory.CreateDirectory(keyDir);
            string pubKey = Path.Combine(keyDir, "public.asc");
            string privKey = Path.Combine(keyDir, "private.asc");

            string sourceFile = Path.Combine(_testDir, "data_no_pass.txt");
            string encryptedFile = Path.Combine(_testDir, "data_no_pass.pgp");
            string decryptedFile = Path.Combine(_testDir, "data_no_pass.dec.txt");

            File.WriteAllText(sourceFile, "Message without passphrase.");

            string keyDirEsc = keyDir.Replace("\\", "\\\\");
            string pubKeyEsc = pubKey.Replace("\\", "\\\\");
            string privKeyEsc = privKey.Replace("\\", "\\\\");
            string sourceFileEsc = sourceFile.Replace("\\", "\\\\");
            string encryptedFileEsc = encryptedFile.Replace("\\", "\\\\");
            string decryptedFileEsc = decryptedFile.Replace("\\", "\\\\");

            string script = $@"
                CREATE PGP_KEY_PAIR '{keyDirEsc}' WITH (BITS = 2048);
                
                ENCRYPT FILE '{sourceFileEsc}' TO '{encryptedFileEsc}' PGP_KEY '{pubKeyEsc}';
                
                DECRYPT FILE '{encryptedFileEsc}' TO '{decryptedFileEsc}' PGP_KEY '{privKeyEsc}';
            ";

            await RunScriptAsync(script);

            Assert.Equal("Message without passphrase.", File.ReadAllText(decryptedFile));
        }
    }
}
