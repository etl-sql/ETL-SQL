using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Handlers;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

// Review fixes for RT06 and RT08: names from a remote server must not escape the local target,
// and a missing remote source must not let DELETE_EXTRA empty the local destination.
public sealed class RemoteFileReviewFixTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"etlsql_rtfix_{Guid.NewGuid():N}");

    public RemoteFileReviewFixTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("../x.csv")]
    [InlineData("a/../../x.csv")]
    [InlineData(@"..\..\x.csv")]
    [InlineData("a//b.csv")]
    [InlineData("C:/x.csv")]
    public void RecursiveReceive_RefusesNamesThatAreNotPlainRelativePaths(string remoteRelative) =>
        Assert.Throws<SecurityException>(() => FileTransferStatementHandler.SafeRelativeLocalPath(remoteRelative, "sftp"));

    [Fact]
    public void RecursiveReceive_KeepsPlainNestedNames()
    {
        var rel = FileTransferStatementHandler.SafeRelativeLocalPath("a/b/c.csv", "sftp");
        Assert.Equal(Path.Combine("a", "b", "c.csv"), rel);
        FileTransferStatementHandler.EnsureUnderDirectory(Path.Combine(_tmpDir, rel), _tmpDir, "sftp");
        Assert.Throws<SecurityException>(() =>
            FileTransferStatementHandler.EnsureUnderDirectory(Path.Combine(_tmpDir, "..", "x.csv"), _tmpDir, "sftp"));
    }

    [Fact]
    public async Task RemoteToLocalSync_MissingRemoteSource_FailsAndKeepsLocalFiles()
    {
        var localDest = Path.Combine(_tmpDir, "mirror");
        Directory.CreateDirectory(localDest);
        var keep = Path.Combine(localDest, "keep.txt");
        File.WriteAllText(keep, "data");

        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();
        fs.Setup(r => r.DirectoryExistsAsync("/typo")).ReturnsAsync(false);
        ((IExecutionContext)evaluator).Connections["rem_missing"] = dsMock.Object;

        var sql = $"SYNC DIRECTORY '/typo' AT rem_missing TO '{localDest.Replace("\\", "/")}' WITH (DELETE_EXTRA = ON);";
        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse()));

        Assert.Contains("Source directory not found", ex.Message);
        Assert.True(File.Exists(keep));
    }
}
