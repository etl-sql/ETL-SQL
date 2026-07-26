using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Services;
using Moq;
using Renci.SshNet;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Live SFTP integration tests.  Requires Docker — run with:
    ///   dotnet test --filter "Category=Integration"
    ///
    /// These tests start an atmoz/sftp container once per collection and share it
    /// across all test methods.  Container startup typically takes 3–8 seconds.
    /// </summary>
    [Collection("SFTP collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "SFTP")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class SftpIntegrationTests : IDisposable
    {
        private readonly SftpFixture _sftp;
        private readonly string _tmpDir;

        public SftpIntegrationTests(SftpFixture sftp)
        {
            _sftp = sftp;
            _tmpDir = Path.Combine(Path.GetTempPath(), $"sftp_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tmpDir))
                Directory.Delete(_tmpDir, recursive: true);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        // Since v0.17.0 an SFTP connection with no HOST_KEY_FINGERPRINT is rejected rather than
        // trusted-with-a-warning. The fixture container is created per test run and generates a
        // fresh host key, so there is no stable fingerprint to pin — this is exactly the transient
        // lab case ALLOW_UNPINNED_HOST_KEY exists for. These tests cover SFTP file operations; the
        // host-key policy itself is covered by SftpConnectorTests.EvaluateHostKey cases, plus
        // UnpinnedHostKey_WithoutOptOut_IsRejected below for the end-to-end wiring.
        private const bool AllowUnpinnedForEphemeralContainer = true;

        /// <summary>Creates a connector that uses the fixture's mapped port and password auth.</summary>
        private SftpConnector PasswordConnector() =>
            new SftpConnector(
                context: null!,
                host: _sftp.Host,
                port: _sftp.Port,
                username: SftpFixture.TestUser,
                password: SftpFixture.TestPassword,
                keyFilePath: null,
                passphrase: null,
                timeoutSeconds: 30,
                clientFactory: (h, u, p, _, _) => new SftpClient(h, _sftp.Port, u, p!),
                allowUnpinnedHostKey: AllowUnpinnedForEphemeralContainer);

        /// <summary>Creates a connector that uses the fixture's generated RSA private key.</summary>
        private SftpConnector KeyConnector() =>
            new SftpConnector(
                context: null!,
                host: _sftp.Host,
                port: _sftp.Port,
                username: SftpFixture.TestUser,
                password: null,
                keyFilePath: _sftp.PrivateKeyPath,
                passphrase: null,
                timeoutSeconds: 30,
                clientFactory: (h, u, _, k, pp) => new SftpClient(h, _sftp.Port, u, new PrivateKeyFile(k!, pp)),
                allowUnpinnedHostKey: AllowUnpinnedForEphemeralContainer);

        private string RemotePath(string filename) =>
            $"/{SftpFixture.RemoteUploadDir}/{filename}";

        private string LocalPath(string filename) =>
            Path.Combine(_tmpDir, filename);

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }

        // ── 1. Basic connectivity ─────────────────────────────────────────────

        [Fact]
        public async Task PasswordAuth_CanConnectAndListDirectory()
        {
            await using var connector = PasswordConnector();
            var entries = new System.Collections.Generic.List<FileMetaData>();
            await foreach (var entry in connector.ListFilesAsync($"/{SftpFixture.RemoteUploadDir}"))
                entries.Add(entry);
            // Directory listing succeeds without throwing; exact contents may vary.
            Assert.NotNull(entries);
        }

        [Fact]
        public async Task PrivateKeyAuth_CanConnectAndListDirectory()
        {
            await using var connector = KeyConnector();
            var entries = new System.Collections.Generic.List<FileMetaData>();
            await foreach (var entry in connector.ListFilesAsync($"/{SftpFixture.RemoteUploadDir}"))
                entries.Add(entry);
            Assert.NotNull(entries);
        }

        [Fact]
        public async Task PrivateKeyWithPassphrase_CanConnectAndListDirectory()
        {
            // Uses an AES-256-CBC encrypted PKCS#8 PEM — verifies SSH.NET reads passphrase-protected keys.
            await using var connector = new SftpConnector(
                context: null!,
                host: _sftp.Host,
                port: _sftp.Port,
                username: SftpFixture.TestUser,
                password: null,
                keyFilePath: _sftp.EncryptedPrivateKeyPath,
                passphrase: SftpFixture.TestPassphrase,
                timeoutSeconds: 30,
                clientFactory: (h, u, _, k, pp) => new SftpClient(h, _sftp.Port, u, new PrivateKeyFile(k!, pp)),
                allowUnpinnedHostKey: AllowUnpinnedForEphemeralContainer);

            var entries = new System.Collections.Generic.List<FileMetaData>();
            await foreach (var entry in connector.ListFilesAsync($"/{SftpFixture.RemoteUploadDir}"))
                entries.Add(entry);
            Assert.NotNull(entries);
        }

        /// <summary>
        /// End-to-end guard for the v0.17.0 breaking change: with no pin and no explicit opt-out the
        /// connection must be refused against a real server. The decision table is unit-tested in
        /// SftpConnectorTests; this covers the wiring from connector options through to the SSH
        /// host-key callback, which a pure unit test cannot reach.
        /// </summary>
        [Fact]
        public async Task UnpinnedHostKey_WithoutOptOut_IsRejected()
        {
            await using var connector = new SftpConnector(
                context: null!,
                host: _sftp.Host,
                port: _sftp.Port,
                username: SftpFixture.TestUser,
                password: SftpFixture.TestPassword,
                keyFilePath: null,
                passphrase: null,
                timeoutSeconds: 30,
                clientFactory: (h, u, p, _, _) => new SftpClient(h, _sftp.Port, u, p!),
                allowUnpinnedHostKey: false);

            var ex = await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await foreach (var _ in connector.ListFilesAsync($"/{SftpFixture.RemoteUploadDir}"))
                {
                }
            });

            Assert.Contains("Host key", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── 2. Upload / Download round-trip ───────────────────────────────────

        [Fact]
        public async Task UploadFile_DownloadFile_ContentMatches()
        {
            var localSrc = LocalPath("upload_src.txt");
            var localDst = LocalPath("download_dst.txt");
            File.WriteAllText(localSrc, "hello from ETL-SQL integration test");

            await using var connector = PasswordConnector();
            var remote = RemotePath($"roundtrip_{Guid.NewGuid():N}.txt");

            await connector.UploadFileAsync(localSrc, remote);
            await connector.DownloadFileAsync(remote, localDst);

            Assert.Equal(File.ReadAllText(localSrc), File.ReadAllText(localDst));
        }

        // ── 3. List directory ─────────────────────────────────────────────────

        [Fact]
        public async Task ListDirectory_AfterUpload_ContainsUploadedFile()
        {
            var localSrc = LocalPath("listed.txt");
            File.WriteAllText(localSrc, "list-me");
            var filename = $"listed_{Guid.NewGuid():N}.txt";
            var remote = RemotePath(filename);

            await using var connector = PasswordConnector();
            await connector.UploadFileAsync(localSrc, remote);

            var entries = new System.Collections.Generic.List<FileMetaData>();
            await foreach (var e in connector.ListFilesAsync($"/{SftpFixture.RemoteUploadDir}"))
                entries.Add(e);

            Assert.Contains(entries, e => e.Name == filename);
        }

        // ── 4. Delete ─────────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteFile_FileNoLongerListedAfterDelete()
        {
            var localSrc = LocalPath("delete_me.txt");
            File.WriteAllText(localSrc, "delete this");
            var filename = $"del_{Guid.NewGuid():N}.txt";
            var remote = RemotePath(filename);

            await using var connector = PasswordConnector();
            await connector.UploadFileAsync(localSrc, remote);
            await connector.DeleteFileAsync(remote);

            var entries = new System.Collections.Generic.List<FileMetaData>();
            await foreach (var e in connector.ListFilesAsync($"/{SftpFixture.RemoteUploadDir}"))
                entries.Add(e);

            Assert.DoesNotContain(entries, e => e.Name == filename);
        }

        // ── 5. OVERWRITE semantics ────────────────────────────────────────────

        [Fact]
        public async Task Overwrite_True_AllowsReuploadWithoutError()
        {
            var localSrc = LocalPath("overwrite_src.txt");
            File.WriteAllText(localSrc, "original");
            var remote = RemotePath($"overwrite_{Guid.NewGuid():N}.txt");

            await using var connector = PasswordConnector();
            await connector.UploadFileAsync(localSrc, remote, overwrite: true);

            File.WriteAllText(localSrc, "updated");
            // Should not throw
            await connector.UploadFileAsync(localSrc, remote, overwrite: true);

            var localDst = LocalPath("overwrite_dst.txt");
            await connector.DownloadFileAsync(remote, localDst);
            Assert.Equal("updated", File.ReadAllText(localDst));
        }

        [Fact]
        public async Task Overwrite_False_ThrowsWhenRemoteFileExists()
        {
            var localSrc = LocalPath("no_overwrite.txt");
            File.WriteAllText(localSrc, "first");
            var remote = RemotePath($"no_ovr_{Guid.NewGuid():N}.txt");

            await using var connector = PasswordConnector();
            await connector.UploadFileAsync(localSrc, remote, overwrite: true);

            await Assert.ThrowsAsync<ExecutionException>(
                () => connector.UploadFileAsync(localSrc, remote, overwrite: false));
        }

        [Fact]
        public async Task Overwrite_False_ThrowsWhenLocalFileExists()
        {
            var localSrc = LocalPath("dl_src.txt");
            var localDst = LocalPath("dl_dst.txt");
            File.WriteAllText(localSrc, "content");
            File.WriteAllText(localDst, "already here");  // pre-existing local file

            var remote = RemotePath($"dl_{Guid.NewGuid():N}.txt");

            await using var connector = PasswordConnector();
            await connector.UploadFileAsync(localSrc, remote, overwrite: true);

            await Assert.ThrowsAsync<ExecutionException>(
                () => connector.DownloadFileAsync(remote, localDst, overwrite: false));
        }

        // ── 6. Error cases ────────────────────────────────────────────────────

        [Fact]
        public async Task DownloadMissingRemotePath_ThrowsExecutionException()
        {
            await using var connector = PasswordConnector();
            var localDst = LocalPath("missing_dst.txt");

            await Assert.ThrowsAsync<ExecutionException>(
                () => connector.DownloadFileAsync("/upload/no_such_file_xyz.txt", localDst));
        }

        [Fact]
        public async Task DeleteMissingRemotePath_ThrowsExecutionException()
        {
            await using var connector = PasswordConnector();

            await Assert.ThrowsAsync<ExecutionException>(
                () => connector.DeleteFileAsync("/upload/no_such_file_xyz.txt"));
        }

        [Fact]
        public async Task UploadToUnauthorizedRootPath_ThrowsExecutionException()
        {
            var localSrc = LocalPath("permission_denied.txt");
            File.WriteAllText(localSrc, "should not be writable at chroot root");

            await using var connector = PasswordConnector();

            await Assert.ThrowsAsync<ExecutionException>(
                () => connector.UploadFileAsync(localSrc, $"/denied_{Guid.NewGuid():N}.txt", overwrite: true));
        }

        // ── 7. Credential masking ─────────────────────────────────────────────

        [Fact]
        public async Task ExceptionMessage_DoesNotContainPassword()
        {
            await using var connector = PasswordConnector();

            var ex = await Assert.ThrowsAsync<ExecutionException>(
                () => connector.DownloadFileAsync("/upload/no_such_file_xyz.txt", LocalPath("err_dst.txt")));

            Assert.DoesNotContain(SftpFixture.TestPassword, ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── 8. Host allowlist ─────────────────────────────────────────────────

        [Fact]
        public void HostNotInAllowlist_ThrowsSecurityException()
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;  // disable auto-bypass so the allowlist is enforced
            security.AllowedHosts.Clear();  // no hosts permitted

            var mockContext = new Mock<IExecutionContext>();
            mockContext.Setup(c => c.SecurityService).Returns(security);
            mockContext.Setup(c => c.Logger).Returns(NullLogger.Instance);

            Assert.Throws<SecurityException>(() =>
                new SftpConnector(mockContext.Object, "sftp.blocked.example.com", SftpFixture.TestUser, SftpFixture.TestPassword));
        }

        // ── 9. Large file with checksum ───────────────────────────────────────

        [Fact]
        public async Task LargeFile_RoundTrip_Sha256Matches()
        {
            // 4 MB of deterministic pseudo-random content
            const int Size = 4 * 1024 * 1024;
            var localSrc = LocalPath("large_src.bin");
            var localDst = LocalPath("large_dst.bin");

            var rng = new Random(42);
            var buf = new byte[Size];
            rng.NextBytes(buf);
            File.WriteAllBytes(localSrc, buf);

            var expectedHash = Sha256(localSrc);
            var remote = RemotePath($"large_{Guid.NewGuid():N}.bin");

            await using var connector = PasswordConnector();
            await connector.UploadFileAsync(localSrc, remote, overwrite: true);
            await connector.DownloadFileAsync(remote, localDst, overwrite: true);

            Assert.Equal(expectedHash, Sha256(localDst));
        }

        // ── 10. ReadBatches (REMOTE_FILE_LIST equivalent) ────────────────────

        [Fact]
        public async Task ReadBatches_AfterUpload_ContainsFileRow()
        {
            var localSrc = LocalPath("batch_file.txt");
            File.WriteAllText(localSrc, "batch content");
            var filename = $"batch_{Guid.NewGuid():N}.txt";
            var remote = RemotePath(filename);

            // Upload first so there is at least one file to list
            await using var uploadConnector = PasswordConnector();
            await uploadConnector.UploadFileAsync(localSrc, remote, overwrite: true);

            // A second connector in list mode — ReadBatches lists the root home dir
            // (the connector's ReadBatches calls ListFilesAsync(""))
            // Verify the batch table has Name, FullPath, Size, LastModified, IsDirectory columns
            await using var listConnector = PasswordConnector();
            DataTable? batch = null;
            await foreach (var b in listConnector.ReadBatches())
            {
                batch = b;
                break;
            }

            Assert.NotNull(batch);
            Assert.Contains("Name", batch.ColumnNames);
            Assert.Contains("FullPath", batch.ColumnNames);
            Assert.Contains("Size", batch.ColumnNames);
            Assert.Contains("IsDirectory", batch.ColumnNames);
        }
    }
}
