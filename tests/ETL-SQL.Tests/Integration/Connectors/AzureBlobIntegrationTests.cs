using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Services;
using Moq;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Azure Blob Storage integration tests.  Docker-requiring tests need the Azurite emulator;
    /// run with:  dotnet test --filter "Category=Integration"
    ///
    /// The fixture starts one Azurite container per collection.  All tests in this class share it.
    /// </summary>
    [Collection("AZURE_BLOB collection")]
    [Trait("Category", "Integration")]
    [Trait("Connector", "AZURE_BLOB")]
    [Trait("CertificationClass", "DockerRealIntegration")]
    public class AzureBlobIntegrationTests
    {
        private readonly AzureBlobFixture _blob;

        public AzureBlobIntegrationTests(AzureBlobFixture blob) => _blob = blob;

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static IExecutionContext MakeContext()
        {
            var security = new SecurityService(NullLogger.Instance);
            // IsTestMode true by default in test process — localhost is allowed regardless.
            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);
            return ctx.Object;
        }

        private AzureBlobConnector ValidConnector(string container = "testcontainer") =>
            new AzureBlobConnector(MakeContext(), _blob.ValidConnectionString, container);

        private AzureBlobConnector BadKeyConnector(string container = "testcontainer") =>
            new AzureBlobConnector(MakeContext(), _blob.BadKeyConnectionString, container);

        private AzureBlobConnector ExpiredSasConnector(string container = "testcontainer") =>
            new AzureBlobConnector(MakeContext(), _blob.ExpiredSasConnectionString(), container);

        /// <summary>Creates a blob container in Azurite and returns it.</summary>
        private async Task<BlobContainerClient> CreateContainerAsync(string name)
        {
            var client = _blob.CreateServiceClient();
            var container = client.GetBlobContainerClient(name);
            await container.CreateIfNotExistsAsync();
            return container;
        }

        // ── 1. Smoke: valid credentials, empty container ──────────────────────────

        [Fact]
        public async Task ValidCredentials_ReadBatches_ReturnsEmptyTable()
        {
            var containerName = $"smoke-{Guid.NewGuid():N}";
            await CreateContainerAsync(containerName);

            var conn = ValidConnector(containerName);
            var tables = await conn.ReadBatches().ToListAsync();

            Assert.Single(tables);
            Assert.Empty(tables[0].Rows);
        }

        // ── 2. Upload and list round-trip ─────────────────────────────────────────

        [Fact]
        public async Task UploadBlob_ThenReadBatches_ListsFile()
        {
            var containerName = $"list-{Guid.NewGuid():N}";
            var blobContainer = await CreateContainerAsync(containerName);

            // Upload a blob directly via Azure SDK.
            var blobClient = blobContainer.GetBlobClient("hello.txt");
            using var content = new MemoryStream("hello world"u8.ToArray());
            await blobClient.UploadAsync(content, overwrite: true);

            var conn = ValidConnector(containerName);
            var tables = await conn.ReadBatches().ToListAsync();

            Assert.Single(tables);
            var row = Assert.Single(tables[0].Rows);
            Assert.Equal("hello.txt", row["Name"]?.ToString());
        }

        // ── 3. Download a blob ────────────────────────────────────────────────────

        [Fact]
        public async Task UploadBlob_ThenDownload_ContentMatches()
        {
            var containerName = $"down-{Guid.NewGuid():N}";
            var blobContainer = await CreateContainerAsync(containerName);

            const string expectedText = "download test content";
            var blobClient = blobContainer.GetBlobClient("download.txt");
            using var uploadStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(expectedText));
            await blobClient.UploadAsync(uploadStream, overwrite: true);

            var localDst = Path.Combine(Path.GetTempPath(), $"blob_dl_{Guid.NewGuid():N}.txt");
            try
            {
                var conn = ValidConnector(containerName);
                await conn.DownloadFileAsync("download.txt", localDst);

                var actual = await File.ReadAllTextAsync(localDst);
                Assert.Equal(expectedText, actual);
            }
            finally
            {
                if (File.Exists(localDst)) File.Delete(localDst);
            }
        }

        // ── 4. Bad account key → auth failure → ExecutionException ────────────────

        [Fact]
        public async Task BadAccountKey_ReadBatches_WrapsAsExecutionException()
        {
            var conn = BadKeyConnector("anycontainer");
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await conn.ReadBatches().ToListAsync());
            Assert.Contains("Azure Blob", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── 5. Bad account key on upload → ExecutionException ─────────────────────

        [Fact]
        public async Task BadAccountKey_UploadFile_WrapsAsExecutionException()
        {
            var localSrc = Path.Combine(Path.GetTempPath(), $"blob_src_{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(localSrc, "test content");
                var conn = BadKeyConnector("anycontainer");
                var ex = await Assert.ThrowsAsync<ExecutionException>(
                    () => conn.UploadFileAsync(localSrc, "test.txt", overwrite: true));
                Assert.Contains("Azure Blob", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(localSrc)) File.Delete(localSrc);
            }
        }

        // ── 6. Expired SAS token → auth failure → ExecutionException ─────────────

        [Fact]
        public async Task ExpiredSas_ReadBatches_WrapsAsExecutionException()
        {
            var containerName = $"sas-{Guid.NewGuid():N}";
            await CreateContainerAsync(containerName);

            var conn = ExpiredSasConnector(containerName);
            var ex = await Assert.ThrowsAsync<ExecutionException>(
                async () => await conn.ReadBatches().ToListAsync());

            Assert.Contains("Azure Blob", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── 7. Host not in allowlist → SecurityException at construction ──────────

        [Fact]
        public void BlockedHost_ThrowsSecurityException()
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;   // disable auto-bypass so the allowlist is enforced
            security.AllowedHosts.Clear(); // no hosts permitted

            var ctx = new Mock<IExecutionContext>();
            ctx.Setup(c => c.SecurityService).Returns(security);
            ctx.Setup(c => c.Logger).Returns(NullLogger.Instance);

            // account.blob.core.windows.net is not localhost — must be rejected
            var publicCs =
                "DefaultEndpointsProtocol=https;" +
                "AccountName=someaccount;" +
                $"AccountKey={AzureBlobFixture.DevAccountKey};";

            Assert.Throws<SecurityException>(() =>
                new AzureBlobConnector(ctx.Object, publicCs, "container"));
        }
    }

    /// <summary>
    /// Unit tests for AzureBlobConnector that do not require Docker.
    /// </summary>
    [Trait("Connector", "AZURE_BLOB")]
    [Trait("CertificationClass", "MetadataOnly")]
    public class AzureBlobConnectorUnitTests
    {
        // ── GetHostStatic parsing ─────────────────────────────────────────────────

        [Fact]
        public void GetHostStatic_AccountName_ReturnsBlobCoreWindowsNetHost()
        {
            const string cs =
                "DefaultEndpointsProtocol=https;" +
                "AccountName=myaccount;" +
                "AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==;";

            var host = AzureBlobConnector.GetHostStatic(cs);
            Assert.Equal("myaccount.blob.core.windows.net", host);
        }

        [Fact]
        public void GetHostStatic_CustomEndpointSuffix_UsesCustomDomain()
        {
            const string cs =
                "DefaultEndpointsProtocol=https;" +
                "AccountName=myaccount;" +
                "AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==;" +
                "EndpointSuffix=blob.usgovcloudapi.net;";

            var host = AzureBlobConnector.GetHostStatic(cs);
            Assert.Equal("myaccount.blob.blob.usgovcloudapi.net", host);
        }

        [Fact]
        public void GetHostStatic_BlobEndpointOverride_ExtractsHostFromUri()
        {
            const string cs =
                "DefaultEndpointsProtocol=http;" +
                "AccountName=devstoreaccount1;" +
                "AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==;" +
                "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";

            var host = AzureBlobConnector.GetHostStatic(cs);
            Assert.Equal("127.0.0.1", host);
        }

        [Fact]
        public void GetHostStatic_EmptyConnectionString_ReturnsNull()
        {
            Assert.Null(AzureBlobConnector.GetHostStatic(""));
        }

        [Fact]
        public void GetHostStatic_NoAccountNameOrEndpoint_ReturnsNull()
        {
            Assert.Null(AzureBlobConnector.GetHostStatic("DefaultEndpointsProtocol=https;"));
        }

        // ── WrongAccountKey sanity ────────────────────────────────────────────────

        [Fact]
        public void WrongAccountKey_IsDifferentFromDevKey()
        {
            Assert.NotEqual(AzureBlobFixture.DevAccountKey, AzureBlobFixture.WrongAccountKey);
        }
    }
}
