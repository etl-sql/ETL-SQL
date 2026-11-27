using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
public sealed class RemoteTransferVerifyTests : IDisposable
{
    private readonly string _tmpDir;

    public RemoteTransferVerifyTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"etlsql_rt09_{Guid.NewGuid():N}");
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

    [Fact]
    public async Task ReceiveFile_VerifyOn_SizeMismatchFailsAndRemovesPartialLocalFile()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();

        var localDest = Path.Combine(_tmpDir, "truncated.csv");
        // Simulate a truncated download (writes 5 bytes while remote reports 100 bytes)
        fs.Setup(r => r.DownloadFileAsync("/data/file.csv", It.IsAny<string>(), true))
            .Callback<string, string, bool>((_, local, _) => File.WriteAllText(local, "12345"))
            .Returns(Task.CompletedTask);
        fs.Setup(r => r.GetFileVerificationInfoAsync("/data/file.csv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteFileVerificationInfo(Size: 100, Md5Hex: null));

        ((IExecutionContext)evaluator).Connections["rem_ver"] = dsMock.Object;

        var sql = $"RECEIVE FILE FROM '/data/file.csv' TO '{localDest.Replace("\\", "/")}' AT rem_ver WITH (VERIFY = ON);";
        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse()));

        Assert.Contains("size mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(localDest));
    }

    [Fact]
    public async Task ReceiveFile_VerifyOn_Md5MismatchFailsAndRemovesPartialLocalFile()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();

        var localDest = Path.Combine(_tmpDir, "corrupt.csv");
        var payload = "exact-same-len";
        fs.Setup(r => r.DownloadFileAsync("/data/corrupt.csv", It.IsAny<string>(), true))
            .Callback<string, string, bool>((_, local, _) => File.WriteAllText(local, payload))
            .Returns(Task.CompletedTask);
        fs.Setup(r => r.GetFileVerificationInfoAsync("/data/corrupt.csv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteFileVerificationInfo(Size: Encoding.UTF8.GetByteCount(payload), Md5Hex: "00000000000000000000000000000000"));

        ((IExecutionContext)evaluator).Connections["rem_md5"] = dsMock.Object;

        var sql = $"RECEIVE FILE FROM '/data/corrupt.csv' TO '{localDest.Replace("\\", "/")}' AT rem_md5 WITH (VERIFY = ON);";
        var ex = await Assert.ThrowsAsync<ExecutionException>(() =>
            evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse()));

        Assert.Contains("MD5", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(localDest));
    }

    [Fact]
    public async Task SendFile_VerifyOn_MatchingSizeAndMd5_Succeeds()
    {
        var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
        var dsMock = new Mock<IDataSource>();
        var fs = dsMock.As<IRemoteFileSystem>();

        var localSrc = Path.Combine(_tmpDir, "good.csv");
        var content = "id,name\n1,Alice\n";
        File.WriteAllText(localSrc, content);
        var expectedMd5 = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(localSrc))).ToLowerInvariant();
        var expectedSize = new FileInfo(localSrc).Length;

        fs.Setup(r => r.UploadFileAsync(It.IsAny<string>(), "/out/good.csv", true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fs.Setup(r => r.GetFileVerificationInfoAsync("/out/good.csv", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RemoteFileVerificationInfo(expectedSize, expectedMd5));

        ((IExecutionContext)evaluator).Connections["rem_ok"] = dsMock.Object;

        var sql = $"SEND FILE '{localSrc.Replace("\\", "/")}' TO '/out/good.csv' AT rem_ok WITH (OVERWRITE = ON, VERIFY = ON);";
        await evaluator.Evaluate(new Parser(new Lexer(sql).Tokenize(), sql).Parse());

        fs.Verify(r => r.GetFileVerificationInfoAsync("/out/good.csv", It.IsAny<CancellationToken>()), Times.Once);
    }
}
