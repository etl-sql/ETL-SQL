using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Connectors.BigQuery;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Services;
using Xunit;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// Unit tests for BigQuery that run without any live connection or Docker.
    /// </summary>
    [Trait("Connector", "BIGQUERY")]
    [Trait("CertificationClass", "MockedIntegration")]
    public class BigQueryConnectorUnitTests : IDisposable
    {
        private static SystemExecutionContext Ctx => SystemExecutionContext.Instance;
        private readonly string _tmpDir;

        public BigQueryConnectorUnitTests()
        {
            _tmpDir = Path.Combine(Path.GetTempPath(), $"bq-unit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tmpDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tmpDir))
                Directory.Delete(_tmpDir, recursive: true);
        }

        [Fact]
        public async Task BigQuery_InvalidCredentialJson_WrapsAsExecutionException()
        {
            var credFile = Path.Combine(_tmpDir, "bad_creds.json");
            File.WriteAllText(credFile, "not valid json at all");

            var ds = new BigQueryDataSource(Ctx,
                $"project=fake-project;credential_file={credFile};", null,
                new Dictionary<string, string> { ["PROJECT_ID"] = "fake-project", ["CREDENTIAL_FILE"] = credFile });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.GetVersionAsync());
            Assert.Contains("BigQuery", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task BigQuery_MissingCredentialFile_WrapsAsExecutionException()
        {
            var missingPath = Path.Combine(_tmpDir, "does_not_exist.json");

            var ds = new BigQueryDataSource(Ctx,
                $"project=fake-project;credential_file={missingPath};", null,
                new Dictionary<string, string> { ["PROJECT_ID"] = "fake-project", ["CREDENTIAL_FILE"] = missingPath });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.GetVersionAsync());
            Assert.Contains("BigQuery", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task BigQuery_InvalidCredential_DoesNotLeakPrivateKeyMaterial()
        {
            const string fakePrivateKey = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA0000FAKEKEYDATA1234567890\n-----END RSA PRIVATE KEY-----";

            var credFile = Path.Combine(_tmpDir, "service_account.json");
            File.WriteAllText(credFile, $$"""
{
  "type": "service_account",
  "project_id": "fake-project",
  "private_key_id": "key-id-12345",
  "private_key": "{{fakePrivateKey}}",
  "client_email": "sa@fake-project.iam.gserviceaccount.com"
}
""");

            var ds = new BigQueryDataSource(Ctx,
                $"project=fake-project;credential_file={credFile};", null,
                new Dictionary<string, string> { ["PROJECT_ID"] = "fake-project", ["CREDENTIAL_FILE"] = credFile });

            var ex = await Assert.ThrowsAsync<ExecutionException>(() => ds.GetVersionAsync());
            Assert.DoesNotContain("FAKEKEYDATA", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("key-id-12345", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BigQuery_HostNotInAllowlist_ThrowsAtConstruction()
        {
            var security = new SecurityService(NullLogger.Instance);
            security.IsTestMode = false;
            security.AllowedHosts.Clear();

            var mock = new Moq.Mock<IExecutionContext>();
            mock.Setup(c => c.SecurityService).Returns(security);
            mock.Setup(c => c.Logger).Returns(NullLogger.Instance);
            mock.Setup(c => c.ResolvePath(Moq.It.IsAny<string>())).Returns<string>(p => p);

            Assert.Throws<SecurityException>(() =>
                new BigQueryDataSource(mock.Object, "project=fake-project;", null,
                    new Dictionary<string, string> { ["PROJECT_ID"] = "fake-project" }));
        }
    }
}
