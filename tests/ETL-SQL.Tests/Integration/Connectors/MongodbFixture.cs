using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    public class MongodbFixture : IAsyncLifetime
    {
        private IContainer? _container;

        public int Port { get; private set; }
        public string ConnectionString => $"mongodb://127.0.0.1:{Port}";
        public string DatabaseName => "test_db";

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder("mongo:6.0")
                .WithName("etl-sql-mongodb")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithPortBinding(27017, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(27017))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(27017);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
            {
                await _container.StopAsync();
            }
        }
    }

    [CollectionDefinition("MongoDB collection")]
    public class MongodbCollection : ICollectionFixture<MongodbFixture> { }
}
