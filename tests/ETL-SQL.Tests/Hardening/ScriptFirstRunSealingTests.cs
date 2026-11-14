using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using ETL_SQL.Orchestrator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ETL_SQL.Tests.Hardening;

/// <summary>
/// File-backed scripts are sealed on first run via SecurityService.SecureScriptForSave whenever
/// ALLOW_PLAINTEXT_SECRETS is OFF, so plaintext credentials and literal USE PASSWORD values
/// never stay on disk after execution.
/// </summary>
public sealed class ScriptFirstRunSealingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etlsql-sealrun-" + Guid.NewGuid().ToString("N"));

    public ScriptFirstRunSealingTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class StubPromptProvider(string password) : IPasswordPromptProvider
    {
        public int CallCount { get; private set; }
        public string ReadPassword(string prompt)
        {
            CallCount++;
            return password;
        }
    }

    [Fact]
    public async Task FirstRun_WithLiteralUsePassword_SealsScriptOnDiskAndRuns()
    {
        var file = Path.Combine(_dir, "job.etlsql");
        var raw = """
            USE PASSWORD = 'master-123';
            CREATE CONNECTION pg AS POSTGRES('Host=localhost;Database=app;Username=etl;Password=db-secret');
            SELECT 1 AS ok;
            """;
        await File.WriteAllTextAsync(file, raw);

        var sp = DependencyInjectionSetup.BuildServiceProvider();
        var evaluator = sp.GetRequiredService<Evaluator>();
        evaluator.CurrentScriptPath = file;

        var script = new Parser(new Lexer(raw).Tokenize(), raw).Parse();
        await evaluator.Evaluate(script);

        var onDisk = await File.ReadAllTextAsync(file);
        Assert.DoesNotContain("master-123", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("db-secret", onDisk, StringComparison.Ordinal);
        Assert.Contains("USE PASSWORD PROMPT;", onDisk, StringComparison.Ordinal);
        Assert.Contains("ENC:", onDisk, StringComparison.Ordinal);

        // Second run: sealed file uses USE PASSWORD PROMPT with MasterPassword supplied.
        var evaluator2 = sp.GetRequiredService<Evaluator>();
        evaluator2.CurrentScriptPath = file;
        evaluator2.MasterPassword = "master-123";
        var script2 = new Parser(new Lexer(onDisk).Tokenize(), onDisk).Parse();
        await evaluator2.Evaluate(script2);

        Assert.Equal(onDisk, await File.ReadAllTextAsync(file));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FirstRun_Sealing_KeepsTheFilesByteOrderMarkAndPaths(bool withBom)
    {
        var file = Path.Combine(_dir, withBom ? "bom.etlsql" : "nobom.etlsql");
        var raw = """
            USE PASSWORD = 'master-123';
            CREATE CONNECTION drop_zone AS DIRECTORY('samples/output/pickup/');
            SELECT 1 AS ok;
            """;
        await File.WriteAllTextAsync(file, raw, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));

        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.CurrentScriptPath = file;
        Assert.True(await evaluator.SealCurrentScriptIfNeededAsync(default));

        var bytes = await File.ReadAllBytesAsync(file);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.Equal(withBom, hasBom);
        var onDisk = await File.ReadAllTextAsync(file);
        Assert.Contains("USE PASSWORD PROMPT;", onDisk, StringComparison.Ordinal);
        // A directory path is not a credential: sealing leaves it readable.
        Assert.Contains("DIRECTORY('samples/output/pickup/')", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirstRun_WithAllowPlaintextSecretsOn_LeavesDiskUntouched()
    {
        var file = Path.Combine(_dir, "dev.etlsql");
        var raw = """
            USE PASSWORD = 'dev-only-password';
            CREATE CONNECTION pg AS POSTGRES('Host=localhost;Password=dev-db-pass');
            """;
        await File.WriteAllTextAsync(file, raw);

        var sp = DependencyInjectionSetup.BuildServiceProvider(new()
        {
            ["Engine:AllowPlaintextSecrets"] = "true"
        });
        var evaluator = sp.GetRequiredService<Evaluator>();
        evaluator.CurrentScriptPath = file;

        var script = new Parser(new Lexer(raw).Tokenize(), raw).Parse();
        await evaluator.Evaluate(script);

        Assert.Equal(raw, await File.ReadAllTextAsync(file));

        // Explicit script-level SET ALLOW_PLAINTEXT_SECRETS = ON also skips sealing on disk.
        var scriptOptInFile = Path.Combine(_dir, "dev-stmt.etlsql");
        var scriptOptIn = """
            SET ALLOW_PLAINTEXT_SECRETS = ON;
            USE PASSWORD = 'dev-only-password';
            """;
        await File.WriteAllTextAsync(scriptOptInFile, scriptOptIn);
        var eval2 = sp.GetRequiredService<Evaluator>();
        eval2.CurrentScriptPath = scriptOptInFile;
        await eval2.SealCurrentScriptIfNeededAsync(default);
        Assert.Equal(scriptOptIn, await File.ReadAllTextAsync(scriptOptInFile));
    }

    [Fact]
    public async Task FirstRun_WithWhatIf_DoesNotRewriteScriptOnDisk()
    {
        var file = Path.Combine(_dir, "whatif.etlsql");
        var raw = """
            USE PASSWORD = 'master-123';
            SELECT 1 AS n;
            """;
        await File.WriteAllTextAsync(file, raw);

        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.CurrentScriptPath = file;
        evaluator.IsWhatIf = true;

        var script = new Parser(new Lexer(raw).Tokenize(), raw).Parse();
        await evaluator.Evaluate(script);

        Assert.Equal(raw, await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task FirstRun_WithoutLiteralUsePassword_PromptsAndSealsOnce()
    {
        var file = Path.Combine(_dir, "prompted.etlsql");
        var raw = """
            CREATE CONNECTION pg AS POSTGRES('Host=localhost;Password=secret-db');
            SELECT 1 AS n;
            """;
        await File.WriteAllTextAsync(file, raw);

        var prompt = new StubPromptProvider("prompted-pass");
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ETL_SQL.Common.ILogger>(ETL_SQL.Common.NullLogger.Instance);
        services.AddLogging();
        services.AddEtlSqlEngine(config);
        services.AddSingleton<IPasswordPromptProvider>(prompt);
        await using var sp = services.BuildServiceProvider();

        var evaluator = sp.GetRequiredService<Evaluator>();
        evaluator.CurrentScriptPath = file;
        var script = new Parser(new Lexer(raw).Tokenize(), raw).Parse();
        await evaluator.Evaluate(script);

        Assert.Equal(1, prompt.CallCount);
        var sealedDisk = await File.ReadAllTextAsync(file);
        Assert.Contains("ENC:", sealedDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-db", sealedDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EngineRunner_CliSealing_RewritesScriptBeforeExecution()
    {
        var file = Path.Combine(_dir, "cli_job.etlsql");
        var raw = """
            USE PASSWORD = 'cli-pass';
            SELECT 42 AS answer;
            """;
        await File.WriteAllTextAsync(file, raw);

        var previous = Program.ServiceProvider;
        Program.ServiceProvider = DependencyInjectionSetup.BuildServiceProvider();
        try
        {
            var ctx = new CliContext
            {
                Command = "run",
                ScriptFile = new FileInfo(file),
                Password = null
            };
            var exit = await EngineRunner.Run(ctx);
            Assert.Equal(0, exit);

            var onDisk = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain("cli-pass", onDisk, StringComparison.Ordinal);
            Assert.Contains("USE PASSWORD PROMPT;", onDisk, StringComparison.Ordinal);
        }
        finally
        {
            Program.ServiceProvider = previous;
        }
    }
}
