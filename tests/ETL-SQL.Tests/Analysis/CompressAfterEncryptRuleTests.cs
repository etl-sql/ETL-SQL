using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core.Parser;
using Xunit;

namespace ETL_SQL.Tests.Analysis
{
    /// <summary>
    /// Tests for the <see cref="CompressAfterEncryptRule"/> lint rule.
    /// Validates that a warning is emitted when COMPRESS FILE follows
    /// ENCRYPT FILE on the same target (compressing encrypted data is
    /// ineffective because encryption maximises entropy).
    /// </summary>
    public class CompressAfterEncryptRuleTests
    {
        private Script Parse(string sql)
        {
            var lexer = new Lexer(sql);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, sql);
            return parser.Parse();
        }

        private async Task<System.Collections.Generic.List<LintResult>> Analyze(string sql)
        {
            var linter = new Linter();
            linter.AddRule(new CompressAfterEncryptRule());
            var script = Parse(sql);
            return await linter.AnalyzeAsync(script, new DefaultLintContext());
        }

        // ── Positive: should warn ───────────────────────────────────────────

        [Fact]
        public async Task CompressAfterEncrypt_SamePath_EmitsWarning()
        {
            var results = await Analyze(@"
                ENCRYPT FILE 'C:\data\export.csv' PASSWORD('pw123');
                COMPRESS FILE 'C:\data\export.csv';
            ");

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Equal("CompressAfterEncrypt", results[0].RuleName);
            Assert.Equal("PERF-COMPRESS-AFTER-ENCRYPT", results[0].Code);
            Assert.Contains("compress first, then encrypt", results[0].Message);
        }

        [Fact]
        public async Task CompressAfterEncrypt_DestinationBecomesSource_EmitsWarning()
        {
            // ENCRYPT FILE source TO dest; COMPRESS FILE dest → should warn
            var results = await Analyze(@"
                ENCRYPT FILE 'C:\data\raw.csv' TO 'C:\data\encrypted.pgp' PASSWORD('pw123');
                COMPRESS FILE 'C:\data\encrypted.pgp';
            ");

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
            Assert.Contains("encrypted.pgp", results[0].Message);
        }

        [Fact]
        public async Task CompressAfterEncrypt_InsideTryCatch_EmitsWarning()
        {
            var results = await Analyze(@"
                BEGIN TRY
                    ENCRYPT FILE 'C:\data\export.csv' PASSWORD('pw123');
                    COMPRESS FILE 'C:\data\export.csv';
                END TRY
                BEGIN CATCH
                    PRINT 'Error';
                END CATCH
            ");

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
        }

        [Fact]
        public async Task CompressAfterEncrypt_InsideBlock_EmitsWarning()
        {
            var results = await Analyze(@"
                BEGIN
                    ENCRYPT FILE 'C:\data\file.csv' PASSWORD('pw');
                    COMPRESS FILE 'C:\data\file.csv';
                END
            ");

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
        }

        // ── Negative: should NOT warn ───────────────────────────────────────

        [Fact]
        public async Task CompressThenEncrypt_CorrectOrder_NoWarning()
        {
            var results = await Analyze(@"
                COMPRESS FILE 'C:\data\export.csv';
                ENCRYPT FILE 'C:\data\export.csv' PASSWORD('pw123');
            ");

            Assert.Empty(results);
        }

        [Fact]
        public async Task EncryptOnly_NoCompress_NoWarning()
        {
            var results = await Analyze(@"
                ENCRYPT FILE 'C:\data\export.csv' PASSWORD('pw123');
            ");

            Assert.Empty(results);
        }

        [Fact]
        public async Task CompressOnly_NoEncrypt_NoWarning()
        {
            var results = await Analyze(@"
                COMPRESS FILE 'C:\data\export.csv';
            ");

            Assert.Empty(results);
        }

        [Fact]
        public async Task CompressAfterEncrypt_DifferentPaths_NoWarning()
        {
            var results = await Analyze(@"
                ENCRYPT FILE 'C:\data\file1.csv' PASSWORD('pw123');
                COMPRESS FILE 'C:\data\file2.csv';
            ");

            Assert.Empty(results);
        }

        [Fact]
        public async Task CompressAfterEncrypt_VariablePaths_NoWarning()
        {
            // When paths are variables (not literals), we can't statically
            // determine the actual file — no warning is emitted.
            var results = await Analyze(@"
                DECLARE @path VARCHAR(200) = 'C:\data\export.csv';
                ENCRYPT FILE @path PASSWORD('pw123');
                COMPRESS FILE @path;
            ");

            Assert.Empty(results);
        }

        // ── Edge cases ──────────────────────────────────────────────────────

        [Fact]
        public async Task CompressAfterEncrypt_CaseInsensitivePaths_EmitsWarning()
        {
            var results = await Analyze(@"
                ENCRYPT FILE 'C:\Data\EXPORT.csv' PASSWORD('pw123');
                COMPRESS FILE 'c:\data\export.csv';
            ");

            Assert.Single(results);
            Assert.Equal(LintSeverity.Warning, results[0].Severity);
        }

        [Fact]
        public async Task MultipleEncryptsThenCompress_EmitsWarningForEach()
        {
            var results = await Analyze(@"
                ENCRYPT FILE 'C:\data\file1.csv' PASSWORD('pw1');
                ENCRYPT FILE 'C:\data\file2.csv' PASSWORD('pw2');
                COMPRESS FILE 'C:\data\file1.csv';
                COMPRESS FILE 'C:\data\file2.csv';
            ");

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(LintSeverity.Warning, r.Severity));
        }
    }
}
