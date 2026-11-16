using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.App;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Functions;

public sealed class FilePathFunctionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "etlsql-filefn-" + Guid.NewGuid().ToString("N"));

    public FilePathFunctionsTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);
    private static string Esc(string s) => s.Replace("'", "''");

    private static Evaluator CreateEvaluator() =>
        DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();

    private static async Task<object?> EvalScalarAsync(string sql, Evaluator? evaluator = null)
    {
        evaluator ??= CreateEvaluator();
        var script = new Parser(new Lexer(sql).Tokenize(), sql).Parse();
        Assert.True(script.Diagnostics.Count == 0, string.Join("\n", script.Diagnostics.Select(d => d.Message)));
        await evaluator.Evaluate(script);
        return evaluator.Variables.TryGetValue("@res", out var value) ? value : null;
    }

    [Fact]
    public async Task PathFilenameNoExt_ExtractsExpectedStem()
    {
        Assert.Equal("report", await EvalScalarAsync("DECLARE @res VARCHAR = PATH_FILENAME_NO_EXT('/var/data/report.csv');"));
        Assert.Equal("archive.tar", await EvalScalarAsync("DECLARE @res VARCHAR = PATH_FILENAME_NO_EXT('C:\\exports\\archive.tar.gz');"));
        Assert.Equal("README", await EvalScalarAsync("DECLARE @res VARCHAR = PATH_FILENAME_NO_EXT('README');"));
        Assert.Null(await EvalScalarAsync("DECLARE @res VARCHAR = PATH_FILENAME_NO_EXT(NULL);"));
    }

    [Fact]
    public async Task PathChangeExtension_ReplacesAddsAndRemovesExtension()
    {
        Assert.Equal("orders.bak", await EvalScalarAsync("DECLARE @res VARCHAR = PATH_CHANGE_EXTENSION('orders.csv', '.bak');"));
        Assert.Equal("orders.bak", await EvalScalarAsync("DECLARE @res VARCHAR = PATH_CHANGE_EXTENSION('orders.csv', 'bak');"));
        Assert.Equal("orders", await EvalScalarAsync("DECLARE @res VARCHAR = PATH_CHANGE_EXTENSION('orders.csv', '');"));
        Assert.Equal("orders.csv", await EvalScalarAsync("DECLARE @res VARCHAR = PATH_CHANGE_EXTENSION('orders', '.csv');"));
        Assert.Null(await EvalScalarAsync("DECLARE @res VARCHAR = PATH_CHANGE_EXTENSION(NULL, '.csv');"));
        Assert.Null(await EvalScalarAsync("DECLARE @res VARCHAR = PATH_CHANGE_EXTENSION('orders.csv', NULL);"));
    }

    [Fact]
    public async Task FileCreated_ReturnsTimestampForExistingAndNullForMissing()
    {
        var path = PathFor("created.txt");
        await File.WriteAllTextAsync(path, "hello");
        var expected = File.GetCreationTime(path);

        var actual = await EvalScalarAsync($"DECLARE @res DATETIME = FILE_CREATED('{Esc(path)}');");
        var actualDt = Assert.IsType<DateTime>(actual);
        Assert.Equal(expected, actualDt);

        var missing = await EvalScalarAsync($"DECLARE @res DATETIME = FILE_CREATED('{Esc(PathFor("does_not_exist.txt"))}');");
        Assert.Null(missing);

        var nullArg = await EvalScalarAsync("DECLARE @res DATETIME = FILE_CREATED(NULL);");
        Assert.Null(nullArg);
    }

    [Fact]
    public async Task FileCreated_RespectsPathBoundary()
    {
        var ex = await Assert.ThrowsAsync<ETL_SQL.Services.SecurityException>(() =>
            EvalScalarAsync("DECLARE @res DATETIME = FILE_CREATED('/etc/passwd');"));
        Assert.Contains("Unauthorized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FileLineCount_HandlesEmptyAndLineEndingsAndHeaderRows()
    {
        var empty = PathFor("empty.txt");
        await File.WriteAllBytesAsync(empty, Array.Empty<byte>());
        Assert.Equal(0m, await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(empty)}');"));

        var lfTrailing = PathFor("lf_trailing.txt");
        await File.WriteAllTextAsync(lfTrailing, "h1,h2\n1,a\n2,b\n");
        Assert.Equal(3m, await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(lfTrailing)}');"));
        Assert.Equal(2m, await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(lfTrailing)}', 1);"));
        Assert.Equal(0m, await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(lfTrailing)}', 10);"));

        var crlfNoTrailing = PathFor("crlf_no_trailing.txt");
        await File.WriteAllTextAsync(crlfNoTrailing, "a\r\nb\r\nc");
        Assert.Equal(3m, await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(crlfNoTrailing)}');"));

        var crOnly = PathFor("cr_only.txt");
        await File.WriteAllTextAsync(crOnly, "x\ry\rz\r");
        Assert.Equal(3m, await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(crOnly)}');"));

        var withBlank = PathFor("with_blank.txt");
        await File.WriteAllTextAsync(withBlank, "a\n\nb\n");
        Assert.Equal(3m, await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(withBlank)}');"));
    }

    [Fact]
    public async Task FileLineCount_HandlesCrLfSplitAcrossReadBuffer()
    {
        // FileStream buffer in FileFunctions is 64 KiB; place \r at byte 65535 and \n at byte 65536.
        var splitFile = PathFor("crlf_split.txt");
        var bytes = new byte[65536 + 3];
        Array.Fill(bytes, (byte)'x');
        bytes[65535] = (byte)'\r';
        bytes[65536] = (byte)'\n';
        bytes[65537] = (byte)'y';
        bytes[65538] = (byte)'\n';
        await File.WriteAllBytesAsync(splitFile, bytes);

        Assert.Equal(2m, await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(splitFile)}');"));
    }

    [Fact]
    public async Task FileLineCount_NullMissingAndInvalidHeaderRows()
    {
        var missing = PathFor("missing.txt");
        Assert.Null(await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(missing)}');"));
        Assert.Null(await EvalScalarAsync("DECLARE @res INT = FILE_LINE_COUNT(NULL);"));

        var existing = PathFor("one.txt");
        await File.WriteAllTextAsync(existing, "only");
        Assert.Null(await EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(existing)}', NULL);"));

        await Assert.ThrowsAsync<ExecutionException>(() =>
            EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(existing)}', -1);"));
        await Assert.ThrowsAsync<ExecutionException>(() =>
            EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(existing)}', 1.5);"));
        await Assert.ThrowsAsync<ExecutionException>(() =>
            EvalScalarAsync($"DECLARE @res INT = FILE_LINE_COUNT('{Esc(existing)}', 'abc');"));
    }

    [Fact]
    public async Task RemoteMetadataAndDirectoryExistsFunctions_ReturnExpectedValues()
    {
        var modified = new DateTime(2026, 5, 1, 14, 30, 0, DateTimeKind.Utc);
        var entries = new Dictionary<string, FileMetaData>(StringComparer.Ordinal)
        {
            ["inbox/orders.csv"] = new FileMetaData
            {
                Name = "orders.csv",
                FullPath = "inbox/orders.csv",
                Size = 4096,
                LastModified = modified,
                IsDirectory = false
            },
            ["inbox/no_mtime.bin"] = new FileMetaData
            {
                Name = "no_mtime.bin",
                FullPath = "inbox/no_mtime.bin",
                Size = 128,
                LastModified = DateTime.MinValue,
                IsDirectory = false
            }
        };

        var mock = new Mock<IDataSource>();
        var remote = mock.As<IRemoteFileSystem>();
        remote.Setup(r => r.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string p) => entries.ContainsKey(p));
        remote.Setup(r => r.DirectoryExistsAsync(It.IsAny<string>()))
            .ReturnsAsync((string p) => string.Equals(p.TrimEnd('/'), "inbox", StringComparison.Ordinal));
        remote.Setup(r => r.ListFilesAsync(It.IsAny<string>()))
            .Returns((string dir) => AsyncEnum(entries.Values.Where(v =>
                dir == "" || v.FullPath.StartsWith(dir, StringComparison.Ordinal))));

        var evaluator = CreateEvaluator();
        evaluator.Connections["REM"] = mock.Object;

        Assert.Equal(4096m, await EvalScalarAsync("DECLARE @res DECIMAL = REMOTE_FILE_SIZE('REM', 'inbox/orders.csv');", evaluator));
        Assert.Equal(modified, await EvalScalarAsync("DECLARE @res DATETIME = REMOTE_FILE_MODIFIED('REM', 'inbox/orders.csv');", evaluator));
        Assert.Null(await EvalScalarAsync("DECLARE @res DATETIME = REMOTE_FILE_MODIFIED('REM', 'inbox/no_mtime.bin');", evaluator));
        Assert.Null(await EvalScalarAsync("DECLARE @res DECIMAL = REMOTE_FILE_SIZE('REM', 'inbox/missing.csv');", evaluator));
        Assert.Null(await EvalScalarAsync("DECLARE @res DATETIME = REMOTE_FILE_MODIFIED('REM', 'inbox/missing.csv');", evaluator));

        Assert.Equal(1m, await EvalScalarAsync("DECLARE @res INT = REMOTE_DIRECTORY_EXISTS('REM', 'inbox');", evaluator));
        Assert.Equal(0m, await EvalScalarAsync("DECLARE @res INT = REMOTE_DIRECTORY_EXISTS('REM', 'inbox/orders.csv');", evaluator));
        Assert.Equal(0m, await EvalScalarAsync("DECLARE @res INT = REMOTE_DIRECTORY_EXISTS('REM', 'nope');", evaluator));

        await Assert.ThrowsAsync<ExecutionException>(() =>
            EvalScalarAsync("DECLARE @res INT = REMOTE_DIRECTORY_EXISTS('UNKNOWN_CONN', 'inbox');", evaluator));
    }

    private static async IAsyncEnumerable<FileMetaData> AsyncEnum(IEnumerable<FileMetaData> items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }
}
