using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Linting;
using ETL_SQL.Analysis.Linting.Rules;
using ETL_SQL.Core;
using Xunit;

namespace ETL_SQL.Tests.Analysis;

/// <summary>Lint rule defects found in the 2026-11-01 project security review.</summary>
public sealed class LintSecurityReviewTests
{
    private static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();

    private static async Task<System.Collections.Generic.List<LintResult>> LintAsync(ILintRule rule, string sql, ILintContext? context = null)
    {
        var linter = new Linter();
        linter.AddRule(rule);
        return await linter.AnalyzeAsync(Parse(sql), context ?? new DefaultLintContext());
    }

    // ── RunScriptDependencyPreflightRule ─────────────────────────────────────

    [Fact]
    public async Task RunScriptPreflight_DoesNotReadNonScriptOrProtectedFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lint-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // A file that is not a script: its tokens must not be echoed back in diagnostics.
            var notAScript = Path.Combine(dir, "secrets.ini");
            await File.WriteAllTextAsync(notAScript, "SELECT @leaked_secret_value;");
            var results = await LintAsync(new RunScriptDependencyPreflightRule(), $"RUN SCRIPT '{notAScript}';");
            Assert.DoesNotContain(results, r => r.Message.Contains("leaked_secret_value", StringComparison.Ordinal));

            // A script file inside a credential directory is not read either.
            var sshDir = Path.Combine(dir, ".ssh");
            Directory.CreateDirectory(sshDir);
            var protectedScript = Path.Combine(sshDir, "child.etlsql");
            await File.WriteAllTextAsync(protectedScript, "SELECT @leaked_secret_value;");
            results = await LintAsync(new RunScriptDependencyPreflightRule(), $"RUN SCRIPT '{protectedScript}';");
            Assert.DoesNotContain(results, r => r.Message.Contains("leaked_secret_value", StringComparison.Ordinal));

            // An ordinary script dependency is still preflighted.
            var child = Path.Combine(dir, "child.etlsql");
            await File.WriteAllTextAsync(child, "SELECT @missing_in_child;");
            results = await LintAsync(new RunScriptDependencyPreflightRule(), $"RUN SCRIPT '{child}';");
            Assert.Contains(results, r => r.Message.Contains("@missing_in_child", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task RunScriptPreflight_DoesNotReadSystemFiles()
    {
        var systemFile = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\Windows\win.ini" : "/etc/hostname";
        var results = await LintAsync(new RunScriptDependencyPreflightRule(), $"RUN SCRIPT '{systemFile}';");
        Assert.Empty(results);
    }

    // ── CredentialLeakRule ───────────────────────────────────────────────────

    // SECRET already sets IsSensitive in the parser; this pins that the rule sees it.
    [Fact]
    public async Task CredentialLeak_FlagsSecretTypedVariable()
    {
        var results = await LintAsync(new CredentialLeakRule(), "DECLARE @v SECRET = 'x'; PRINT @v;");
        Assert.Contains(results, r => r.Message.Contains("@v", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("PRINT '${@v}';")]
    [InlineData("PRINT 'value: ${v}';")]
    public async Task CredentialLeak_FlagsInterpolatedSensitiveVariable(string print)
    {
        var results = await LintAsync(new CredentialLeakRule(), "DECLARE @v SENSITIVE = 'ENC:x'; " + print);
        Assert.Contains(results, r => r.Message.Contains("@v", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CredentialLeak_IgnoresInterpolatedOrdinaryVariable()
    {
        var results = await LintAsync(new CredentialLeakRule(), "DECLARE @region STRING = 'west'; PRINT 'Region: ${@region}';");
        Assert.Empty(results);
    }

    // ── FileSystemSecurityRule ───────────────────────────────────────────────

    [Theory]
    [InlineData("COPY FILE '/home/user/.ssh/id_rsa' TO 'out.txt';")]
    [InlineData("COPY FILE 'C:\\repo\\.git\\config' TO 'out.txt';")]
    [InlineData("COPY FILE 'data/../../.ssh/id_rsa' TO 'out.txt';")]
    [InlineData("MERGE FILES '/home/user/.aws/*' TO 'out.txt';")]
    [InlineData("SPLIT FILE '/home/user/.ssh/id_rsa' TO 'parts/' WITH (LIMIT_TYPE = 'ROWS', LIMIT_VALUE = 10);")]
    [InlineData("SYNC DIRECTORY '/home/user/.ssh' TO 'backup';")]
    public async Task FileSystemSecurity_FlagsCredentialDirectoryAnywhereInThePath(string sql)
    {
        var results = await LintAsync(new FileSystemSecurityRule(), sql);
        Assert.Contains(results, r => r.RuleName == "FileSystemSecurity");
    }

    [Theory]
    [InlineData("COPY FILE 'data/etc/input.csv' TO 'out/input.csv';")]
    [InlineData("COPY FILE 'C:\\Projects\\gitlab\\a.csv' TO 'out.csv';")]
    public async Task FileSystemSecurity_DoesNotFlagOrdinaryPaths(string sql)
    {
        var results = await LintAsync(new FileSystemSecurityRule(), sql);
        Assert.Empty(results);
    }

    // ── Linter ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Linter_ReusedContextDoesNotStackOverlaysOrLeakScriptMetadata()
    {
        var baseline = new StubMetadataProvider(new());
        var context = new DefaultLintContext { Metadata = baseline };
        var linter = new Linter();
        linter.AddRule(new CredentialLeakRule());

        await linter.AnalyzeAsync(Parse("CREATE CONNECTION first AS MOCKDB();"), context);
        Assert.Contains("first", context.Metadata!.GetConnections());

        await linter.AnalyzeAsync(Parse("CREATE CONNECTION second AS MOCKDB();"), context);
        var overlay = Assert.IsType<ScriptMetadataOverlay>(context.Metadata);
        Assert.Contains("second", overlay.GetConnections());
        Assert.DoesNotContain("first", overlay.GetConnections());
        Assert.Same(baseline, overlay.BaseProvider);
    }
}
