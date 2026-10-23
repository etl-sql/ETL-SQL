using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using Xunit;

namespace ETL_SQL.Tests.Reporting
{
    /// <summary>
    /// Dataset machine-bound cryptography, encryption options, and encryption lint policy.
    /// </summary>
    public class DatasetEncryptionTests
    {
        // ── MachineBoundCrypto ────────────────────────────────────────────────────

        [Fact]
        public void MachineBoundCrypto_ProtectUnprotect_RoundTrips()
        {
            var original = "Hello, machine-bound world!"u8.ToArray();
            var protected_ = MachineBoundCrypto.Protect(original);
            var recovered = MachineBoundCrypto.Unprotect(protected_);

            Assert.Equal(original, recovered);
        }

        [Fact]
        public void MachineBoundCrypto_ProtectedBytes_DifferFromPlaintext()
        {
            var original = new byte[] { 1, 2, 3, 4, 5 };
            var protected_ = MachineBoundCrypto.Protect(original);

            // Ciphertext must not be identical to plaintext
            Assert.False(original.SequenceEqual(protected_));
        }

        [Fact]
        public void MachineBoundCrypto_ProtectedBytes_DetectTampering()
        {
            var protected_ = MachineBoundCrypto.Protect("tamper check"u8.ToArray());
            protected_[^1] ^= 0x01;

            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
                () => MachineBoundCrypto.Unprotect(protected_));
        }

        [Fact]
        public void MachineBoundCrypto_KeysComeFromAPrivateKeyFile_AndOldPayloadsStillDecrypt()
        {
            if (OperatingSystem.IsWindows()) return; // Windows in-memory payloads use DPAPI.

            var protected_ = MachineBoundCrypto.Protect("new payload"u8.ToArray());
            Assert.Equal("ETLSQLM3", System.Text.Encoding.ASCII.GetString(protected_, 0, 8));
            var keyFile = MachineBoundCrypto.KeyFilePath();
            Assert.True(File.Exists(keyFile));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keyFile));

            // A payload written before the key file existed: AES-GCM keyed from the machine id.
            var machineSecret = File.Exists("/etc/machine-id") ? File.ReadAllText("/etc/machine-id").Trim() : Environment.MachineName;
            var legacyKey = System.Security.Cryptography.HKDF.DeriveKey(
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Text.Encoding.UTF8.GetBytes(machineSecret), 32, info: "etl-sql-machine-bound"u8.ToArray());
            var plaintext = "old payload"u8.ToArray();
            var nonce = new byte[12];
            var tag = new byte[16];
            var cipher = new byte[plaintext.Length];
            using (var gcm = new System.Security.Cryptography.AesGcm(legacyKey, 16))
                gcm.Encrypt(nonce, plaintext, cipher, tag);
            var legacy = "ETLSQLM2"u8.ToArray().Concat(nonce).Concat(tag).Concat(cipher).ToArray();

            Assert.Equal(plaintext, MachineBoundCrypto.Unprotect(legacy));
        }

        [Fact]
        public async Task StreamDecryption_RefusesATamperedFile()
        {
            var dir = Path.GetTempPath();
            var plain = Path.Combine(dir, Path.GetRandomFileName());
            var machine = Path.Combine(dir, Path.GetRandomFileName());
            var password = Path.Combine(dir, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(plain, "id,amount\n1,100\n2,200\n");
                await MachineBoundCrypto.EncryptFileAsync(plain, machine);
                ETL_SQL.Common.CryptoUtils.EncryptFile(plain, password, "correct horse battery", overwrite: true);

                foreach (var path in new[] { machine, password })
                {
                    var bytes = File.ReadAllBytes(path);
                    bytes[bytes.Length - 40] ^= 0x01; // a ciphertext byte, before the 32-byte tag
                    File.WriteAllBytes(path, bytes);
                }

                using (var stream = File.OpenRead(machine))
                    Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => MachineBoundCrypto.DecryptStream(stream));
                using (var stream = File.OpenRead(password))
                    Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => ETL_SQL.Common.CryptoUtils.DecryptStream(stream, "correct horse battery"));
            }
            finally
            {
                foreach (var f in new[] { plain, machine, password })
                    if (File.Exists(f)) File.Delete(f);
            }
        }

        [Fact]
        public async Task MachineBoundCrypto_StreamRoundTrip_ReturnsTheContent()
        {
            var plain = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var encrypted = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            try
            {
                File.WriteAllText(plain, "id,amount\n1,100\n");
                await MachineBoundCrypto.EncryptFileAsync(plain, encrypted);
                using var input = File.OpenRead(encrypted);
                using var reader = new StreamReader(MachineBoundCrypto.DecryptStream(input));
                Assert.Equal("id,amount\n1,100\n", reader.ReadToEnd());
            }
            finally
            {
                foreach (var f in new[] { plain, encrypted })
                    if (File.Exists(f)) File.Delete(f);
            }
        }

        [Fact]
        public async Task MachineBoundCrypto_FileRoundTrip_ProducesIdenticalContent()
        {
            var dir = Path.GetTempPath();
            var plain = Path.Combine(dir, Path.GetRandomFileName());
            var encrypted = Path.Combine(dir, Path.GetRandomFileName());
            var decrypted = Path.Combine(dir, Path.GetRandomFileName());
            var content = "Dataset row 1,row 2,row 3\n";

            try
            {
                File.WriteAllText(plain, content);
                await MachineBoundCrypto.EncryptFileAsync(plain, encrypted);

                // Encrypted file must differ from plaintext
                Assert.NotEqual(content, File.ReadAllText(encrypted));

                await MachineBoundCrypto.DecryptFileAsync(encrypted, decrypted);
                Assert.Equal(content, File.ReadAllText(decrypted));
            }
            finally
            {
                foreach (var f in new[] { plain, encrypted, decrypted })
                    if (File.Exists(f)) File.Delete(f);
            }
        }

        // ── EncryptionOptions — MACHINE mode ──────────────────────────────────────

        [Fact]
        public void EncryptionOptions_MachineBound_EnabledAndFlagged()
        {
            var opts = new EncryptionOptions(
                new System.Collections.Generic.Dictionary<string, string>
                {
                    ["ENCRYPT"] = "MACHINE"
                });

            Assert.True(opts.Enabled);
            Assert.True(opts.IsMachineBound);
        }

        [Fact]
        public void EncryptionOptions_None_NotEnabledNotMachineBound()
        {
            var opts = new EncryptionOptions(null);

            Assert.False(opts.Enabled);
            Assert.False(opts.IsMachineBound);
        }

        [Fact]
        public void EncryptionOptions_OnOrTrue_WithPassword_EnabledButNotMachineBound()
        {
            foreach (var mode in new[] { "ON", "TRUE" })
            {
                var opts = new EncryptionOptions(
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["ENCRYPT"] = mode,
                        ["PASSWORD"] = "an-actual-key"
                    });

                Assert.True(opts.Enabled, $"Expected Enabled for ENCRYPT={mode}");
                Assert.False(opts.IsMachineBound, $"Expected IsMachineBound=false for ENCRYPT={mode}");
            }
        }

        [Fact]
        public void EncryptionOptions_EnabledWithoutKey_FailsClosed()
        {
            // ENCRYPT=ON without PASSWORD/KEYFILE must throw rather than silently use a default key.
            foreach (var mode in new[] { "ON", "TRUE", "PASSWORD" })
            {
                Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
                    new EncryptionOptions(new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["ENCRYPT"] = mode
                    }));
            }
        }

        [Fact]
        public void EncryptionOptions_WeakAlgorithm_IsRejected()
        {
            foreach (var weak in new[] { "MD5", "SHA1" })
            {
                Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(() =>
                    new EncryptionOptions(new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["ENCRYPT"] = "ON",
                        ["PASSWORD"] = "k",
                        ["ALGORITHM"] = weak
                    }));
            }
        }

        // ── DatasetEncryptionModeRule ─────────────────────────────────────────────

        private static Script Parse(string sql)
        {
            var tokens = new Lexer(sql).Tokenize();
            return new Parser(tokens).Parse();
        }

        [Fact]
        public async Task DatasetEncryptionModeRule_PasswordMode_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new DatasetEncryptionModeRule());

            var sql = "CREATE DATASET &sales ENCRYPT = PASSWORD PASSWORD = 'secret' AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("ENCRYPT = PASSWORD", results[0].Message);
            Assert.Contains("EXPORT", results[0].Message);
        }

        [Fact]
        public async Task DatasetEncryptionModeRule_KeyFileMode_IsWarning()
        {
            var linter = new Linter();
            linter.AddRule(new DatasetEncryptionModeRule());

            var sql = "CREATE DATASET &sales ENCRYPT = KEYFILE KEYFILE = '/keys/k.pem' AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("ENCRYPT = KEYFILE", results[0].Message);
        }

        [Fact]
        public async Task DatasetEncryptionModeRule_MachineMode_NoErrors()
        {
            var linter = new Linter();
            linter.AddRule(new DatasetEncryptionModeRule());

            var sql = "CREATE DATASET &sales ENCRYPT = MACHINE AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task DatasetEncryptionModeRule_NoEncryption_NoErrors()
        {
            var linter = new Linter();
            linter.AddRule(new DatasetEncryptionModeRule());

            var sql = "CREATE DATASET &sales AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public async Task DatasetEncryptionModeRule_AutoDiscoveredByLinterFactory()
        {
            // LinterFactory uses reflection — verify the new rule is picked up automatically
            var linter = LinterFactory.CreateWithAllRules();
            var sql = "CREATE DATASET &sales ENCRYPT = PASSWORD PASSWORD = 'x' AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Contains(results, r => r.RuleName == "DatasetEncryptionMode" && r.Severity == LintSeverity.Warning);
        }
    }
}
