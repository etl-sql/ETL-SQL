using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using PgpCore;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// Key pairs for <see cref="PgpSignVerifyTests"/>, generated once: RSA generation dominates the run.
/// </summary>
public sealed class PgpKeyFixture : IAsyncLifetime
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "etlsql-pgpsign-keys-" + Guid.NewGuid().ToString("N"));

    public string Key(string owner, string kind) => Path.Combine(Dir, $"{owner}_{kind}.asc");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Dir);
        foreach (var owner in new[] { "us", "partner", "stranger" })
        {
            using var pgp = new PGP();
            await pgp.GenerateKeyAsync(new FileInfo(Key(owner, "public")), new FileInfo(Key(owner, "private")),
                $"{owner} <{owner}@example.com>", owner + "-pass", 2048);
        }
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }
}

/// <summary>
/// ENCRYPT FILE ... SIGNING_KEY signs while it encrypts, and DECRYPT FILE ... VERIFY_KEY refuses a
/// file that is unsigned, signed by another key or changed after signing (ROADMAP FO11). Measured
/// before the fix: PgpCore leaves an output file on every one of those failures, and a tampered file
/// leaves the full plaintext, so a failed verify must write nothing.
/// </summary>
public sealed class PgpSignVerifyTests : IClassFixture<PgpKeyFixture>, IDisposable
{
    private readonly PgpKeyFixture _keys;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etlsql-pgpsign-" + Guid.NewGuid().ToString("N"));

    public PgpSignVerifyTests(PgpKeyFixture keys)
    {
        _keys = keys;
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "orders.csv"), "id,amount\n1,10\n2,20\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name).Replace("'", "''");
    private string K(string owner, string kind) => _keys.Key(owner, kind).Replace("'", "''");

    private static async Task RunAsync(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.True(script.Diagnostics.Count == 0, string.Join("\n", script.Diagnostics.Select(d => d.Message)));
        await evaluator.Evaluate(script);
    }

    private static string ParseError(string sql) =>
        new Parser(new Lexer(sql).Tokenize(), sql).Parse().Diagnostics.Select(d => d.Message).FirstOrDefault() ?? "";

    /// <summary>The partner signs a file for us with their private key, encrypted to our public key.</summary>
    private Task PartnerSendsSigned(string output, string signer = "partner") =>
        RunAsync($"ENCRYPT FILE '{P("orders.csv")}' TO '{P(output)}' PGP_KEY '{K("us", "public")}' " +
                 $"SIGNING_KEY '{K(signer, "private")}' SIGNING_PASSWORD '{signer}-pass';");

    private Task WeDecryptVerifyingPartner(string input, string output) =>
        RunAsync($"DECRYPT FILE '{P(input)}' TO '{P(output)}' PGP_KEY '{K("us", "private")}' PASSWORD 'us-pass' " +
                 $"VERIFY_KEY '{K("partner", "public")}';");

    private void AssertNoOutput(string output) =>
        Assert.Empty(Directory.GetFiles(_dir, output + "*"));

    [Fact]
    public async Task SignedByTheVerifyKey_DecryptsToTheOriginal()
    {
        await PartnerSendsSigned("orders.pgp");
        await WeDecryptVerifyingPartner("orders.pgp", "orders.out.csv");

        Assert.Equal(File.ReadAllText(Path.Combine(_dir, "orders.csv")), File.ReadAllText(Path.Combine(_dir, "orders.out.csv")));
    }

    [Fact]
    public async Task SignedByAnotherKey_FailsAndWritesNothing()
    {
        await PartnerSendsSigned("orders.pgp", signer: "stranger");

        var error = await Assert.ThrowsAsync<ExecutionException>(() => WeDecryptVerifyingPartner("orders.pgp", "orders.out.csv"));

        Assert.Contains("not signed by the VERIFY_KEY", error.Message);
        Assert.DoesNotContain("PGP PUBLIC KEY", error.Message);
        AssertNoOutput("orders.out.csv");
    }

    [Fact]
    public async Task Unsigned_FailsAndWritesNothing()
    {
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.pgp")}' PGP_KEY '{K("us", "public")}';");

        var error = await Assert.ThrowsAsync<ExecutionException>(() => WeDecryptVerifyingPartner("orders.pgp", "orders.out.csv"));

        Assert.Contains("is not signed", error.Message);
        AssertNoOutput("orders.out.csv");
    }

    [Fact]
    public async Task ChangedAfterSigning_FailsAndWritesNothing()
    {
        await PartnerSendsSigned("orders.pgp");
        var path = Path.Combine(_dir, "orders.pgp");
        var lines = File.ReadAllLines(path);
        var middle = lines.Length / 2;
        var chars = lines[middle].ToCharArray();
        chars[5] = chars[5] == 'A' ? 'B' : 'A';
        lines[middle] = new string(chars);
        File.WriteAllLines(path, lines);

        var error = await Assert.ThrowsAsync<ExecutionException>(() => WeDecryptVerifyingPartner("orders.pgp", "orders.out.csv"));

        Assert.Contains("changed", error.Message);
        AssertNoOutput("orders.out.csv");
    }

    [Fact]
    public async Task FailedVerify_KeepsAnExistingDestination()
    {
        await PartnerSendsSigned("orders.pgp", signer: "stranger");
        File.WriteAllText(Path.Combine(_dir, "orders.out.csv"), "keep me");

        await Assert.ThrowsAsync<ExecutionException>(() => WeDecryptVerifyingPartner("orders.pgp", "orders.out.csv"));

        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_dir, "orders.out.csv")));
    }

    [Fact]
    public async Task WrongSigningPassword_FailsWithoutEchoingItAndWritesNothing()
    {
        var error = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync(
            $"ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.pgp")}' PGP_KEY '{K("us", "public")}' " +
            $"SIGNING_KEY '{K("partner", "private")}' SIGNING_PASSWORD 'not-the-passphrase';"));

        Assert.Contains("SIGNING_PASSWORD", error.Message);
        Assert.DoesNotContain("not-the-passphrase", error.Message);
        AssertNoOutput("orders.pgp");
    }

    [Fact]
    public async Task PublicKeyAsSigningKey_SaysItNeedsThePrivateKey()
    {
        var error = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync(
            $"ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.pgp")}' PGP_KEY '{K("us", "public")}' " +
            $"SIGNING_KEY '{K("partner", "public")}';"));

        Assert.Contains("SIGNING_KEY", error.Message);
        Assert.Contains("private key", error.Message);
        AssertNoOutput("orders.pgp");
    }

    [Fact]
    public async Task SignedFile_StillDecryptsWithoutVerifyKey()
    {
        // VERIFY_KEY is opt-in: a signed file decrypts like any other when the reader does not check it.
        await PartnerSendsSigned("orders.pgp");
        await RunAsync($"DECRYPT FILE '{P("orders.pgp")}' TO '{P("orders.out.csv")}' PGP_KEY '{K("us", "private")}' PASSWORD 'us-pass';");

        Assert.True(File.Exists(Path.Combine(_dir, "orders.out.csv")));
    }

    [Fact]
    public async Task TamperedFile_WithoutVerifyKey_FailsAndWritesNothing()
    {
        // The integrity check runs without VERIFY_KEY too, and used to leave the decrypted plaintext behind.
        // Enough rows that the middle of the armour is encrypted data, not the session-key packet.
        File.WriteAllText(Path.Combine(_dir, "orders.csv"), "id,amount\n" + string.Concat(Enumerable.Range(1, 400).Select(i => $"{i},{i * 10}\n")));
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.pgp")}' PGP_KEY '{K("us", "public")}';");
        var path = Path.Combine(_dir, "orders.pgp");
        var lines = File.ReadAllLines(path);
        var middle = lines.Length / 2;
        var chars = lines[middle].ToCharArray();
        chars[5] = chars[5] == 'A' ? 'B' : 'A';
        lines[middle] = new string(chars);
        File.WriteAllLines(path, lines);

        var error = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync(
            $"DECRYPT FILE '{P("orders.pgp")}' TO '{P("orders.out.csv")}' PGP_KEY '{K("us", "private")}' PASSWORD 'us-pass';"));

        Assert.Contains("changed", error.Message);
        AssertNoOutput("orders.out.csv");
    }

    [Theory]
    [InlineData("ENCRYPT FILE 'a' TO 'b' PASSWORD 'p' SIGNING_KEY 'k';", "SIGNING_KEY works with PGP_KEY only")]
    [InlineData("ENCRYPT FILE 'a' TO 'b' KEYFILE 'k.pub' SIGNING_KEY 'k';", "SIGNING_KEY works with PGP_KEY only")]
    [InlineData("DECRYPT FILE 'a' TO 'b' KEYFILE 'k' VERIFY_KEY 'v';", "VERIFY_KEY works with PGP_KEY only")]
    [InlineData("DECRYPT FILE 'a' TO 'b' PGP_KEY 'k' SIGNING_KEY 's';", "DECRYPT FILE does not take SIGNING_KEY")]
    [InlineData("ENCRYPT FILE 'a' TO 'b' PGP_KEY 'k' VERIFY_KEY 'v';", "ENCRYPT FILE does not take VERIFY_KEY")]
    [InlineData("ENCRYPT FILE 'a' TO 'b' PGP_KEY 'k' SIGNING_PASSWORD 'x';", "SIGNING_PASSWORD needs SIGNING_KEY")]
    [InlineData("COPY FILE 'a' TO 'b' SIGNING_KEY 's';", "COPY FILE does not take")]
    public void MisplacedSigningClause_IsASyntaxError(string sql, string expected)
    {
        Assert.Contains(expected, ParseError(sql));
    }
}
