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
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Collection("FTP collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "FTP")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class FtpIntegrationTests : IDisposable
    {
        private readonly FtpFixture _ftp;
        private readonly ITestOutputHelper _output;
        private readonly string _tmpDir;

        public FtpIntegrationTests(FtpFixture ftp, ITestOutputHelper output)
        {
            _ftp = ftp;
            _output = output;
            _tmpDir = Path.Combine(Path.GetTempPath(), $"ftp_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tmpDir);
        }

        // The first FTP test is occasionally refused right after the fixture's readiness probe
        // succeeded (issue #61), and the refusal does not say which port. Each client logs its FTP
        // conversation to the test output (shown only for a failed test), and a failure adds which
        // host ports accept and the server's own log.
        private FtpConnector Logged(FtpConnector connector)
        {
            connector.ConfigureClient(client => client.LegacyLogger = (level, message) =>
            {
                try { _output.WriteLine($"{DateTimeOffset.UtcNow:HH:mm:ss.fff} {level} {message}"); }
                catch (InvalidOperationException) { } // Output closed after the test finished.
            });
            return connector;
        }

        private async Task Diagnosed(Func<Task> test)
        {
            _output.WriteLine($"Test started {DateTimeOffset.UtcNow:O}, {(DateTimeOffset.UtcNow - _ftp.ReadyAt).TotalMilliseconds:F0} ms after the fixture was ready.");
            try { await test(); }
            catch
            {
                _output.WriteLine(await _ftp.DescribeAsync());
                throw;
            }
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

        private FtpConnector ValidConnector()
        {
            var c = new FtpConnector(MakeContext(), _ftp.Host, FtpFixture.TestUser, FtpFixture.TestPassword, _ftp.Port);
            // Docker passive-mode fix: the PASV response advertises the container's internal IP;
            // override it so the data connection always goes to the mapped host address instead.
            c.ConfigureClient(client => client.Config.AddressResolver = () => _ftp.Host);
            return Logged(c);
        }

        private FtpConnector WrongPasswordConnector() =>
            Logged(new FtpConnector(MakeContext(), _ftp.Host, FtpFixture.TestUser, "wrong-password", _ftp.Port));

        private string LocalPath(string filename) =>
            Path.Combine(_tmpDir, filename);

        [Fact]
        public Task ValidCredentials_ListRoot_Succeeds() => Diagnosed(async () =>
        {
            await using var connector = ValidConnector();

            var entries = await connector.ListFilesAsync("/").ToListAsync();

            Assert.NotNull(entries);
        });

        [Fact]
        public Task CreateDataSource_WithPortOption_ConnectsToMappedServer() => Diagnosed(async () =>
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["USER"] = FtpFixture.TestUser,
                ["PASSWORD"] = FtpFixture.TestPassword,
                ["PORT"] = _ftp.Port.ToString()
            };

            await using var connector = Logged((FtpConnector)new FtpConnector().CreateDataSource(MakeContext(), _ftp.Host, options));

            var entries = await connector.ListFilesAsync("/").ToListAsync();

            Assert.NotNull(entries);
        });

        [Fact]
        public Task UploadDownload_RoundTrip_ContentMatches() => Diagnosed(async () =>
        {
            var remoteName = $"roundtrip_{Guid.NewGuid():N}.txt";
            var localSource = LocalPath("source.txt");
            var localDestination = LocalPath("destination.txt");
            File.WriteAllText(localSource, "hello from FTP integration test");

            await using var connector = ValidConnector();

            await connector.UploadFileAsync(localSource, remoteName);
            await connector.DownloadFileAsync(remoteName, localDestination);

            Assert.Equal(File.ReadAllText(localSource), File.ReadAllText(localDestination));
        });

        [Fact]
        public Task WrongPassword_ListRoot_WrapsAsExecutionException() => Diagnosed(async () =>
        {
            await using var connector = WrongPasswordConnector();

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await connector.ListFilesAsync("/").ToListAsync());

            Assert.Contains("FTP", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(FtpFixture.TestPassword, ex.Message, StringComparison.Ordinal);
        });
    }
}
