using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class RemoteRecursiveTests : IDisposable
{
    private readonly string _tmpDir;

    public RemoteRecursiveTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"etlsql_rt06_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tmpDir))
                Directory.Delete(_tmpDir, recursive: true);
        }
        catch { }
    }

    private static async IAsyncEnumerable<FileMetaData> AsAsync(params FileMetaData[] items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RemoteFileList_Recursive_ExceedsDefaultDepthFive_FailsWithoutTruncating()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();

        // 6 nested levels: /root -> /root/l1 -> ... -> /root/l1/l2/l3/l4/l5/l6
        fs.Setup(r => r.ListFilesAsync("/root")).Returns(() => AsAsync(new FileMetaData { Name = "l1", FullPath = "/root/l1", IsDirectory = true, IsSymlink = false }));
        fs.Setup(r => r.ListFilesAsync("/root/l1")).Returns(() => AsAsync(new FileMetaData { Name = "l2", FullPath = "/root/l1/l2", IsDirectory = true, IsSymlink = false }));
        fs.Setup(r => r.ListFilesAsync("/root/l1/l2")).Returns(() => AsAsync(new FileMetaData { Name = "l3", FullPath = "/root/l1/l2/l3", IsDirectory = true, IsSymlink = false }));
        fs.Setup(r => r.ListFilesAsync("/root/l1/l2/l3")).Returns(() => AsAsync(new FileMetaData { Name = "l4", FullPath = "/root/l1/l2/l3/l4", IsDirectory = true, IsSymlink = false }));
        fs.Setup(r => r.ListFilesAsync("/root/l1/l2/l3/l4")).Returns(() => AsAsync(new FileMetaData { Name = "l5", FullPath = "/root/l1/l2/l3/l4/l5", IsDirectory = true, IsSymlink = false }));
        fs.Setup(r => r.ListFilesAsync("/root/l1/l2/l3/l4/l5")).Returns(() => AsAsync(new FileMetaData { Name = "l6", FullPath = "/root/l1/l2/l3/l4/l5/l6", IsDirectory = true, IsSymlink = false }));

        ((IExecutionContext)evaluator).Connections["rem_deep"] = dsMock.Object;

        const string sql = "SELECT * INTO #deep FROM REMOTE_FILE_LIST(rem_deep, '/root', TRUE);";
        await Assert.ThrowsAsync<SecurityException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse()));
    }

    [Fact]
    public async Task RemoteFileList_Recursive_DoesNotFollowSymlinkLoop()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();

        fs.Setup(r => r.ListFilesAsync("/root")).Returns(() => AsAsync(
            new FileMetaData { Name = "a.txt", FullPath = "/root/a.txt", Size = 5, IsDirectory = false, IsSymlink = false },
            new FileMetaData { Name = "loop", FullPath = "/root/loop", IsDirectory = true, IsSymlink = true }));

        ((IExecutionContext)evaluator).Connections["rem_sym"] = dsMock.Object;

        const string sql = "SELECT * INTO #sym FROM REMOTE_FILE_LIST(rem_sym, '/root', TRUE);";
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

        var rows = (await ((IExecutionContext)evaluator).Connections["#sym"].ReadBatches().ToListAsync())
            .SelectMany(b => b.Rows).ToList();
        Assert.Equal(2, rows.Count);
        fs.Verify(r => r.ListFilesAsync("/root/loop"), Times.Never);
    }

    [Fact]
    public async Task DeleteDirectoryAt_NonEmptyFailsWithoutRecursive_SucceedsWithRecursive()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();

        fs.Setup(r => r.ListFilesAsync("/dir")).Returns(() => AsAsync(
            new FileMetaData { Name = "sub", FullPath = "/dir/sub", IsDirectory = true, IsSymlink = false },
            new FileMetaData { Name = "top.txt", FullPath = "/dir/top.txt", IsDirectory = false, IsSymlink = false }));
        fs.Setup(r => r.ListFilesAsync("/dir/sub")).Returns(() => AsAsync(
            new FileMetaData { Name = "inner.txt", FullPath = "/dir/sub/inner.txt", IsDirectory = false, IsSymlink = false }));

        var deletedOrder = new List<string>();
        fs.Setup(r => r.DeleteFileAsync(It.IsAny<string>()))
            .Callback<string>(p => deletedOrder.Add("F:" + p))
            .Returns(Task.CompletedTask);
        fs.Setup(r => r.DeleteDirectoryAsync(It.IsAny<string>()))
            .Callback<string>(p => deletedOrder.Add("D:" + p))
            .Returns(Task.CompletedTask);

        ((IExecutionContext)evaluator).Connections["rem_del"] = dsMock.Object;

        // Without RECURSIVE: must fail before calling DeleteDirectoryAsync
        const string nonRecSql = "DELETE DIRECTORY '/dir' AT rem_del;";
        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(nonRecSql).Tokenize(), nonRecSql).Parse()));
        Assert.Contains("not empty", ex.Message, StringComparison.OrdinalIgnoreCase);
        fs.Verify(r => r.DeleteDirectoryAsync(It.IsAny<string>()), Times.Never);

        // With RECURSIVE = ON: deletes files, then sub, then /dir
        const string recSql = "DELETE DIRECTORY '/dir' AT rem_del WITH (RECURSIVE = ON);";
        await evaluator.Evaluate(new Parser(new Lexer(recSql).Tokenize(), recSql).Parse());

        Assert.Contains("F:/dir/top.txt", deletedOrder);
        Assert.Contains("F:/dir/sub/inner.txt", deletedOrder);
        Assert.True(deletedOrder.IndexOf("D:/dir/sub") < deletedOrder.IndexOf("D:/dir"));
    }

    [Fact]
    public async Task ReceiveFile_Recursive_PreservesSubfolderLayout()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();

        fs.Setup(r => r.ListFilesAsync("/in")).Returns(() => AsAsync(
            new FileMetaData { Name = "root.txt", FullPath = "/in/root.txt", IsDirectory = false, IsSymlink = false },
            new FileMetaData { Name = "nested", FullPath = "/in/nested", IsDirectory = true, IsSymlink = false }));
        fs.Setup(r => r.ListFilesAsync("/in/nested")).Returns(() => AsAsync(
            new FileMetaData { Name = "child.txt", FullPath = "/in/nested/child.txt", IsDirectory = false, IsSymlink = false }));
        fs.Setup(r => r.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Callback<string, string, bool>((remote, local, _) => File.WriteAllText(local, "from:" + remote))
            .Returns(Task.CompletedTask);

        ((IExecutionContext)evaluator).Connections["rem_rx"] = dsMock.Object;
        var localDest = _tmpDir.Replace("\\", "/");
        var sql = $"RECEIVE FILE FROM '/in' TO '{localDest}' AT rem_rx WITH (RECURSIVE = ON);";
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

        Assert.Equal("from:/in/root.txt", File.ReadAllText(Path.Combine(_tmpDir, "root.txt")));
        Assert.Equal("from:/in/nested/child.txt", File.ReadAllText(Path.Combine(_tmpDir, "nested", "child.txt")));
    }
}
