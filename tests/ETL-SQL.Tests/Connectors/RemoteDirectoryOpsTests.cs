using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
public sealed class RemoteDirectoryOpsTests
{
    private static async IAsyncEnumerable<FileMetaData> AsAsync(params FileMetaData[] items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CopyDirectoryAt_PartialFailure_ReportsCopiedFilesAndDoesNotDeleteThem()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        dsMock.Setup(d => d.ConnectorType).Returns("SFTP");
        var fs = dsMock.As<IRemoteFileSystem>();

        fs.Setup(r => r.ListFilesAsync("/src")).Returns(() => AsAsync(
            new FileMetaData { Name = "first.csv", FullPath = "/src/first.csv", IsDirectory = false, IsSymlink = false },
            new FileMetaData { Name = "second.csv", FullPath = "/src/second.csv", IsDirectory = false, IsSymlink = false }));
        fs.Setup(r => r.CreateDirectoryAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        fs.Setup(r => r.CopyFileAsync("/src/first.csv", "/dst/first.csv", true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fs.Setup(r => r.CopyFileAsync("/src/second.csv", "/dst/second.csv", true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExecutionException("disk full"));

        ((IExecutionContext)evaluator).Connections["rem_cp"] = dsMock.Object;

        const string sql = "COPY DIRECTORY '/src' TO '/dst' AT rem_cp;";
        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse()));

        Assert.Contains("/dst/first.csv", ex.Message, StringComparison.Ordinal);
        Assert.Contains("not deleted", ex.Message, StringComparison.OrdinalIgnoreCase);
        fs.Verify(r => r.DeleteFileAsync(It.IsAny<string>()), Times.Never);
        fs.Verify(r => r.DeleteDirectoryAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RenameDirectoryAt_UnsupportedConnector_ThrowsClearMessage()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        dsMock.Setup(d => d.ConnectorType).Returns("S3");
        var fs = dsMock.As<IRemoteFileSystem>();
        fs.Setup(r => r.RenameDirectoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException());

        ((IExecutionContext)evaluator).Connections["s3_ren"] = dsMock.Object;

        const string sql = "RENAME DIRECTORY 'in' TO 'out' AT s3_ren;";
        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse()));

        Assert.Contains("not supported on S3", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectoryOpsAt_WhatIf_TouchesNothing()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();
        ((IExecutionContext)evaluator).Connections["rem_wi"] = dsMock.Object;

        const string sql =
            "SET WHAT_IF ON;\n" +
            "RENAME DIRECTORY '/a' TO 'b' AT rem_wi;\n" +
            "MOVE DIRECTORY '/a' TO '/c' AT rem_wi;\n" +
            "COPY DIRECTORY '/a' TO '/d' AT rem_wi;\n" +
            "SET WHAT_IF OFF;";
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

        fs.Verify(r => r.RenameDirectoryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fs.Verify(r => r.CreateDirectoryAsync(It.IsAny<string>()), Times.Never);
        fs.Verify(r => r.ListFilesAsync(It.IsAny<string>()), Times.Never);
    }
}
