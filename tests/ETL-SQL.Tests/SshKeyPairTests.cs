using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using Xunit;
using ETL_SQL.Common;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Tests
{
    public class SshKeyPairTests : IDisposable
    {
        private readonly string _testDir;

        public SshKeyPairTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ETL_SQL_SshTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true);
        }

        private Script Parse(string sql)
        {
            return TestHelpers.Parse(sql);
        }

        [Fact]
        public async Task TestGenerateRsaKey()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string scriptDir = Path.Combine(_testDir, "rsa");
            string script = $"CREATE SSH_KEY_PAIR('{scriptDir.Replace("\\", "\\\\")}', 2048, 'RSA');";
            
            await evaluator.Evaluate(Parse(script));

            Assert.True(File.Exists(Path.Combine(scriptDir, "id_rsa")), "Private key not found");
            Assert.True(File.Exists(Path.Combine(scriptDir, "id_rsa.pub")), "Public key not found");
            
            string privateKey = File.ReadAllText(Path.Combine(scriptDir, "id_rsa"));
            Assert.Contains("BEGIN PRIVATE KEY", privateKey);
        }

        [Fact]
        public async Task TestGenerateEcdsaKey()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string scriptDir = Path.Combine(_testDir, "ecdsa");
            string script = $"CREATE SSH_KEY_PAIR('{scriptDir.Replace("\\", "\\\\")}', 256, 'ECDSA');";
            
            await evaluator.Evaluate(Parse(script));

            Assert.True(File.Exists(Path.Combine(scriptDir, "id_ecdsa_256")), "Private key not found");
            Assert.True(File.Exists(Path.Combine(scriptDir, "id_ecdsa_256.pub")), "Public key not found");
            
            string privateKey = File.ReadAllText(Path.Combine(scriptDir, "id_ecdsa_256"));
            Assert.Contains("BEGIN PRIVATE KEY", privateKey);
        }

        [Fact(Skip = "Ed25519 support is currently disabled due to missing SDK types in this environment.")]
        public async Task TestGenerateEd25519Key()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string scriptDir = Path.Combine(_testDir, "ed25519");
            string script = $"CREATE SSH_KEY_PAIR('{scriptDir.Replace("\\", "\\\\")}', 0, 'ED25519');";
            
            await evaluator.Evaluate(Parse(script));

            Assert.True(File.Exists(Path.Combine(scriptDir, "id_ed25519")), "Private key not found");
            Assert.True(File.Exists(Path.Combine(scriptDir, "id_ed25519.pub")), "Public key not found");
            
            string privateKey = File.ReadAllText(Path.Combine(scriptDir, "id_ed25519"));
            Assert.Contains("BEGIN PRIVATE KEY", privateKey);
        }

        [Fact]
        public async Task TestGenerateEncryptedRsaKey()
        {
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            string scriptDir = Path.Combine(_testDir, "encrypted");
            string passphrase = "secret_passphrase";
            string script = $"CREATE SSH_KEY_PAIR('{scriptDir.Replace("\\", "\\\\")}', 2048, 'RSA', '{passphrase}');";
            
            await evaluator.Evaluate(Parse(script));

            string privateKey = File.ReadAllText(Path.Combine(scriptDir, "id_rsa"));
            Assert.Contains("BEGIN ENCRYPTED PRIVATE KEY", privateKey);
        }
    }
}
