using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Starts the MIT-licensed nnnkkk7 Snowflake emulator. It translates a
    /// Snowflake-compatible HTTP protocol subset to DuckDB for local smoke tests.
    /// </summary>
    public class SnowflakeEmulatorFixture : IAsyncLifetime
    {
        private IContainer? _container;

        public string Host => "127.0.0.1";
        public int Port { get; private set; }

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder("ghcr.io/nnnkkk7/snowflake-emulator:latest")
                .WithPortBinding(8080, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(8080);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
            {
                await _container.StopAsync();
            }
        }
    }

    [CollectionDefinition("SNOWFLAKE_EMULATOR collection")]
    public class SnowflakeEmulatorCollection : ICollectionFixture<SnowflakeEmulatorFixture> { }
}
