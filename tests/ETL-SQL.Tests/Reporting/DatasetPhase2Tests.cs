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
    /// Phase 2 DATASET tests: machine-bound crypto, EncryptionOptions MACHINE mode,
    /// and the DatasetEncryptionModeRule lint rule.
    /// </summary>
    public class DatasetPhase2Tests
    {
        // ── MachineBoundCrypto ────────────────────────────────────────────────────

        [Fact]
        public void MachineBoundCrypto_ProtectUnprotect_RoundTrips()
        {
            var original = "Hello, machine-bound world!"u8.ToArray();
            var protected_ = MachineBoundCrypto.Protect(original);
            var recovered  = MachineBoundCrypto.Unprotect(protected_);

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
        public void MachineBoundCrypto_FileRoundTrip_ProducesIdenticalContent()
        {
            var dir = Path.GetTempPath();
            var plain     = Path.Combine(dir, Path.GetRandomFileName());
            var encrypted = Path.Combine(dir, Path.GetRandomFileName());
            var decrypted = Path.Combine(dir, Path.GetRandomFileName());
            var content   = "Dataset row 1,row 2,row 3\n";

            try
            {
                File.WriteAllText(plain, content);
                MachineBoundCrypto.EncryptFile(plain, encrypted);

                // Encrypted file must differ from plaintext
                Assert.NotEqual(content, File.ReadAllText(encrypted));

                MachineBoundCrypto.DecryptFile(encrypted, decrypted);
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
        public void EncryptionOptions_OnOrTrue_EnabledButNotMachineBound()
        {
            foreach (var mode in new[] { "ON", "TRUE" })
            {
                var opts = new EncryptionOptions(
                    new System.Collections.Generic.Dictionary<string, string>
                    {
                        ["ENCRYPT"] = mode
                    });

                Assert.True(opts.Enabled, $"Expected Enabled for ENCRYPT={mode}");
                Assert.False(opts.IsMachineBound, $"Expected IsMachineBound=false for ENCRYPT={mode}");
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
            var sql    = "CREATE DATASET &sales ENCRYPT = PASSWORD PASSWORD = 'x' AS (SELECT 1 AS v FROM t);";
            var results = (await linter.AnalyzeAsync(Parse(sql), new DefaultLintContext())).ToList();

            Assert.Contains(results, r => r.RuleName == "DatasetEncryptionMode" && r.Severity == LintSeverity.Warning);
        }
    }
}
