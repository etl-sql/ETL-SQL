using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

/// <summary>
/// <c>WITH (DELETE_SOURCE = ON)</c> on ENCRYPT, DECRYPT, COMPRESS and DECOMPRESS FILE removes the
/// plaintext or archive only after the output is written; a failed run or WHAT_IF keeps it
/// (ROADMAP FO03). Scripts used to need a separate DELETE FILE, which ran even when the operation
/// before it had not produced anything.
/// </summary>
public sealed class FileDeleteSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etlsql-delsrc-" + Guid.NewGuid().ToString("N"));

    public FileDeleteSourceTests()
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

    private static async Task RunAsync(string sql)
    {
        var evaluator = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.True(script.Diagnostics.Count == 0, string.Join("\n", script.Diagnostics.Select(d => d.Message)));
        await evaluator.Evaluate(script);
    }

    private static string ParseError(string sql) =>
        new Parser(new Lexer(sql).Tokenize(), sql).Parse().Diagnostics.Select(d => d.Message).FirstOrDefault() ?? "";

    [Fact]
    public async Task Encrypt_DeleteSourceOn_RemovesThePlaintextAfterWriting()
    {
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.enc")}' PASSWORD 'p' WITH (DELETE_SOURCE = ON);");

        Assert.True(Exists("orders.enc"));
        Assert.False(Exists("orders.csv"));
    }

    [Fact]
    public async Task Encrypt_ByDefault_KeepsTheSource()
    {
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.enc")}' PASSWORD 'p';");

        Assert.True(Exists("orders.enc"));
        Assert.True(Exists("orders.csv"));
    }

    [Fact]
    public async Task Decrypt_DeleteSourceOn_RemovesTheEncryptedFile()
    {
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.enc")}' PASSWORD 'p';");
        await RunAsync($"DECRYPT FILE '{P("orders.enc")}' TO '{P("restored.csv")}' PASSWORD 'p' WITH (DELETE_SOURCE = ON);");

        Assert.True(Exists("restored.csv"));
        Assert.False(Exists("orders.enc"));
    }

    [Fact]
    public async Task FailedDecrypt_KeepsTheSource()
    {
        await RunAsync($"ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.enc")}' PASSWORD 'p';");

        await Assert.ThrowsAsync<ExecutionException>(() => RunAsync(
            $"DECRYPT FILE '{P("orders.enc")}' TO '{P("restored.csv")}' PASSWORD 'wrong' WITH (DELETE_SOURCE = ON);"));

        Assert.True(Exists("orders.enc"));
        Assert.False(Exists("restored.csv"));
    }

    [Fact]
    public async Task WhatIf_KeepsTheSourceAndWritesNothing()
    {
        await RunAsync($"SET WHAT_IF ON; ENCRYPT FILE '{P("orders.csv")}' TO '{P("orders.enc")}' PASSWORD 'p' WITH (DELETE_SOURCE = ON);");

        Assert.True(Exists("orders.csv"));
        Assert.False(Exists("orders.enc"));
    }

    [Fact]
    public async Task CompressThenDecompress_DeleteSourceOn_LeavesOnlyTheResult()
    {
        await RunAsync($"COMPRESS FILE '{P("orders.csv")}' TO '{P("orders.zip")}' WITH (DELETE_SOURCE = ON);");
        Assert.True(Exists("orders.zip"));
        Assert.False(Exists("orders.csv"));

        await RunAsync($"DECOMPRESS FILE '{P("orders.zip")}' TO '{P("out")}' WITH (DELETE_SOURCE = ON);");
        Assert.True(File.Exists(Path.Combine(_dir, "out", "orders.csv")));
        Assert.False(Exists("orders.zip"));
    }

    [Fact]
    public async Task OutputOverTheSource_IsRefusedBeforeAnythingRuns()
    {
        var error = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync(
            $"COMPRESS FILE '{P("orders.csv")}' TO '{P("orders.csv")}' WITH (DELETE_SOURCE = ON);"));

        Assert.Contains("DELETE_SOURCE", error.Message);
        Assert.Equal("id,amount\n1,10\n", File.ReadAllText(Path.Combine(_dir, "orders.csv")));
    }

    [Fact]
    public async Task DirectorySource_IsRefusedAndKept()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "batch"));
        File.WriteAllText(Path.Combine(_dir, "batch", "a.csv"), "a");

        var error = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync(
            $"COMPRESS FILE '{P("batch")}' TO '{P("batch.zip")}' WITH (DELETE_SOURCE = ON);"));

        Assert.Contains("DELETE DIRECTORY", error.Message);
        Assert.True(File.Exists(Path.Combine(_dir, "batch", "a.csv")));
        Assert.False(Exists("batch.zip"));
    }

    [Theory]
    [InlineData("COPY FILE 'a' TO 'b' WITH (DELETE_SOURCE = ON);", "COPY FILE has no WITH option 'DELETE_SOURCE'")]
    [InlineData("MOVE FILE 'a' TO 'b' WITH (DELETE_SOURCE = ON);", "MOVE FILE has no WITH option 'DELETE_SOURCE'")]
    public void DeleteSource_OnOtherFileStatements_IsASyntaxError(string sql, string expected)
    {
        Assert.Contains(expected, ParseError(sql));
    }
}
