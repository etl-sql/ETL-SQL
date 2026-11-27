using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class RemoteCopyFileTests : IDisposable
{
    private readonly string _tempRoot;

    public RemoteCopyFileTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"etlsql_rt04_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task CopyRemoteFileAsync_ServerSideSupported_DoesNotDownloadOrUpload()
    {
        var fs = new Mock<IRemoteFileSystem>();
        fs.Setup(f => f.CopyFileAsync("/a.csv", "/b.csv", true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await FileOperationStatementHandler.CopyRemoteFileAsync(
            fs.Object, "/a.csv", fs.Object, "/b.csv", overwrite: true, _tempRoot, CancellationToken.None);

        fs.Verify(f => f.CopyFileAsync("/a.csv", "/b.csv", true, It.IsAny<CancellationToken>()), Times.Once);
        fs.Verify(f => f.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        fs.Verify(f => f.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyRemoteFileAsync_OverwriteOffExistingTarget_FailsBeforeAnyByteMoves()
    {
        var fs = new Mock<IRemoteFileSystem>();
        fs.Setup(f => f.FileExistsAsync("/existing.csv")).ReturnsAsync(true);

        await Assert.ThrowsAsync<ExecutionException>(() =>
            FileOperationStatementHandler.CopyRemoteFileAsync(
                fs.Object, "/src.csv", fs.Object, "/existing.csv", overwrite: false, _tempRoot, CancellationToken.None));

        fs.Verify(f => f.CopyFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fs.Verify(f => f.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        fs.Verify(f => f.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyRemoteFileAsync_FailedUpload_DeletesTempFile()
    {
        var fs = new Mock<IRemoteFileSystem>();
        fs.Setup(f => f.CopyFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException());
        fs.Setup(f => f.DownloadFileAsync("/src.csv", It.IsAny<string>(), true))
            .Callback<string, string, bool>((_, local, _) => File.WriteAllText(local, "payload-bytes"))
            .Returns(Task.CompletedTask);
        fs.Setup(f => f.UploadFileAsync(It.IsAny<string>(), "/dest.csv", true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExecutionException("Simulated upload failure"));

        await Assert.ThrowsAsync<ExecutionException>(() =>
            FileOperationStatementHandler.CopyRemoteFileAsync(
                fs.Object, "/src.csv", fs.Object, "/dest.csv", overwrite: true, _tempRoot, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(_tempRoot));
    }

    [Fact]
    public async Task CopyFileAt_WhatIf_LogsWithoutTouchingRemote()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var fs = new Mock<IDataSource>();
        var remoteFs = fs.As<IRemoteFileSystem>();
        ((IExecutionContext)evaluator).Connections["rem_whatif"] = fs.Object;

        const string sql =
            "SET WHAT_IF ON;\n" +
            "COPY FILE '/in/a.csv' TO '/out/b.csv' AT rem_whatif WITH (OVERWRITE = OFF);\n" +
            "SET WHAT_IF OFF;";
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

        remoteFs.Verify(f => f.FileExistsAsync(It.IsAny<string>()), Times.Never);
        remoteFs.Verify(f => f.CopyFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        remoteFs.Verify(f => f.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        remoteFs.Verify(f => f.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
