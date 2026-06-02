using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    public class KafkaFixture : IAsyncLifetime
    {
        private IContainer? _container;

        public int Port { get; private set; }
        public string BootstrapServers => $"127.0.0.1:{Port}";
        public string TopicName => "integration-test-topic";

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder("redpandadata/redpanda:latest")
                .WithName("etl-sql-kafka")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithPortBinding(9092, 9092)
                .WithCommand("redpanda", "start", "--mode", "dev-container")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(9092))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(9092);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    [CollectionDefinition("Kafka collection")]
    public class KafkaCollection : ICollectionFixture<KafkaFixture> { }
}
