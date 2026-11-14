using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using PgpCore;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// ENCRYPT FILE and DECRYPT FILE derive a safe sibling path when TO is omitted: .pgp for PGP_KEY,
/// .enc for PASSWORD or KEYFILE, and strip .pgp/.enc on DECRYPT FILE while refusing other names
/// (ROADMAP FO04).
/// </summary>
public sealed class FileDefaultDestinationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etlsql-defaultdest-" + Guid.NewGuid().ToString("N"));

    public FileDefaultDestinationTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "orders.csv"), "id,amount\n1,10\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name).Replace("'", "''");
    private bool Exists(string name) => File.Exists(Path.Combine(_dir, name));

    private static async Task<Evaluator> RunAsync(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.True(script.Diagnostics.Count == 0, string.Join("\n", script.Diagnostics.Select(d => d.Message)));
        await evaluator.Evaluate(script);
        return evaluator;
    }

    [Fact]
    public async Task Password_EncryptAndDecrypt_WithoutTo_UsesEncSuffix()
    {
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' PASSWORD 'secret';");
        Assert.True(Exists("orders.csv"));
        Assert.True(Exists("orders.csv.enc"));

        File.Delete(Path.Combine(_dir, "orders.csv"));
        await RunAsync($"DECRYPT FILE '{P("orders.csv.enc")}' PASSWORD 'secret';");
        Assert.True(Exists("orders.csv"));
        Assert.Equal("id,amount\n1,10\n", File.ReadAllText(Path.Combine(_dir, "orders.csv")));
    }

    [Fact]
    public async Task Keyfile_EncryptAndDecrypt_WithoutTo_UsesEncSuffix()
    {
        await RunAsync($"""
            CREATE SSH_KEY_PAIR '{P("keys")}' WITH (BITS = 2048, ALGORITHM = 'RSA');
            ENCRYPT FILE '{P("orders.csv")}' KEYFILE '{P(Path.Combine("keys", "id_rsa.pub"))}';
            """);

        Assert.True(Exists("orders.csv"));
        Assert.True(Exists("orders.csv.enc"));

        File.Delete(Path.Combine(_dir, "orders.csv"));
        await RunAsync($"DECRYPT FILE '{P("orders.csv.enc")}' KEYFILE '{P(Path.Combine("keys", "id_rsa"))}';");
        Assert.Equal("id,amount\n1,10\n", File.ReadAllText(Path.Combine(_dir, "orders.csv")));
    }

    [Fact]
    public async Task Pgp_EncryptAndDecrypt_WithoutTo_UsesPgpSuffix()
    {
        var pub = Path.Combine(_dir, "pub.asc");
        var priv = Path.Combine(_dir, "priv.asc");
        using (var pgp = new PGP())
        {
            await pgp.GenerateKeyAsync(new FileInfo(pub), new FileInfo(priv), "partner <partner@example.com>", "kpass", 2048);
        }

        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' PGP_KEY '{P("pub.asc")}';");
        Assert.True(Exists("orders.csv"));
        Assert.True(Exists("orders.csv.pgp"));

        File.Delete(Path.Combine(_dir, "orders.csv"));
        await RunAsync($"DECRYPT FILE '{P("orders.csv.pgp")}' PGP_KEY '{P("priv.asc")}' PASSWORD 'kpass';");
        Assert.Equal("id,amount\n1,10\n", File.ReadAllText(Path.Combine(_dir, "orders.csv")));
    }

    [Theory]
    [InlineData("orders.csv")]
    [InlineData("orders.dat")]
    [InlineData(".enc")]
    public async Task Decrypt_WithoutTo_RejectsUnrecognizedSuffix(string fileName)
    {
        var path = Path.Combine(_dir, fileName);
        if (!File.Exists(path))
            File.WriteAllText(path, "payload");

        var error = await Assert.ThrowsAsync<ExecutionException>(() =>
            RunAsync($"DECRYPT FILE '{P(fileName)}' PASSWORD 'secret';"));

        Assert.Contains("DECRYPT FILE without TO requires a '.pgp' or '.enc'", error.Message);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ExistingDerivedDestination_FollowsOverwrite()
    {
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' PASSWORD 'secret';");

        var error = await Assert.ThrowsAsync<ExecutionException>(() =>
            RunAsync($"ENCRYPT FILE '{P("orders.csv")}' PASSWORD 'secret' WITH (OVERWRITE = OFF);"));
        Assert.Contains("OVERWRITE is OFF", error.Message);

        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' PASSWORD 'secret' WITH (OVERWRITE = ON);");
        Assert.True(Exists("orders.csv.enc"));

        var decError = await Assert.ThrowsAsync<ExecutionException>(() =>
            RunAsync($"DECRYPT FILE '{P("orders.csv.enc")}' PASSWORD 'secret' WITH (OVERWRITE = OFF);"));
        Assert.Contains("OVERWRITE is OFF", decError.Message);
    }

    [Fact]
    public async Task WhatIf_ShowsDerivedDestination_AndWritesNothing()
    {
        var encryptEval = await RunAsync($"SET WHAT_IF ON; ENCRYPT FILE '{P("orders.csv")}' PASSWORD 'secret';");

        Assert.False(Exists("orders.csv.enc"));
        Assert.Contains(encryptEval.Messages, l => l.Message.Contains("WHAT IF:") && l.Message.Contains("orders.csv.enc"));

        File.WriteAllText(Path.Combine(_dir, "orders.csv.enc"), "stub");
        var decryptEval = await RunAsync($"SET WHAT_IF ON; DECRYPT FILE '{P("orders.csv.enc")}' PASSWORD 'secret';");

        Assert.Contains(decryptEval.Messages, l => l.Message.Contains("WHAT IF:") && l.Message.Contains(" -> ") && l.Message.EndsWith("orders.csv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteSourceOn_WithoutTo_DeletesSourceAfterWritingDerivedFile()
    {
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' PASSWORD 'secret' WITH (DELETE_SOURCE = ON);");
        Assert.False(Exists("orders.csv"));
        Assert.True(Exists("orders.csv.enc"));

        await RunAsync($"DECRYPT FILE '{P("orders.csv.enc")}' PASSWORD 'secret' WITH (DELETE_SOURCE = ON);");
        Assert.True(Exists("orders.csv"));
        Assert.False(Exists("orders.csv.enc"));
    }
}
