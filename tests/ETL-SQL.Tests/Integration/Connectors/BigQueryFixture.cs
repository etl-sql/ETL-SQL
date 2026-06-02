using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors
{
    /// <summary>
    /// Starts a goccy/bigquery-emulator container and exposes connection details
    /// for the BigQuery integration test suite.
    ///
    /// Run with: dotnet test --filter "Category=Integration"
    ///
    /// The emulator REST API runs on port 9050. BIGQUERY_EMULATOR_HOST is set
    /// as a process env var scoped to the test run so the BigQueryClientBuilder
    /// can route to it automatically via EmulatorDetection.EmulatorOrProduction.
    /// </summary>
    public class BigQueryFixture : IAsyncLifetime
    {
        public const string TestProject = "test-project";
        public const string TestDataset = "test-dataset";
        private const int EmulatorPort = 9050;

        private IContainer? _container;

        public int Port { get; private set; }
        public string EmulatorHost => $"localhost:{Port}";

        public async Task InitializeAsync()
        {
            _container = new ContainerBuilder("ghcr.io/goccy/bigquery-emulator:latest")
                .WithName("etl-sql-bigquery")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .WithCommand($"--project={TestProject}", $"--dataset={TestDataset}")
                .WithPortBinding(EmulatorPort, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("listening"))
                .Build();

            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(EmulatorPort);

            // BigQueryClientBuilder checks this env var when EmulatorDetection = EmulatorOrProduction.
            Environment.SetEnvironmentVariable("BIGQUERY_EMULATOR_HOST", EmulatorHost);
        }

        public async Task DisposeAsync()
        {
            Environment.SetEnvironmentVariable("BIGQUERY_EMULATOR_HOST", null);

            if (_container != null)
                await _container.DisposeAsync();
        }
    }

    [CollectionDefinition("BigQuery collection")]
    public class BigQueryCollection : ICollectionFixture<BigQueryFixture> { }
}
