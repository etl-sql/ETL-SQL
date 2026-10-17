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
                // Listening inside the container is not the same as reachable from the host: the
                // first test to run connected through the mapped port before it was forwarding and
                // was refused in two milliseconds, while the later ones passed.
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilInternalTcpPortIsAvailable(21)
                    .UntilExternalTcpPortIsAvailable(21));

            for (var port = PassiveMinPort; port <= PassiveMaxPort; port++)
            {
                builder = builder.WithPortBinding(port, port);
            }

            _container = builder.Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(21);
            await WaitForGreetingAsync();
        }

        // An open mapped port only proves Docker's proxy accepts. On a cold start the first test was
        // still refused: on the control port, and once the server greeted, on the passive data ports.
        // Ready means what the tests do works: sign in and list the root over a passive connection.
        private Task WaitForGreetingAsync() =>
            ETL_SQL.TestSupport.LoadAwareWait.UntilAsync(
                "the FTP test server to list its root over a passive connection",
                async cancellationToken =>
                {
                    try
                    {
                        await using var client = new FluentFTP.AsyncFtpClient(Host, TestUser, TestPassword, Port);
                        client.Config.DataConnectionType = FluentFTP.FtpDataConnectionType.PASV;
                        client.Config.ConnectTimeout = 3000;
                        client.Config.DataConnectionConnectTimeout = 3000;
                        await client.Connect(cancellationToken);
                        await client.GetListing("/", cancellationToken);
                        return (Ready: true, Error: "");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        return (Ready: false, Error: ex.Message);
                    }
                },
                state => state.Ready,
                TimeSpan.FromSeconds(20),
                pollInterval: TimeSpan.FromMilliseconds(500),
                describe: state => state.Error);

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
