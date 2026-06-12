using System;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Starts an Azurite container (Azure Storage emulator) for Azure Blob integration tests.
    /// Azurite exposes the Blob service on port 10000.
    ///
    /// Well-known Azurite development credentials are used — they are public and documented
    /// in the Microsoft Azurite repository.
    /// </summary>
    public class AzureBlobFixture : IAsyncLifetime
    {
        public const string DevAccountName = "devstoreaccount1";

        // Public Azurite development account key — not a secret.
        public const string DevAccountKey =
            "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        // 64 null bytes in base64 — guaranteed different from the dev key, causes auth failure.
        public static readonly string WrongAccountKey = Convert.ToBase64String(new byte[64]);

        private IContainer? _container;

        public int BlobPort { get; private set; }

        public string ConnectionString(string accountKey) =>
            $"DefaultEndpointsProtocol=http;" +
            $"AccountName={DevAccountName};" +
            $"AccountKey={accountKey};" +
            $"BlobEndpoint=http://127.0.0.1:{BlobPort}/{DevAccountName};";

        public string ValidConnectionString => ConnectionString(DevAccountKey);
        public string BadKeyConnectionString => ConnectionString(WrongAccountKey);

        public string ExpiredSasConnectionString()
        {
            var credential = new StorageSharedKeyCredential(DevAccountName, DevAccountKey);
            var sas = new AccountSasBuilder
            {
                Services = AccountSasServices.Blobs,
                ResourceTypes = AccountSasResourceTypes.Service | AccountSasResourceTypes.Container | AccountSasResourceTypes.Object,
                Protocol = SasProtocol.HttpsAndHttp,
                StartsOn = DateTimeOffset.UtcNow.AddHours(-2),
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(-1)
            };
            sas.SetPermissions(AccountSasPermissions.Read | AccountSasPermissions.List);

            return $"DefaultEndpointsProtocol=http;" +
                   $"AccountName={DevAccountName};" +
                   $"BlobEndpoint=http://127.0.0.1:{BlobPort}/{DevAccountName};" +
                   $"SharedAccessSignature={sas.ToSasQueryParameters(credential)};";
        }

        public BlobServiceClient CreateServiceClient() =>
            new BlobServiceClient(ValidConnectionString);

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
                .WithName("etl-sql-azurite")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithPortBinding(10000, true)
                .WithCommand("azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(10000))
                .Build();

            await _container.StartAsync();
            BlobPort = _container.GetMappedPublicPort(10000);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
                await _container.DisposeAsync();
        }
    }

    [CollectionDefinition("AZURE_BLOB collection")]
    public class AzureBlobCollection : ICollectionFixture<AzureBlobFixture> { }
}
