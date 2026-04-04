using System.Threading.Tasks;
using Xunit;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.Oracle;

namespace ETL_SQL.Tests
{
    public class DatabaseFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres;
        private readonly MsSqlContainer _sqlServer;
        private readonly OracleContainer _oracle;

        public string PostgresConnectionString { get; private set; } = "";
        public string SqlConnectionString { get; private set; } = "";
        public string OracleConnectionString { get; private set; } = "";

        public DatabaseFixture()
        {
            _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
            _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            _oracle = new OracleBuilder("gvenzl/oracle-free:latest")
                .WithPassword("Oracle123")
                .Build();
        }

        public async Task InitializeAsync()
        {
            await Task.WhenAll(
                _postgres.StartAsync(),
                _sqlServer.StartAsync(),
                _oracle.StartAsync()
            );

            PostgresConnectionString = _postgres.GetConnectionString();
            SqlConnectionString = _sqlServer.GetConnectionString();
            
            var oraConn = _oracle.GetConnectionString();
            OracleConnectionString = oraConn.Replace("SERVICE_NAME=XE", "SERVICE_NAME=FREEPDB1");
        }

        public async Task DisposeAsync()
        {
            await Task.WhenAll(
                _postgres.StopAsync(),
                _sqlServer.StopAsync(),
                _oracle.StopAsync()
            );
        }
    }

    [CollectionDefinition("Database collection")]
    public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
