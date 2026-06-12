using System.Threading.Tasks;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.Oracle;
using Testcontainers.PostgreSql;
using Xunit;

namespace ETL_SQL.Tests.Integration
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
            _postgres = new PostgreSqlBuilder("postgres:15-alpine")
                .WithName("etl-sql-postgres")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .Build();
            _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .WithName("etl-sql-mssql")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .Build();
            _oracle = new OracleBuilder("gvenzl/oracle-free:latest")
                .WithPassword("Oracle123")
                .WithName("etl-sql-oracle")
                .WithLabel("test-suite", "ETL-SQL.Integration")
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
            // DisposeAsync removes the container and its anonymous volumes; StopAsync leaves both
            // on disk if Ryuk never runs (e.g. a killed/crashed test process), leaking multi-GB
            // database data volumes run over run.
            await Task.WhenAll(
                _postgres.DisposeAsync().AsTask(),
                _sqlServer.DisposeAsync().AsTask(),
                _oracle.DisposeAsync().AsTask()
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

    public class MySqlFixture : IAsyncLifetime
    {
        private readonly MySqlContainer _mysql;

        public string MySqlConnectionString { get; private set; } = "";

        public MySqlFixture()
        {
            _mysql = new MySqlBuilder("mysql:8.0")
                .WithName("etl-sql-mysql")
                .WithLabel("test-suite", "ETL-SQL.Integration")
                .Build();
        }

        public async Task InitializeAsync()
        {
            await _mysql.StartAsync();
            MySqlConnectionString = _mysql.GetConnectionString();
        }

        public async Task DisposeAsync()
        {
            await _mysql.DisposeAsync();
        }
    }

    [CollectionDefinition("MySQL collection")]
    public class MySqlCollection : ICollectionFixture<MySqlFixture>
    {
    }
}
