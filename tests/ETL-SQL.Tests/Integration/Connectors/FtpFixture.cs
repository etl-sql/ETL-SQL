using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Starts a real FTP server with fixed passive data ports so FluentFTP can
    /// exercise control and data connections through Docker port mappings.
    /// </summary>
    public class FtpFixture : IAsyncLifetime
    {
        public const string TestUser = "ftpuser";
        public const string TestPassword = "ftppass";
        public const int PassiveMinPort = 21000;
        public const int PassiveMaxPort = 21010;

        private IContainer? _container;

        public string Host => "127.0.0.1";
        public int Port { get; private set; }

        public async Task InitializeAsync()
        {
            var builder = new ContainerBuilder("delfer/alpine-ftp-server:latest")
                .WithName("etl-sql-ftp")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithEnvironment("USERS", $"{TestUser}|{TestPassword}")
                .WithEnvironment("ADDRESS", Host)
                .WithEnvironment("MIN_PORT", PassiveMinPort.ToString())
                .WithEnvironment("MAX_PORT", PassiveMaxPort.ToString())
                .WithPortBinding(21, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(21));

            for (var port = PassiveMinPort; port <= PassiveMaxPort; port++)
            {
                builder = builder.WithPortBinding(port, port);
            }

            _container = builder.Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(21);
        }

        public async Task DisposeAsync()
        {
            if (_container != null)
            {
                await _container.DisposeAsync();
            }
        }
    }

    [CollectionDefinition("FTP collection")]
    public class FtpCollection : ICollectionFixture<FtpFixture> { }
}
