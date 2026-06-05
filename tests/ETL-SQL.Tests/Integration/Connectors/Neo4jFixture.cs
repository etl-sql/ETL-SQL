using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    public class Neo4jFixture : IAsyncLifetime
    {
        private IContainer? _container;

        public int Port { get; private set; }
        public string ConnectionString => $"bolt://127.0.0.1:{Port}";
        public string Username => "neo4j";
        public string Password => "password";

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder("neo4j:5-community")
                .WithName("etl-sql-neo4j")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithEnvironment("NEO4J_AUTH", "neo4j/password")
                .WithPortBinding(7687, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(7687))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(7687);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    [CollectionDefinition("Neo4j collection")]
    public class Neo4jCollection : ICollectionFixture<Neo4jFixture> { }
}
