using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
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

    [Fact]
    public async Task EncryptAndCompressFile_ToDirectoryConnection_WritesInsideConnectionPath()
    {
        var srcFile = Path.Combine(_root, "orders.csv");
        await File.WriteAllTextAsync(srcFile, "id,total\n1,100\n");
        var connDir = Path.Combine(_root, "dir_conn_out");
        Directory.CreateDirectory(connDir);

        await RunAsync($"""
            CREATE CONNECTION my_dir AS DIRECTORY (PATH = '{connDir}');
            ENCRYPT FILE '{srcFile}' TO my_dir PASSWORD 'test-passphrase';
            COMPRESS FILE '{srcFile}' TO my_dir WITH (FORMAT = 'GZIP');
            """);

        Assert.True(File.Exists(Path.Combine(connDir, "orders.csv.enc")));
        Assert.True(File.Exists(Path.Combine(connDir, "orders.csv.gz")));
    }

    [Theory]
    [InlineData("ENCRYPT FILE '{0}' TO my_sftp PASSWORD 'secret';", "ENCRYPT FILE")]
    [InlineData("DECRYPT FILE '{0}' TO my_sftp PASSWORD 'secret';", "DECRYPT FILE")]
    [InlineData("COMPRESS FILE '{0}' TO my_sftp;", "COMPRESS FILE")]
    [InlineData("DECOMPRESS FILE '{0}' TO my_sftp;", "DECOMPRESS FILE")]
    public async Task CryptoAndArchiveOps_ToRemoteConnection_FailClosedWithoutUploading(string sqlTemplate, string opName)
    {
        var srcFile = Path.Combine(_root, "secret.csv");
        await File.WriteAllTextAsync(srcFile, "ssn\n123-45-6789\n");

        var remoteMock = new Mock<IDataSource>();
        remoteMock.SetupGet(d => d.ConnectorType).Returns("SFTP");
        remoteMock.SetupGet(d => d.Path).Returns("/inbound");
        var remoteFsMock = remoteMock.As<IRemoteFileSystem>();

        var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
        eval.Connections["my_sftp"] = remoteMock.Object;

        var sql = string.Format(sqlTemplate, srcFile);
        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            eval.Evaluate(new Parser(new Lexer(sql).Tokenize()).Parse()));

        Assert.Contains(opName, ex.Message);
        Assert.Contains("remote connection", ex.Message, StringComparison.OrdinalIgnoreCase);
        remoteFsMock.Verify(
            r => r.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
