using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Parser;
using ETL_SQL.Engine;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;
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

        [Fact]
        public Task ListFilesAsync_DropsDotAndDotDotEntries() => Diagnosed(async () =>
        {
            var folder = $"rt03_{Guid.NewGuid():N}";
            var subFolder = $"{folder}/sub";
            var remoteFile = $"{folder}/one.txt";
            var localFile = LocalPath("rt03_one.txt");
            File.WriteAllText(localFile, "hello");

            await using var connector = ValidConnector();
            await connector.CreateDirectoryAsync(folder);
            await connector.CreateDirectoryAsync(subFolder);
            await connector.UploadFileAsync(localFile, remoteFile, overwrite: true);

            var entries = await connector.ListFilesAsync(folder).ToListAsync();

            Assert.Equal(2, entries.Count);
            Assert.DoesNotContain(entries, e => e.Name is "." or "..");
            Assert.Contains(entries, e => e.Name == "one.txt" && !e.IsDirectory);
            Assert.Contains(entries, e => e.Name == "sub" && e.IsDirectory);

            var fileEntry = entries.Single(e => e.Name == "one.txt");
            Assert.Equal(false, fileEntry.IsSymlink);
            if (fileEntry.Permissions != null)
                Assert.Matches("^[r-][w-][x-][r-][w-][x-][r-][w-][x-]$", fileEntry.Permissions);
        });

        [Fact]
        public Task CopyFileAt_Succeeds_AndRespectsOverwriteOff() => Diagnosed(async () =>
        {
            var srcRemote = $"rt04_src_{Guid.NewGuid():N}.txt";
            var dstRemote = $"rt04_dst_{Guid.NewGuid():N}.txt";
            var localSrc = LocalPath("rt04_src.txt");
            var localDst = LocalPath("rt04_dst.txt");
            File.WriteAllText(localSrc, "ftp copy payload v1");

            await using var connector = ValidConnector();
            await connector.UploadFileAsync(localSrc, srcRemote, overwrite: true);

            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            ((IExecutionContext)evaluator).Connections["ftp_rt04"] = connector;

            var copySql = $"COPY FILE '{srcRemote}' TO '{dstRemote}' AT ftp_rt04;";
            await evaluator.Evaluate(new Parser(new Lexer(copySql).Tokenize(), copySql).Parse());

            Assert.True(await connector.FileExistsAsync(srcRemote));
            Assert.True(await connector.FileExistsAsync(dstRemote));
            await connector.DownloadFileAsync(dstRemote, localDst, overwrite: true);
            Assert.Equal("ftp copy payload v1", File.ReadAllText(localDst));

            // OVERWRITE = OFF fails when destination already exists
            var noOverwriteSql = $"COPY FILE '{srcRemote}' TO '{dstRemote}' AT ftp_rt04 WITH (OVERWRITE = OFF);";
            await Assert.ThrowsAsync<ExecutionException>(() =>
                evaluator.Evaluate(new Parser(new Lexer(noOverwriteSql).Tokenize(), noOverwriteSql).Parse()));
        });

        [Fact]
        public Task RecursiveListReceiveAndNonEmptyDelete_EnforcesDepthAndUniformDeleteRule() => Diagnosed(async () =>
        {
            var root = $"rt06_{Guid.NewGuid():N}";
            var localSeed = LocalPath("rt06_seed.txt");
            File.WriteAllText(localSeed, "ftp-rt06-data");

            await using var connector = ValidConnector();
            await connector.CreateDirectoryAsync(root);
            await connector.CreateDirectoryAsync($"{root}/sub");
            await connector.UploadFileAsync(localSeed, $"{root}/top.txt", overwrite: true);
            await connector.UploadFileAsync(localSeed, $"{root}/sub/inner.txt", overwrite: true);

            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            ((IExecutionContext)evaluator).Connections["ftp_rt06"] = connector;

            // 1. Recursive list finds nested files
            var listSql = $"SELECT * INTO #ftp_rec FROM REMOTE_FILE_LIST(ftp_rt06, '{root}', TRUE);";
            await evaluator.Evaluate(new Parser(new Lexer(listSql).Tokenize(), listSql).Parse());
            var listed = (await ((IExecutionContext)evaluator).Connections["#ftp_rec"].ReadBatches().ToListAsync())
                .SelectMany(b => b.Rows).ToList();
            Assert.Contains(listed, r => r["Name"]?.ToString() == "top.txt");
            Assert.Contains(listed, r => r["Name"]?.ToString() == "inner.txt");

            // 2. Depth > 5 fails instead of truncating
            var deepRoot = $"rt06_deep_{Guid.NewGuid():N}";
            string cur = deepRoot;
            await connector.CreateDirectoryAsync(cur);
            for (int i = 1; i <= 6; i++)
            {
                cur += $"/d{i}";
                await connector.CreateDirectoryAsync(cur);
            }
            var deepSql = $"SELECT * INTO #ftp_deep_fail FROM REMOTE_FILE_LIST(ftp_rt06, '{deepRoot}', TRUE);";
            await Assert.ThrowsAsync<SecurityException>(() =>
                evaluator.Evaluate(new Parser(new Lexer(deepSql).Tokenize(), deepSql).Parse()));

            // 3. DELETE DIRECTORY without RECURSIVE fails on non-empty FTP folder (uniform with SFTP!)
            var delNonRec = $"DELETE DIRECTORY '{root}' AT ftp_rt06;";
            await Assert.ThrowsAsync<ExecutionException>(() =>
                evaluator.Evaluate(new Parser(new Lexer(delNonRec).Tokenize(), delNonRec).Parse()));
            Assert.True(await connector.DirectoryExistsAsync(root));

            // 4. DELETE DIRECTORY WITH (RECURSIVE = ON) deletes cleanly
            var delRec = $"DELETE DIRECTORY '{root}' AT ftp_rt06 WITH (RECURSIVE = ON);";
            await evaluator.Evaluate(new Parser(new Lexer(delRec).Tokenize(), delRec).Parse());
            Assert.False(await connector.DirectoryExistsAsync(root));
        });

        [Fact]
        public Task DirectoryRenameMoveAndCopyAt_AllThreeSucceed() => Diagnosed(async () =>
        {
            var baseDir = $"rt07_{Guid.NewGuid():N}";
            var srcDir = $"{baseDir}/src";
            var copiedDir = $"{baseDir}/copied";
            var renamedDir = $"{baseDir}/renamed";
            var movedDir = $"{baseDir}/archive/moved";
            var localSeed = LocalPath("rt07_seed.txt");
            File.WriteAllText(localSeed, "rt07-ftp-payload");

            await using var connector = ValidConnector();
            await connector.CreateDirectoryAsync(baseDir);
            await connector.CreateDirectoryAsync($"{baseDir}/archive");
            await connector.CreateDirectoryAsync(srcDir);
            await connector.CreateDirectoryAsync($"{srcDir}/sub");
            await connector.UploadFileAsync(localSeed, $"{srcDir}/a.txt", overwrite: true);
            await connector.UploadFileAsync(localSeed, $"{srcDir}/sub/b.txt", overwrite: true);

            var evaluator = ETL_SQL.Program.ServiceProvider.GetRequiredService<Evaluator>();
            ((IExecutionContext)evaluator).Connections["ftp_rt07"] = connector;

            // 1. COPY DIRECTORY
            var copySql = $"COPY DIRECTORY '{srcDir}' TO '{copiedDir}' AT ftp_rt07;";
            await evaluator.Evaluate(new Parser(new Lexer(copySql).Tokenize(), copySql).Parse());
            Assert.True(await connector.FileExistsAsync($"{copiedDir}/a.txt"));
            Assert.True(await connector.FileExistsAsync($"{copiedDir}/sub/b.txt"));
            Assert.True(await connector.FileExistsAsync($"{srcDir}/a.txt"));

            // 2. RENAME DIRECTORY
            var renameSql = $"RENAME DIRECTORY '{copiedDir}' TO 'renamed' AT ftp_rt07;";
            await evaluator.Evaluate(new Parser(new Lexer(renameSql).Tokenize(), renameSql).Parse());
            Assert.False(await connector.DirectoryExistsAsync(copiedDir));
            Assert.True(await connector.FileExistsAsync($"{renamedDir}/sub/b.txt"));

            // 3. MOVE DIRECTORY
            var moveSql = $"MOVE DIRECTORY '{renamedDir}' TO '{movedDir}' AT ftp_rt07;";
            await evaluator.Evaluate(new Parser(new Lexer(moveSql).Tokenize(), moveSql).Parse());
            Assert.False(await connector.DirectoryExistsAsync(renamedDir));
            Assert.True(await connector.FileExistsAsync($"{movedDir}/a.txt"));
            Assert.True(await connector.FileExistsAsync($"{movedDir}/sub/b.txt"));
        });
    }
}
