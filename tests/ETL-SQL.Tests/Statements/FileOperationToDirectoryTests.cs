using System;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ETL_SQL.Tests.Statements;

public sealed class FileOperationToDirectoryTests : IDisposable
{
    private readonly string _root;

    public FileOperationToDirectoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "etlsql_fo08_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task EncryptAndDecryptFile_ToDirectory_RoundTripAndRejectUnknownSuffix()
    {
        var srcFile = Path.Combine(_root, "payroll.csv");
        await File.WriteAllTextAsync(srcFile, "id,amount\n1,42\n");
        var encDir = Path.Combine(_root, "outbound");
        var decDir = Path.Combine(_root, "restored");

        await RunAsync($"""
            ENCRYPT FILE '{srcFile}' TO DIRECTORY '{encDir}' PASSWORD 'test-passphrase';
            """);

        var expectedEnc = Path.Combine(encDir, "payroll.csv.enc");
        Assert.True(File.Exists(expectedEnc));
        Assert.NotEqual("id,amount\n1,42\n", await File.ReadAllTextAsync(expectedEnc));

        await RunAsync($"""
            DECRYPT FILE '{expectedEnc}' TO DIRECTORY '{decDir}' PASSWORD 'test-passphrase';
            """);

        var expectedDec = Path.Combine(decDir, "payroll.csv");
        Assert.True(File.Exists(expectedDec));
        Assert.Equal("id,amount\n1,42\n", await File.ReadAllTextAsync(expectedDec));

        // Non-.enc/.pgp filename with TO DIRECTORY must fail clearly rather than guessing.
        var ex = await Assert.ThrowsAsync<ExecutionException>(() => RunAsync($"""
            DECRYPT FILE '{srcFile}' TO DIRECTORY '{decDir}' PASSWORD 'test-passphrase';
            """));
        Assert.Contains("TO DIRECTORY", ex.Message);
        Assert.Contains(".pgp", ex.Message);
    }

    [Fact]
    public async Task CompressAndDecompressFile_ToDirectory_ZipAndGzip_RoundTrip()
    {
        var srcFile = Path.Combine(_root, "ledger.csv");
        await File.WriteAllTextAsync(srcFile, "acct,val\n100,99.5\n");

        var zipDir = Path.Combine(_root, "zip_out");
        var zipExtractDir = Path.Combine(_root, "zip_extracted");
        var gzDir = Path.Combine(_root, "gz_out");
        var gzExtractDir = Path.Combine(_root, "gz_extracted");

        await RunAsync($"""
            COMPRESS FILE '{srcFile}' TO DIRECTORY '{zipDir}';
            DECOMPRESS FILE '{Path.Combine(zipDir, "ledger.csv.zip")}' TO DIRECTORY '{zipExtractDir}';

            COMPRESS FILE '{srcFile}' TO DIRECTORY '{gzDir}' WITH (FORMAT = 'GZIP');
            DECOMPRESS FILE '{Path.Combine(gzDir, "ledger.csv.gz")}' TO DIRECTORY '{gzExtractDir}';
            """);

        Assert.True(File.Exists(Path.Combine(zipDir, "ledger.csv.zip")));
        Assert.Equal("acct,val\n100,99.5\n", await File.ReadAllTextAsync(Path.Combine(zipExtractDir, "ledger.csv")));

        Assert.True(File.Exists(Path.Combine(gzDir, "ledger.csv.gz")));
        Assert.Equal("acct,val\n100,99.5\n", await File.ReadAllTextAsync(Path.Combine(gzExtractDir, "ledger.csv")));
    }

    [Fact]
    public async Task EncryptFile_ToDirectory_WhatIf_DoesNotCreateDirectoryOrFile()
    {
        var srcFile = Path.Combine(_root, "draft.csv");
        await File.WriteAllTextAsync(srcFile, "a,b\n1,2\n");
        var whatIfDir = Path.Combine(_root, "whatif_out");

        await RunAsync($"""
            SET WHAT_IF ON;
            ENCRYPT FILE '{srcFile}' TO DIRECTORY '{whatIfDir}' PASSWORD 'test-passphrase';
            SET WHAT_IF OFF;
            """);

        Assert.False(Directory.Exists(whatIfDir));
    }
}
