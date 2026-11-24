using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class RemoteSyncDirectoryTests : IDisposable
{
    private readonly string _tmpDir;

    public RemoteSyncDirectoryTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"etlsql_rt08_{Guid.NewGuid():N}");
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
    public async Task SyncDirectory_WhatIf_PrintsPlanAndTouchesNothing()
    {
        var localSrc = Path.Combine(_tmpDir, "src");
        Directory.CreateDirectory(localSrc);
        File.WriteAllText(Path.Combine(localSrc, "new.txt"), "hello");

        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();
        fs.Setup(r => r.DirectoryExistsAsync("/rem")).ReturnsAsync(true);
        fs.Setup(r => r.ListFilesAsync("/rem")).Returns(() => AsAsync(
            new FileMetaData { Name = "extra.txt", FullPath = "/rem/extra.txt", Size = 10, LastModified = DateTime.UtcNow, IsDirectory = false }));

        ((IExecutionContext)evaluator).Connections["rem_sync"] = dsMock.Object;

        var sql =
            $"SET WHAT_IF ON;\n" +
            $"SYNC DIRECTORY '{localSrc.Replace("\\", "/")}' TO '/rem' AT rem_sync WITH (DELETE_EXTRA = ON, RECURSIVE = ON);\n" +
            $"SET WHAT_IF OFF;";
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

        fs.Verify(r => r.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fs.Verify(r => r.DeleteFileAsync(It.IsAny<string>()), Times.Never);
        fs.Verify(r => r.CreateDirectoryAsync(It.IsAny<string>()), Times.Never);
    }
}
