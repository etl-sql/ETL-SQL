using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Formatting;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using PgpCore;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public sealed class DetachedSignatureTests : IDisposable
{
    private readonly string _root;

    public DetachedSignatureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "etlsql_fo12_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private static async Task RunAsync(string sql)
    {
        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        await eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse());
    }

    private (string Pub, string Priv) GeneratePgpKeyPair(string name, string passphrase)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var pub = Path.Combine(dir, "public.asc");
        var priv = Path.Combine(dir, "private.asc");
        using var pgp = new PGP();
        pgp.GenerateKey(new FileInfo(pub), new FileInfo(priv), $"{name}@example.test", passphrase);
        return (pub, priv);
    }

    [Fact]
    public async Task SignAndVerifyFileSignature_GoodWrongSignerTamperedMissingAndNoKeyLeak()
    {
        const string pass = "super-secret-signing-passphrase-xyz";
        var (alicePub, alicePriv) = GeneratePgpKeyPair("alice", pass);
        var (bobPub, _) = GeneratePgpKeyPair("bob", "bob-pass");

        var dataFile = Path.Combine(_root, "remittance.csv");
        await File.WriteAllTextAsync(dataFile, "id,amount\n1,500.00\n");

        // 1. Good detached signature (default <source>.sig)
        await RunAsync($"""
            SIGN FILE '{dataFile}' PGP_KEY '{alicePriv}' PASSWORD '{pass}';
            VERIFY FILE SIGNATURE '{dataFile}' PGP_KEY '{alicePub}';
            """);

        var defaultSig = dataFile + ".sig";
        Assert.True(File.Exists(defaultSig));
        var sigText = await File.ReadAllTextAsync(defaultSig);
        Assert.Contains("BEGIN PGP SIGNATURE", sigText);

        // 2. Wrong signer must fail without echoing key material
        var wrongSignerEx = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync($"""
            VERIFY FILE SIGNATURE '{dataFile}' SIGNATURE '{defaultSig}' PGP_KEY '{bobPub}';
            """));
        Assert.Contains("not created by", wrongSignerEx.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN PGP", wrongSignerEx.Message);

        // 3. Tampered file must fail
        var tamperedFile = Path.Combine(_root, "tampered.csv");
        await File.WriteAllTextAsync(tamperedFile, "id,amount\n1,999999.00\n");
        var tamperedEx = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync($"""
            VERIFY FILE SIGNATURE '{tamperedFile}' SIGNATURE '{defaultSig}' PGP_KEY '{alicePub}';
            """));
        Assert.Contains("modified after signing", tamperedEx.Message, StringComparison.OrdinalIgnoreCase);

        // 4. Missing signature file must fail
        var missingSigEx = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync($"""
            VERIFY FILE SIGNATURE '{dataFile}' SIGNATURE '{Path.Combine(_root, "missing.sig")}' PGP_KEY '{alicePub}';
            """));
        Assert.Contains("does not exist", missingSigEx.Message, StringComparison.OrdinalIgnoreCase);

        // 5. Wrong passphrase must fail without echoing passphrase or creating partial output
        var badSigOut = Path.Combine(_root, "should_not_exist.sig");
        var badPassEx = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync($"""
            SIGN FILE '{dataFile}' TO '{badSigOut}' PGP_KEY '{alicePriv}' PASSWORD 'wrong-passphrase-999';
            """));
        Assert.DoesNotContain("wrong-passphrase-999", badPassEx.Message);
        Assert.DoesNotContain("BEGIN PGP", badPassEx.Message);
        Assert.False(File.Exists(badSigOut));

        // 6. TO DIRECTORY and WHAT_IF
        var sigDir = Path.Combine(_root, "sigs");
        await RunAsync($"""
            SET WHAT_IF ON;
            SIGN FILE '{dataFile}' TO DIRECTORY '{sigDir}' PGP_KEY '{alicePriv}' PASSWORD '{pass}';
            SET WHAT_IF OFF;
            """);
        Assert.False(Directory.Exists(sigDir));

        await RunAsync($"""
            SIGN FILE '{dataFile}' TO DIRECTORY '{sigDir}' PGP_KEY '{alicePriv}' PASSWORD '{pass}';
            VERIFY FILE SIGNATURE '{dataFile}' SIGNATURE '{Path.Combine(sigDir, "remittance.csv.sig")}' PGP_KEY '{alicePub}';
            """);
        Assert.True(File.Exists(Path.Combine(sigDir, "remittance.csv.sig")));
    }

    [Fact]
    public void SignAndVerifyFileSignature_ParserFormatterAndMetadata_Match()
    {
        Assert.Contains("SIGN", LanguageMetadata.DdlKeywords);
        Assert.True(LanguageMetadata.IsKeyword("SIGNATURE"));

        var signSql = "SIGN FILE 'a.csv' TO 'a.csv.sig' PGP_KEY 'priv.asc' PASSWORD 'p' WITH (OVERWRITE = OFF);";
        var signScript = new Parser(new Lexer(signSql).Tokenize(), signSql).Parse();
        Assert.Empty(signScript.Diagnostics);
        Assert.Contains("SIGN FILE", signScript.Statements[0].ToSql());

        var verifySql = "VERIFY FILE SIGNATURE 'a.csv' SIGNATURE 'a.csv.sig' PGP_KEY 'pub.asc';";
        var verifyScript = new Parser(new Lexer(verifySql).Tokenize(), verifySql).Parse();
        Assert.Empty(verifyScript.Diagnostics);
        Assert.Contains("VERIFY FILE SIGNATURE", verifyScript.Statements[0].ToSql());

        // Math function SIGN(-5) must remain unaffected
        var mathScript = new Parser(new Lexer("SELECT SIGN(-5) AS s;").Tokenize()).Parse();
        Assert.Empty(mathScript.Diagnostics);

        // Missing PGP_KEY is a syntax error
        var noKeySign = new Parser(new Lexer("SIGN FILE 'a.csv';").Tokenize()).Parse();
        Assert.Single(noKeySign.Diagnostics);
        var noKeyVerify = new Parser(new Lexer("VERIFY FILE SIGNATURE 'a.csv';").Tokenize()).Parse();
        Assert.Single(noKeyVerify.Diagnostics);
    }
}
