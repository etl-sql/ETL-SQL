using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using ETL_SQL.Common;
using ETL_SQL.Connectors.S3;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    [Collection("S3 collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "S3")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class S3IntegrationTests
    {
        private readonly S3Fixture _fixture;

        public S3IntegrationTests(S3Fixture fixture)
        {
            _fixture = fixture;
        }

        private IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        private Dictionary<string, string> GetValidOptions() => new()
        {
            { "ENDPOINT", _fixture.ServiceUrl },
            { "ACCESS_KEY", S3Fixture.AccessKey },
            { "SECRET_KEY", S3Fixture.SecretKey },
            { "BUCKET", S3Fixture.BucketName },
            { "FORCE_PATH_STYLE", "TRUE" }
        };

        [Fact]
        public async Task S3Connector_GetVersionAsync_Success()
        {
            var ctx = MakeContext();
            var options = GetValidOptions();
            var connector = new S3Connector(ctx, S3Fixture.BucketName, options);

            var version = await connector.GetVersionAsync(ctx, S3Fixture.BucketName);
            Assert.Contains("Connected", version);
            Assert.Contains(S3Fixture.BucketName, version);
        }

        [Fact]
        public async Task S3Connector_GetVersionAsync_InvalidBucket_ThrowsException()
        {
            var ctx = MakeContext();
            var options = GetValidOptions();
            options["BUCKET"] = "non-existent-bucket-name-xyz";

            var connector = new S3Connector(ctx, "non-existent-bucket-name-xyz", options);

            await Assert.ThrowsAsync<ExecutionException>(() =>
                connector.GetVersionAsync(ctx, "non-existent-bucket-name-xyz"));
        }

        [Fact]
        public async Task S3Connector_UploadAndDownload_RoundTrip_Success()
        {
            var ctx = MakeContext();
            var options = GetValidOptions();
            var connector = new S3Connector(ctx, S3Fixture.BucketName, options);

            var localSrc = Path.GetTempFileName();
            var localDst = Path.GetTempFileName();
            var remoteKey = $"test_folder/upload_{Guid.NewGuid():N}.txt";

            try
            {
                var originalText = "hello minio integration storage test";
                await File.WriteAllTextAsync(localSrc, originalText);

                // 1. Upload
                await connector.UploadFileAsync(localSrc, remoteKey);

                // 2. File Exists Check
                var exists = await connector.FileExistsAsync(remoteKey);
                Assert.True(exists);

                // 3. List Files
                var files = await connector.ListFilesAsync("test_folder").ToListAsync();
                Assert.Contains(files, f => f.FullPath == remoteKey);

                // 4. Download
                await connector.DownloadFileAsync(remoteKey, localDst, overwrite: true);
                var downloadedText = await File.ReadAllTextAsync(localDst);
                Assert.Equal(originalText, downloadedText);

                // 5. Delete File
                await connector.DeleteFileAsync(remoteKey);
                exists = await connector.FileExistsAsync(remoteKey);
                Assert.False(exists);
            }
            finally
            {
                if (File.Exists(localSrc)) File.Delete(localSrc);
                if (File.Exists(localDst)) File.Delete(localDst);
            }
        }
    }
}
