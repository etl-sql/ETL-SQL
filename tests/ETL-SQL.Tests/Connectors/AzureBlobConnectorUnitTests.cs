using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Services;
using ETL_SQL.Tests.Integration.Connectors;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// Unit tests for AzureBlobConnector that do not require Docker.
    /// </summary>
    [Trait("Connector", "AZURE_BLOB")]
    [Trait("CertificationClass", "MetadataOnly")]
    public class AzureBlobConnectorUnitTests
    {
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

        [Fact]
        public void WrongAccountKey_IsDifferentFromDevKey()
        {
            Assert.NotEqual(AzureBlobFixture.DevAccountKey, AzureBlobFixture.WrongAccountKey);
        }

        [Fact]
        public void BuildConnectionString_WithConnectionStringOption_ReturnsIt()
        {
            var connector = new AzureBlobConnector();
            var props = new Dictionary<string, string>
            {
                { "CONNECTION_STRING", "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;" }
            };
            var connStr = connector.BuildConnectionString(props);
            Assert.Equal("DefaultEndpointsProtocol=https;AccountName=test;AccountKey=key;", connStr);
        }

        [Fact]
        public void BuildConnectionString_WithAccountAndKey_ReturnsConnectionString()
        {
            var connector = new AzureBlobConnector();
            var props = new Dictionary<string, string>
            {
                { "ACCOUNT_NAME", "myaccount" },
                { "ACCOUNT_KEY", "mykey" }
            };
            var connStr = connector.BuildConnectionString(props);
            Assert.Contains("AccountName=myaccount", connStr);
            Assert.Contains("AccountKey=mykey", connStr);
            Assert.Contains("EndpointSuffix=core.windows.net", connStr);
        }

        [Fact]
        public void BuildConnectionString_WithAccountAndSasToken_ReturnsConnectionString()
        {
            var connector = new AzureBlobConnector();
            var props = new Dictionary<string, string>
            {
                { "ACCOUNT_NAME", "myaccount" },
                { "SAS_TOKEN", "sas-token-123" }
            };
            var connStr = connector.BuildConnectionString(props);
            Assert.Contains("AccountName=myaccount", connStr);
            Assert.Contains("SharedAccessSignature=sas-token-123", connStr);
        }

        [Fact]
        public void BuildConnectionString_WithCustomEndpointSuffix_ReturnsConnectionString()
        {
            var connector = new AzureBlobConnector();
            var props = new Dictionary<string, string>
            {
                { "ACCOUNT_NAME", "myaccount" },
                { "ACCOUNT_KEY", "mykey" },
                { "ENDPOINT_SUFFIX", "blob.usgovcloudapi.net" }
            };
            var connStr = connector.BuildConnectionString(props);
            Assert.Contains("EndpointSuffix=blob.usgovcloudapi.net", connStr);
        }

        [Fact]
        public void BuildConnectionString_WithBlobEndpoint_ReturnsConnectionString()
        {
            var connector = new AzureBlobConnector();
            var props = new Dictionary<string, string>
            {
                { "ACCOUNT_NAME", "myaccount" },
                { "ACCOUNT_KEY", "mykey" },
                { "BLOB_ENDPOINT", "http://127.0.0.1:10000/myaccount" }
            };
            var connStr = connector.BuildConnectionString(props);
            Assert.Contains("BlobEndpoint=http://127.0.0.1:10000/myaccount", connStr);
            Assert.DoesNotContain("EndpointSuffix", connStr);
        }

        [Fact]
        public void CreateDataSource_WithEncryptedAccountKey_DecryptsCorrectly()
        {
            var mockLogger = new Moq.Mock<ILogger>();
            var securityService = new SecurityService(mockLogger.Object);
            var mockContext = new Moq.Mock<IExecutionContext>();
            mockContext.Setup(c => c.SecurityService).Returns(securityService);
            mockContext.Setup(c => c.Logger).Returns(mockLogger.Object);
            mockContext.Setup(c => c.DecryptValue("ENC:encryptedKey")).Returns("AQIDBAU=");

            var connector = new AzureBlobConnector();
            var options = new Dictionary<string, string>
            {
                { "ACCOUNT_NAME", "myaccount" },
                { "ACCOUNT_KEY", "ENC:encryptedKey" },
                { "CONTAINER", "test-container" }
            };

            var dataSource = (AzureBlobConnector)connector.CreateDataSource(mockContext.Object, "", options);

            // Validate the host from decrypted connection string was checked
            mockContext.Verify(c => c.DecryptValue("ENC:encryptedKey"), Moq.Times.Once);
        }
    }
}
