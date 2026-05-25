using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Collection("FTP collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "FTP")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class FtpIntegrationTests : IDisposable
    {
        private readonly FtpFixture _ftp;
        private readonly string _tmpDir;

        public FtpIntegrationTests(FtpFixture ftp)
        {
            _ftp = ftp;
            _tmpDir = Path.Combine(Path.GetTempPath(), $"ftp_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tmpDir))
            {
                Directory.Delete(_tmpDir, recursive: true);
            }
        }

        private static IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        private FtpConnector ValidConnector() =>
            new FtpConnector(MakeContext(), _ftp.Host, FtpFixture.TestUser, FtpFixture.TestPassword, _ftp.Port);

        private FtpConnector WrongPasswordConnector() =>
            new FtpConnector(MakeContext(), _ftp.Host, FtpFixture.TestUser, "wrong-password", _ftp.Port);

        private string LocalPath(string filename) =>
            Path.Combine(_tmpDir, filename);

        [Fact]
        public async Task ValidCredentials_ListRoot_Succeeds()
        {
            await using var connector = ValidConnector();

            var entries = await connector.ListFilesAsync("/").ToListAsync();

            Assert.NotNull(entries);
        }

        [Fact]
        public async Task CreateDataSource_WithPortOption_ConnectsToMappedServer()
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["USER"] = FtpFixture.TestUser,
                ["PASSWORD"] = FtpFixture.TestPassword,
                ["PORT"] = _ftp.Port.ToString()
            };

            await using var connector = (FtpConnector)new FtpConnector().CreateDataSource(MakeContext(), _ftp.Host, options);

            var entries = await connector.ListFilesAsync("/").ToListAsync();

            Assert.NotNull(entries);
        }

        [Fact]
        public async Task UploadDownload_RoundTrip_ContentMatches()
        {
            var remoteName = $"roundtrip_{Guid.NewGuid():N}.txt";
            var localSource = LocalPath("source.txt");
            var localDestination = LocalPath("destination.txt");
            File.WriteAllText(localSource, "hello from FTP integration test");

            await using var connector = ValidConnector();

            await connector.UploadFileAsync(localSource, remoteName);
            await connector.DownloadFileAsync(remoteName, localDestination);

            Assert.Equal(File.ReadAllText(localSource), File.ReadAllText(localDestination));
        }

        [Fact]
        public async Task WrongPassword_ListRoot_WrapsAsExecutionException()
        {
            await using var connector = WrongPasswordConnector();

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await connector.ListFilesAsync("/").ToListAsync());

            Assert.Contains("FTP", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(FtpFixture.TestPassword, ex.Message, StringComparison.Ordinal);
        }
    }
}
