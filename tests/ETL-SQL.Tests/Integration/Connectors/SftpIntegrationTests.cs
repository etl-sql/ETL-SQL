using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
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

        private SftpConnector AtomicPasswordConnector() =>
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
                atomicUpload: true,
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
        [Trait("Category", "TransactionalFileCertification")]
        public async Task AtomicUpload_PosixRenameReplacesTargetAndLeavesNoStage()
        {
            var localSrc = LocalPath("atomic_src.txt");
            var remoteName = $"atomic_{Guid.NewGuid():N}.txt";
            var remote = RemotePath(remoteName);
            await File.WriteAllTextAsync(localSrc, "original");

            await using var connector = AtomicPasswordConnector();
            await connector.UploadFileAsync(localSrc, remote, overwrite: true);
            await File.WriteAllTextAsync(localSrc, "replacement");
            await connector.UploadFileAsync(localSrc, remote, overwrite: true);

            var downloaded = LocalPath("atomic_download.txt");
            await connector.DownloadFileAsync(remote, downloaded);
            Assert.Equal("replacement", await File.ReadAllTextAsync(downloaded));

            var entries = new System.Collections.Generic.List<FileMetaData>();
            await foreach (var entry in connector.ListFilesAsync($"/{SftpFixture.RemoteUploadDir}"))
                entries.Add(entry);
            Assert.DoesNotContain(entries, entry =>
                entry.Name.StartsWith(remoteName + ".etl-stage-", StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Category", "TransactionalFileCertification")]
        public async Task AtomicUpload_CancellationPreservesTargetAndLeavesNoStage()
        {
            var localSrc = LocalPath("atomic_cancel_src.txt");
            var remoteName = $"atomic_cancel_{Guid.NewGuid():N}.txt";
            var remote = RemotePath(remoteName);
            await File.WriteAllTextAsync(localSrc, "published");

            await using var connector = AtomicPasswordConnector();
            await connector.UploadFileAsync(localSrc, remote, overwrite: true);
            await File.WriteAllTextAsync(localSrc, "must-not-publish");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                connector.UploadFileAsync(localSrc, remote, overwrite: true, cancellation.Token));

            var downloaded = LocalPath("atomic_cancel_download.txt");
            await connector.DownloadFileAsync(remote, downloaded);
            Assert.Equal("published", await File.ReadAllTextAsync(downloaded));

            var entries = new System.Collections.Generic.List<FileMetaData>();
            await foreach (var entry in connector.ListFilesAsync($"/{SftpFixture.RemoteUploadDir}"))
                entries.Add(entry);
            Assert.DoesNotContain(entries, entry =>
                entry.Name.StartsWith(remoteName + ".etl-stage-", StringComparison.Ordinal));
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
            Assert.DoesNotContain(batch.Rows, r => r["Name"]?.ToString() is "." or "..");
        }

        [Fact]
        public async Task ListFilesAsync_DropsDotAndDotDotEntries()
        {
            var folder = RemotePath($"rt03_{Guid.NewGuid():N}");
            var subFolder = $"{folder}/sub";
            var remoteFile = $"{folder}/one.txt";
            var localFile = LocalPath("rt03_one.txt");
            File.WriteAllText(localFile, "hello");

            await using var connector = PasswordConnector();
            await connector.CreateDirectoryAsync(folder);
            await connector.CreateDirectoryAsync(subFolder);
            await connector.UploadFileAsync(localFile, remoteFile, overwrite: true);

            var entries = await connector.ListFilesAsync(folder).ToListAsync();

            Assert.Equal(2, entries.Count);
            Assert.DoesNotContain(entries, e => e.Name is "." or "..");
            Assert.Contains(entries, e => e.Name == "one.txt" && !e.IsDirectory);
            Assert.Contains(entries, e => e.Name == "sub" && e.IsDirectory);

            var fileEntry = entries.Single(e => e.Name == "one.txt");
            Assert.Null(fileEntry.CreationTime);
            Assert.NotNull(fileEntry.Permissions);
            Assert.Equal(9, fileEntry.Permissions!.Length);
            Assert.Matches("^[r-][w-][x-][r-][w-][x-][r-][w-][x-]$", fileEntry.Permissions);
            Assert.False(string.IsNullOrWhiteSpace(fileEntry.Owner));
            Assert.Equal(false, fileEntry.IsSymlink);
        }

        [Fact]
        public async Task CopyFileAt_Succeeds_AndRespectsOverwriteOff()
        {
            var srcRemote = RemotePath($"rt04_src_{Guid.NewGuid():N}.txt");
            var dstRemote = RemotePath($"rt04_dst_{Guid.NewGuid():N}.txt");
            var localSrc = LocalPath("rt04_src.txt");
            var localDst = LocalPath("rt04_dst.txt");
            File.WriteAllText(localSrc, "sftp copy payload v1");

            await using var connector = PasswordConnector();
            await connector.UploadFileAsync(localSrc, srcRemote, overwrite: true);

            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            ((IExecutionContext)evaluator).Connections["sftp_rt04"] = connector;

            var copySql = $"COPY FILE '{srcRemote}' TO '{dstRemote}' AT sftp_rt04;";
            await evaluator.Evaluate(new Parser(new Lexer(copySql).Tokenize(), copySql).Parse());

            Assert.True(await connector.FileExistsAsync(srcRemote));
            Assert.True(await connector.FileExistsAsync(dstRemote));
            await connector.DownloadFileAsync(dstRemote, localDst, overwrite: true);
            Assert.Equal("sftp copy payload v1", File.ReadAllText(localDst));

            // OVERWRITE = OFF fails when destination already exists
            var noOverwriteSql = $"COPY FILE '{srcRemote}' TO '{dstRemote}' AT sftp_rt04 WITH (OVERWRITE = OFF);";
            await Assert.ThrowsAsync<ExecutionException>(() =>
                evaluator.Evaluate(new Parser(new Lexer(noOverwriteSql).Tokenize(), noOverwriteSql).Parse()));
        }

        [Fact]
        public async Task RecursiveListReceiveDeleteAndSymlinkLoop_EnforcesDepthAndNonEmptyRules()
        {
            var root = RemotePath($"rt06_{Guid.NewGuid():N}");
            var localSeed = LocalPath("rt06_seed.txt");
            File.WriteAllText(localSeed, "rt06-data");

            await using var connector = PasswordConnector();
            await connector.CreateDirectoryAsync(root);
            await connector.CreateDirectoryAsync($"{root}/sub");
            await connector.UploadFileAsync(localSeed, $"{root}/top.txt", overwrite: true);
            await connector.UploadFileAsync(localSeed, $"{root}/sub/inner.txt", overwrite: true);

            // Create a real symlink loop back to root via SSH.NET
            using (var rawClient = new SftpClient(_sftp.Host, _sftp.Port, SftpFixture.TestUser, SftpFixture.TestPassword))
            {
                rawClient.Connect();
                rawClient.SymbolicLink(root, $"{root}/loop");
                rawClient.Disconnect();
            }

            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            ((IExecutionContext)evaluator).Connections["sftp_rt06"] = connector;

            // 1. Recursive list terminates cleanly without following symlink loop
            var listSql = $"SELECT * INTO #sftp_rec FROM REMOTE_FILE_LIST(sftp_rt06, '{root}', TRUE);";
            await evaluator.Evaluate(new Parser(new Lexer(listSql).Tokenize(), listSql).Parse());
            var listed = (await ((IExecutionContext)evaluator).Connections["#sftp_rec"].ReadBatches().ToListAsync())
                .SelectMany(b => b.Rows).ToList();
            Assert.Contains(listed, r => r["Name"]?.ToString() == "top.txt");
            Assert.Contains(listed, r => r["Name"]?.ToString() == "inner.txt");
            Assert.Contains(listed, r => r["Name"]?.ToString() == "loop" && Equals(r["IsSymlink"], true));

            // 2. Recursive RECEIVE FILE downloads nested tree cleanly
            var rxDir = Path.Combine(_tmpDir, "rx_tree");
            var rxSql = $"RECEIVE FILE FROM '{root}' TO '{rxDir.Replace("\\", "/")}' AT sftp_rt06 WITH (RECURSIVE = ON);";
            await evaluator.Evaluate(new Parser(new Lexer(rxSql).Tokenize(), rxSql).Parse());
            Assert.Equal("rt06-data", File.ReadAllText(Path.Combine(rxDir, "top.txt")));
            Assert.Equal("rt06-data", File.ReadAllText(Path.Combine(rxDir, "sub", "inner.txt")));

            // 3. Depth > 5 fails instead of silently truncating
            var deepRoot = RemotePath($"rt06_deep_{Guid.NewGuid():N}");
            string cur = deepRoot;
            await connector.CreateDirectoryAsync(cur);
            for (int i = 1; i <= 6; i++)
            {
                cur += $"/d{i}";
                await connector.CreateDirectoryAsync(cur);
            }
            var deepSql = $"SELECT * INTO #deep_fail FROM REMOTE_FILE_LIST(sftp_rt06, '{deepRoot}', TRUE);";
            await Assert.ThrowsAsync<SecurityException>(() =>
                evaluator.Evaluate(new Parser(new Lexer(deepSql).Tokenize(), deepSql).Parse()));

            // 4. DELETE DIRECTORY without RECURSIVE fails on non-empty folder, succeeds with RECURSIVE = ON
            var delNonRec = $"DELETE DIRECTORY '{root}' AT sftp_rt06;";
            await Assert.ThrowsAsync<ExecutionException>(() =>
                evaluator.Evaluate(new Parser(new Lexer(delNonRec).Tokenize(), delNonRec).Parse()));
            Assert.True(await connector.DirectoryExistsAsync(root));

            var delRec = $"DELETE DIRECTORY '{root}' AT sftp_rt06 WITH (RECURSIVE = ON);";
            await evaluator.Evaluate(new Parser(new Lexer(delRec).Tokenize(), delRec).Parse());
            Assert.False(await connector.DirectoryExistsAsync(root));
        }

        [Fact]
        public async Task DirectoryRenameMoveAndCopyAt_AllThreeSucceed()
        {
            var baseDir = RemotePath($"rt07_{Guid.NewGuid():N}");
            var srcDir = $"{baseDir}/src";
            var copiedDir = $"{baseDir}/copied";
            var renamedDir = $"{baseDir}/renamed";
            var movedDir = $"{baseDir}/archive/moved";
            var localSeed = LocalPath("rt07_seed.txt");
            File.WriteAllText(localSeed, "rt07-sftp-payload");

            await using var connector = PasswordConnector();
            await connector.CreateDirectoryAsync(baseDir);
            await connector.CreateDirectoryAsync($"{baseDir}/archive");
            await connector.CreateDirectoryAsync(srcDir);
            await connector.CreateDirectoryAsync($"{srcDir}/sub");
            await connector.UploadFileAsync(localSeed, $"{srcDir}/a.txt", overwrite: true);
            await connector.UploadFileAsync(localSeed, $"{srcDir}/sub/b.txt", overwrite: true);

            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            ((IExecutionContext)evaluator).Connections["sftp_rt07"] = connector;

            // 1. COPY DIRECTORY
            var copySql = $"COPY DIRECTORY '{srcDir}' TO '{copiedDir}' AT sftp_rt07;";
            await evaluator.Evaluate(new Parser(new Lexer(copySql).Tokenize(), copySql).Parse());
            Assert.True(await connector.FileExistsAsync($"{copiedDir}/a.txt"));
            Assert.True(await connector.FileExistsAsync($"{copiedDir}/sub/b.txt"));
            Assert.True(await connector.FileExistsAsync($"{srcDir}/a.txt"));

            // 2. RENAME DIRECTORY (relative name in same parent)
            var renameSql = $"RENAME DIRECTORY '{copiedDir}' TO 'renamed' AT sftp_rt07;";
            await evaluator.Evaluate(new Parser(new Lexer(renameSql).Tokenize(), renameSql).Parse());
            Assert.False(await connector.DirectoryExistsAsync(copiedDir));
            Assert.True(await connector.FileExistsAsync($"{renamedDir}/sub/b.txt"));

            // 3. MOVE DIRECTORY (into another parent)
            var moveSql = $"MOVE DIRECTORY '{renamedDir}' TO '{movedDir}' AT sftp_rt07;";
            await evaluator.Evaluate(new Parser(new Lexer(moveSql).Tokenize(), moveSql).Parse());
            Assert.False(await connector.DirectoryExistsAsync(renamedDir));
            Assert.True(await connector.FileExistsAsync($"{movedDir}/a.txt"));
            Assert.True(await connector.FileExistsAsync($"{movedDir}/sub/b.txt"));
        }

        [Fact]
        public async Task SyncDirectory_BothDirections_NoChangeSecondRun_AndDeleteExtraOffByDefault()
        {
            var localSrc = Path.Combine(_tmpDir, "sync_src");
            var localMirror = Path.Combine(_tmpDir, "sync_mirror");
            Directory.CreateDirectory(Path.Combine(localSrc, "sub"));
            Directory.CreateDirectory(localMirror);

            File.WriteAllText(Path.Combine(localSrc, "a.txt"), "alpha-v1");
            File.WriteAllText(Path.Combine(localSrc, "sub", "b.txt"), "beta-v1");
            File.WriteAllText(Path.Combine(localMirror, "stale_local.txt"), "stale");

            var remoteDir = RemotePath($"rt08_{Guid.NewGuid():N}");
            await using var connector = PasswordConnector();
            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            ((IExecutionContext)evaluator).Connections["sftp_rt08"] = connector;

            // 1. Local -> Remote (first run copies 2 files)
            var l2rSql = $"SYNC DIRECTORY '{localSrc.Replace("\\", "/")}' TO '{remoteDir}' AT sftp_rt08 WITH (RECURSIVE = ON);";
            await evaluator.Evaluate(new Parser(new Lexer(l2rSql).Tokenize(), l2rSql).Parse());
            Assert.True(await connector.FileExistsAsync($"{remoteDir}/a.txt"));
            Assert.True(await connector.FileExistsAsync($"{remoteDir}/sub/b.txt"));

            // Add an extra file on remote, then run Local -> Remote again WITHOUT DELETE_EXTRA (default OFF)
            var extraLocal = LocalPath("extra.txt");
            File.WriteAllText(extraLocal, "keep-me");
            await connector.UploadFileAsync(extraLocal, $"{remoteDir}/extra_remote.txt", overwrite: true);

            // Second run (no changes in source, DELETE_EXTRA off by default): extra_remote.txt stays
            await evaluator.Evaluate(new Parser(new Lexer(l2rSql).Tokenize(), l2rSql).Parse());
            Assert.True(await connector.FileExistsAsync($"{remoteDir}/extra_remote.txt"));

            // 2. Local -> Remote with DELETE_EXTRA = ON removes extra_remote.txt
            var l2rDelSql = $"SYNC DIRECTORY '{localSrc.Replace("\\", "/")}' TO '{remoteDir}' AT sftp_rt08 WITH (RECURSIVE = ON, DELETE_EXTRA = ON);";
            await evaluator.Evaluate(new Parser(new Lexer(l2rDelSql).Tokenize(), l2rDelSql).Parse());
            Assert.False(await connector.FileExistsAsync($"{remoteDir}/extra_remote.txt"));

            // 3. Remote -> Local (default DELETE_EXTRA = OFF keeps stale_local.txt)
            var r2lSql = $"SYNC DIRECTORY '{remoteDir}' AT sftp_rt08 TO '{localMirror.Replace("\\", "/")}' WITH (RECURSIVE = ON);";
            await evaluator.Evaluate(new Parser(new Lexer(r2lSql).Tokenize(), r2lSql).Parse());
            Assert.Equal("alpha-v1", File.ReadAllText(Path.Combine(localMirror, "a.txt")));
            Assert.Equal("beta-v1", File.ReadAllText(Path.Combine(localMirror, "sub", "b.txt")));
            Assert.True(File.Exists(Path.Combine(localMirror, "stale_local.txt")));

            // 4. Remote -> Local with DELETE_EXTRA = ON removes stale_local.txt
            var r2lDelSql = $"SYNC DIRECTORY '{remoteDir}' AT sftp_rt08 TO '{localMirror.Replace("\\", "/")}' WITH (RECURSIVE = ON, DELETE_EXTRA = ON);";
            await evaluator.Evaluate(new Parser(new Lexer(r2lDelSql).Tokenize(), r2lDelSql).Parse());
            Assert.False(File.Exists(Path.Combine(localMirror, "stale_local.txt")));
        }
    }
}
