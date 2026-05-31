using System;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    public class S3Fixture : IAsyncLifetime
    {
        private IContainer? _container;

        public const string AccessKey = "minioadmin";
        public const string SecretKey = "minioadmin";
        public const string BucketName = "test-bucket";

        public int Port { get; private set; }
        public string ServiceUrl => $"http://127.0.0.1:{Port}";

        public IAmazonS3 CreateClient()
        {
            var config = new AmazonS3Config
            {
                ServiceURL = ServiceUrl,
                ForcePathStyle = true
            };
            return new AmazonS3Client(AccessKey, SecretKey, config);
        }

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder("minio/minio:latest")
                .WithName("etl-sql-minio")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithPortBinding(9000, true)
                .WithCommand("server", "/data")
                .WithEnvironment("MINIO_ROOT_USER", AccessKey)
                .WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(9000))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(9000);

            // Create the test bucket
            using var client = CreateClient();
            await client.PutBucketAsync(new PutBucketRequest { BucketName = BucketName });
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
            {
                await _container.StopAsync();
            }
        }
    }

    [CollectionDefinition("S3 collection")]
    public class S3Collection : ICollectionFixture<S3Fixture> { }
}
