using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// The master password and USE PASSWORD protect connection strings and secrets inside a script; they
/// are not a portable file passphrase. ENCRYPT/DECRYPT FILE and DIRECTORY used to fall back to the
/// master password when PASSWORD was absent, so a file could be encrypted with a key the recipient
/// never had. A file statement now needs its own PASSWORD, KEYFILE or PGP_KEY (ROADMAP FO09, owner
/// decision 2026-11-13). Engine-generated public keys (.pub) and PEM keys (.pem) are readable without
/// an override (ROADMAP FO10).
/// </summary>
[Trait("CompatBreak", "0.22.0")]
public sealed class FilePassphraseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etlsql-passphrase-" + Guid.NewGuid().ToString("N"));

    public FilePassphraseTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static async Task RunAsync(Evaluator evaluator, string sql) =>
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

    private Evaluator NewEvaluatorWithMasterPassword()
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        evaluator.SecurityService.MasterPassword = "connection-master-password";
        evaluator.MasterPassword = "connection-master-password";
        return evaluator;
    }

    private string P(string name) => Path.Combine(_dir, name).Replace("'", "''");

    [Fact]
    public async Task EncryptFile_WithoutPassword_DoesNotUseTheMasterPassword()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "plain.csv"), "a,b");
        var evaluator = NewEvaluatorWithMasterPassword();

        var error = await Assert.ThrowsAsync<ExecutionException>(() =>
            RunAsync(evaluator, $"ENCRYPT FILE '{P("plain.csv")}' TO '{P("plain.csv.enc")}';"));

        Assert.Contains("requires a PASSWORD, KEYFILE or PGP_KEY clause", error.Message);
        Assert.Contains("master password is not used for files", error.Message);
        Assert.False(File.Exists(Path.Combine(_dir, "plain.csv.enc")));
    }

    [Fact]
    public async Task EncryptDirectory_WithoutPassword_DoesNotUseTheMasterPassword()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "src", "a.csv"), "a,b");
        var evaluator = NewEvaluatorWithMasterPassword();

        var error = await Assert.ThrowsAsync<ExecutionException>(() =>
            RunAsync(evaluator, $"ENCRYPT DIRECTORY '{P("src")}' TO '{P("vault")}';"));

        Assert.Contains("requires a PASSWORD clause", error.Message);
        Assert.Contains("master password is not used for files", error.Message);
        Assert.False(File.Exists(Path.Combine(_dir, "vault", "a.csv.enc")));
    }

    [Fact]
    public async Task GeneratedSshKeyPair_EncryptsWithoutAFileTypeOverride()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "ledger.csv"), "id,amount\n1,10");
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

        await RunAsync(evaluator, $"""
            CREATE SSH_KEY_PAIR '{P("keys")}' WITH (BITS = 2048, ALGORITHM = 'RSA');
            ENCRYPT FILE '{P("ledger.csv")}' TO '{P("ledger.csv.enc")}' KEYFILE '{P(Path.Combine("keys", "id_rsa.pub"))}';
            DECRYPT FILE '{P("ledger.csv.enc")}' TO '{P("restored.csv")}' KEYFILE '{P(Path.Combine("keys", "id_rsa"))}';
            """);

        Assert.Equal("id,amount\n1,10", await File.ReadAllTextAsync(Path.Combine(_dir, "restored.csv")));
    }

    [Theory]
    [InlineData("partner.pub")]
    [InlineData("partner_public.pem")]
    public void KeyFileExtensions_AreAllowedByDefault(string fileName)
    {
        var security = new SecurityService(NullLogger.Instance);
        security.ValidateFileType(Path.Combine(_dir, fileName));
    }
}
