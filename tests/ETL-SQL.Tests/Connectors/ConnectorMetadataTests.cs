using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Reflection;
using System.Threading.Tasks;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Xunit;
using ETL_SQL.Connectors;
using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Postgres;
using MySqlConnectorObj = ETL_SQL.Connectors.MySql.MySqlConnector;
using MySqlConnector;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.Directory;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.Snowflake;
using ETL_SQL.Connectors.BigQuery;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Tests.Connectors
{
    /// <summary>
    /// Tests connector metadata (Name, Aliases, IsFileBased, GetHelp, GetSupportedOptions,
    /// GetOptionValues, BuildConnectionString, GetTablesAsync) without requiring live connections.
    /// </summary>
    [Trait("Connector", "MULTIPLE")]
    [Trait("CertificationClass", "MetadataOnly")]
    public class ConnectorMetadataTests
    {
        private static SystemExecutionContext Ctx => SystemExecutionContext.Instance;

        // ── OdbcConnector ─────────────────────────────────────────────────────

        [Fact]
        public void OdbcConnector_Name_IsOdbc()
        {
            var c = new OdbcConnector();
            Assert.Equal("ODBC", c.Name);
        }

        [Fact]
        public void OdbcConnector_Aliases_NotEmpty()
        {
            var c = new OdbcConnector();
            Assert.NotEmpty(c.Aliases);
        }

        [Fact]
        public void OdbcConnector_GetHelp_ReturnsText()
        {
            var c = new OdbcConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void OdbcConnector_GetSupportedOptions_NotNull()
        {
            var c = new OdbcConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void OdbcConnector_GetOptionValues_ReturnsDict()
        {
            var c = new OdbcConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public void OdbcConnector_BuildConnectionString_WithDsn()
        {
            var c = new OdbcConnector();
            var result = c.BuildConnectionString(new Dictionary<string, string> { ["DSN"] = "MyDSN" });
            Assert.Contains("DSN", result);
        }

        [Fact]
        public void OdbcConnector_BuildConnectionString_WithDriver()
        {
            var c = new OdbcConnector();
            var result = c.BuildConnectionString(new Dictionary<string, string>
            {
                ["DRIVER"] = "SQL Server",
                ["SERVER"] = "localhost",
                ["DATABASE"] = "TestDB"
            });
            Assert.Contains("DRIVER", result);
        }

        [Fact]
        public void OdbcConnector_GetSupportedFunctions_NotNull()
        {
            var c = new OdbcConnector();
            Assert.NotNull(c.GetSupportedFunctions());
        }

        [Fact]
        public void OdbcConnector_GetSupportedKeywords_NotNull()
        {
            var c = new OdbcConnector();
            Assert.NotNull(c.GetSupportedKeywords());
        }

        [Fact]
        public void OdbcConnector_GetHost_WithOptions()
        {
            var result = OdbcConnector.GetHostStatic("MyDSN", new Dictionary<string, string> { ["SERVER"] = "myhost" });
            Assert.Equal("myhost", result);
        }

        [Fact]
        public void OdbcConnector_GetHost_NoOptions_ReturnsNullOrString()
        {
            var result = OdbcConnector.GetHostStatic("SERVER=myhost;DATABASE=mydb");
            Assert.Equal("myhost", result);
        }

        [Fact]
        public void OdbcDataSource_CreateCommand_AppliesTimeoutWithoutRecursing()
        {
            var ds = new OdbcDataSource(
                Ctx,
                "DSN=LocalTest",
                options: new Dictionary<string, string> { ["TIMEOUT_SECONDS"] = "11" });
            using var conn = new OdbcConnection();

            var method = typeof(OdbcDataSource).GetMethod("CreateCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cmd = (OdbcCommand)method.Invoke(ds, new object[] { "SELECT 1", conn })!;

            Assert.Equal("SELECT 1", cmd.CommandText);
            Assert.Equal(11, cmd.CommandTimeout);
        }

        // ── OracleConnector ───────────────────────────────────────────────────

        [Fact]
        public void OracleConnector_Name_IsOracle()
        {
            var c = new OracleConnector();
            Assert.Equal("ORACLE", c.Name);
        }

        [Fact]
        public void OracleConnector_GetHelp_ReturnsText()
        {
            var c = new OracleConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void OracleConnector_GetSupportedOptions_NotNull()
        {
            var c = new OracleConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void OracleConnector_GetOptionValues_NotNull()
        {
            var c = new OracleConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public void OracleConnector_BuildConnectionString_WithProperties()
        {
            var c = new OracleConnector();
            var result = c.BuildConnectionString(new Dictionary<string, string>
            {
                ["HOST"] = "oraserver",
                ["PORT"] = "1521",
                ["SERVICE_NAME"] = "ORCL",
                ["USER"] = "admin",
                ["PASSWORD"] = "pass"
            });
            Assert.NotEmpty(result);
        }

        [Fact]
        public void OracleConnector_GetSupportedFunctions_NotNull()
        {
            var c = new OracleConnector();
            Assert.NotNull(c.GetSupportedFunctions());
        }

        [Fact]
        public void OracleDataSource_CreateCommand_BindsByNameAndAppliesTimeout()
        {
            var ds = new ETL_SQL.Connectors.Oracle.OracleDataSource(
                Ctx,
                "User Id=user;Password=pass;Data Source=localhost/XEPDB1",
                options: new Dictionary<string, string> { ["TIMEOUT_SECONDS"] = "7" });
            using var conn = new OracleConnection();

            var method = typeof(ETL_SQL.Connectors.Oracle.OracleDataSource).GetMethod("CreateCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cmd = (OracleCommand)method.Invoke(ds, new object[] { "SELECT :p1 FROM dual", conn })!;

            Assert.Equal("SELECT :p1 FROM dual", cmd.CommandText);
            Assert.True(cmd.BindByName);
            Assert.Equal(7, cmd.CommandTimeout);
        }

        // ── PostgresConnector ─────────────────────────────────────────────────

        [Fact]
        public void PostgresConnector_Name_IsPostgres()
        {
            var c = new PostgresConnector();
            Assert.Equal("POSTGRES", c.Name);
        }

        [Fact]
        public void PostgresConnector_GetHelp_ReturnsText()
        {
            var c = new PostgresConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void PostgresConnector_GetSupportedOptions_NotNull()
        {
            var c = new PostgresConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void PostgresConnector_GetOptionValues_NotNull()
        {
            var c = new PostgresConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public void PostgresConnector_BuildConnectionString_WithProperties()
        {
            var c = new PostgresConnector();
            var result = c.BuildConnectionString(new Dictionary<string, string>
            {
                ["HOST"] = "pgserver",
                ["DATABASE"] = "mydb",
                ["USER"] = "pguser",
                ["PASSWORD"] = "secret"
            });
            Assert.NotEmpty(result);
        }

        [Fact]
        public void PostgresConnector_Aliases_NotNull()
        {
            var c = new PostgresConnector();
            Assert.NotNull(c.Aliases);
        }

        [Fact]
        public void PostgresDataSource_CreateCommand_AppliesTimeoutWithoutRecursing()
        {
            var ds = new PostgresDataSource(
                Ctx,
                "Host=localhost;Username=user;Password=pass;Database=db",
                options: new Dictionary<string, string> { ["TIMEOUT_SECONDS"] = "9" });
            using var conn = new NpgsqlConnection();

            var method = typeof(PostgresDataSource).GetMethod("CreateCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cmd = (NpgsqlCommand)method.Invoke(ds, new object[] { "SELECT 1", conn })!;

            Assert.Equal("SELECT 1", cmd.CommandText);
            Assert.Equal(9, cmd.CommandTimeout);
        }

        // ── MySqlConnector ────────────────────────────────────────────────────

        [Fact]
        public void MySqlConnector_Name_IsMySql()
        {
            var c = new MySqlConnectorObj();
            Assert.Equal("MYSQL", c.Name);
        }

        [Fact]
        public void MySqlConnector_Aliases_IncludesMariaDb()
        {
            var c = new MySqlConnectorObj();
            Assert.Contains("MARIADB", c.Aliases);
        }

        [Fact]
        public void MySqlConnector_GetHelp_ReturnsText()
        {
            var c = new MySqlConnectorObj();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void MySqlConnector_GetSupportedOptions_NotNull()
        {
            var c = new MySqlConnectorObj();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void MySqlConnector_GetOptionValues_NotNull()
        {
            var c = new MySqlConnectorObj();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public void MySqlConnector_BuildConnectionString_WithProperties()
        {
            var c = new MySqlConnectorObj();
            var result = c.BuildConnectionString(new Dictionary<string, string>
            {
                ["HOST"] = "mysqlserver",
                ["DATABASE"] = "mydb",
                ["USER"] = "mysqluser",
                ["PASSWORD"] = "secret",
                ["PORT"] = "3306",
                ["SSL_MODE"] = "VerifyFull",
                ["ALLOW_PUBLIC_KEY_RETRIEVAL"] = "TRUE",
                ["ALLOW_USER_VARIABLES"] = "TRUE"
            });
            Assert.NotEmpty(result);
            Assert.Contains("Server=mysqlserver", result);
            Assert.Contains("Database=mydb", result);
            Assert.Contains("User ID=mysqluser", result);
            Assert.Contains("Password=secret", result);
            Assert.Contains("Port=3306", result);
            Assert.Contains("SSL Mode=VerifyFull", result);
            Assert.Contains("Allow Public Key Retrieval=True", result);
            Assert.Contains("Allow User Variables=True", result);
        }

        [Fact]
        public void MySqlDataSource_CreateCommand_AppliesTimeoutWithoutRecursing()
        {
            var ds = new ETL_SQL.Connectors.MySql.MySqlDataSource(
                Ctx,
                "Server=localhost;Database=db;Uid=user;Pwd=pass",
                options: new Dictionary<string, string> { ["TIMEOUT_SECONDS"] = "9" });
            using var conn = new MySqlConnection();

            var method = typeof(ETL_SQL.Connectors.MySql.MySqlDataSource).GetMethod("CreateCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cmd = (MySqlCommand)method.Invoke(ds, new object[] { "SELECT 1", conn })!;

            Assert.Equal("SELECT 1", cmd.CommandText);
            Assert.Equal(9, cmd.CommandTimeout);
        }

        // ── RestConnector ─────────────────────────────────────────────────────

        [Fact]
        public void RestConnector_Name_IsApi()
        {
            var c = new RestConnector();
            Assert.Equal("API", c.Name);
        }

        [Fact]
        public void RestConnector_GetHelp_ReturnsText()
        {
            var c = new RestConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void RestConnector_GetSupportedOptions_NotNull()
        {
            var c = new RestConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void RestConnector_GetOptionValues_NotNull()
        {
            var c = new RestConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public void RestConnector_BuildConnectionString_WithUrl()
        {
            var c = new RestConnector();
            var result = c.BuildConnectionString(new Dictionary<string, string> { ["URL"] = "https://api.example.com" });
            Assert.Equal("https://api.example.com", result);
        }

        [Fact]
        public void RestConnector_Aliases_ContainsRestAndHttp()
        {
            var c = new RestConnector();
            Assert.Contains("REST", c.Aliases, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("HTTP", c.Aliases, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void RestConnector_GetHost_FromConnectionString()
        {
            var c = new RestConnector();
            var result = c.GetHost("https://api.example.com/v1");
            Assert.NotNull(result);
        }

        // ── ExcelConnector ────────────────────────────────────────────────────

        [Fact]
        public void ExcelConnector_Name_IsExcel()
        {
            var c = new ExcelConnector();
            Assert.Equal("EXCEL", c.Name);
        }

        [Fact]
        public void ExcelConnector_IsFileBased_True()
        {
            var c = new ExcelConnector();
            Assert.True(c.IsFileBased);
        }

        [Fact]
        public void ExcelConnector_GetHelp_ReturnsText()
        {
            var c = new ExcelConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void ExcelConnector_GetSupportedOptions_NotNull()
        {
            var c = new ExcelConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void ExcelConnector_GetOptionValues_NotNull()
        {
            var c = new ExcelConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public async Task ExcelConnector_GetTablesAsync_ReturnsEmpty()
        {
            var c = new ExcelConnector();
            var tables = await c.GetTablesAsync(Ctx, "/path/file.xlsx");
            Assert.Empty(tables);
        }

        // ── DirectoryConnector ────────────────────────────────────────────────

        [Fact]
        public void DirectoryConnector_Name_IsDirectory()
        {
            var c = new DirectoryConnector();
            Assert.Equal("DIRECTORY", c.Name);
        }

        [Fact]
        public void DirectoryConnector_IsFileBased_True()
        {
            var c = new DirectoryConnector();
            Assert.True(c.IsFileBased);
        }

        [Fact]
        public void DirectoryConnector_GetHelp_ReturnsText()
        {
            var c = new DirectoryConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void DirectoryConnector_GetSupportedOptions_NotNull()
        {
            var c = new DirectoryConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void DirectoryConnector_GetOptionValues_NotNull()
        {
            var c = new DirectoryConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        // ── AvroConnector ─────────────────────────────────────────────────────

        [Fact]
        public void AvroConnector_Name_IsAvro()
        {
            var c = new AvroConnector();
            Assert.Equal("AVRO", c.Name);
        }

        [Fact]
        public void AvroConnector_IsFileBased_True()
        {
            var c = new AvroConnector();
            Assert.True(c.IsFileBased);
        }

        [Fact]
        public void AvroConnector_GetHelp_ReturnsText()
        {
            var c = new AvroConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void AvroConnector_GetSupportedOptions_NotNull()
        {
            var c = new AvroConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void AvroConnector_GetOptionValues_NotNull()
        {
            var c = new AvroConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        // ── ParquetConnector ──────────────────────────────────────────────────

        [Fact]
        public void ParquetConnector_Name_IsParquet()
        {
            var c = new ParquetConnector();
            Assert.Equal("PARQUET", c.Name);
        }

        [Fact]
        public void ParquetConnector_IsFileBased_True()
        {
            var c = new ParquetConnector();
            Assert.True(c.IsFileBased);
        }

        [Fact]
        public void ParquetConnector_GetHelp_ReturnsText()
        {
            var c = new ParquetConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void ParquetConnector_GetSupportedOptions_NotNull()
        {
            var c = new ParquetConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void ParquetConnector_GetOptionValues_NotNull()
        {
            var c = new ParquetConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public async Task ParquetConnector_GetTablesAsync_ReturnsFile()
        {
            var c = new ParquetConnector();
            var tables = await c.GetTablesAsync(Ctx, "/path/file.parquet");
            Assert.Contains("FILE", tables);
        }

        // ── MockDbConnector ───────────────────────────────────────────────────

        [Fact]
        public void MockDbConnector_Name_IsMockDb()
        {
            var c = new MockDbConnector();
            Assert.Equal("MOCKDB", c.Name);
        }

        [Fact]
        public void MockDbConnector_GetHelp_ReturnsText()
        {
            var c = new MockDbConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void MockDbConnector_GetSupportedOptions_NotNull()
        {
            var c = new MockDbConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void MockDbConnector_GetOptionValues_NotNull()
        {
            var c = new MockDbConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public void MockDbConnector_Aliases_Empty()
        {
            var c = new MockDbConnector();
            Assert.NotNull(c.Aliases);
        }

        // ── SnowflakeConnector ────────────────────────────────────────────────

        [Fact]
        public void SnowflakeConnector_Name_IsSnowflake()
        {
            var c = new SnowflakeConnector();
            Assert.Equal("SNOWFLAKE", c.Name);
        }

        [Fact]
        public void SnowflakeConnector_GetHelp_ReturnsText()
        {
            var c = new SnowflakeConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void SnowflakeConnector_GetSupportedOptions_NotNull()
        {
            var c = new SnowflakeConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void SnowflakeConnector_GetOptionValues_NotNull()
        {
            var c = new SnowflakeConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        // ── BigQueryConnector ─────────────────────────────────────────────────

        [Fact]
        public void BigQueryConnector_Name_IsBigQuery()
        {
            var c = new BigQueryConnector();
            Assert.Equal("BIGQUERY", c.Name);
        }

        [Fact]
        public void BigQueryConnector_GetHelp_ReturnsText()
        {
            var c = new BigQueryConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void BigQueryConnector_GetSupportedOptions_NotNull()
        {
            var c = new BigQueryConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void BigQueryConnector_GetOptionValues_NotNull()
        {
            var c = new BigQueryConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        // ── AzureBlobConnector ────────────────────────────────────────────────

        [Fact]
        public void AzureBlobConnector_Name_IsAzureBlob()
        {
            var c = new AzureBlobConnector();
            Assert.Equal("AZURE_BLOB", c.Name);
        }

        [Fact]
        public void AzureBlobConnector_GetHelp_ReturnsText()
        {
            var c = new AzureBlobConnector();
            Assert.NotEmpty(c.GetHelp());
        }

        [Fact]
        public void AzureBlobConnector_GetSupportedOptions_NotNull()
        {
            var c = new AzureBlobConnector();
            Assert.NotNull(c.GetSupportedOptions());
        }

        [Fact]
        public void AzureBlobConnector_GetOptionValues_NotNull()
        {
            var c = new AzureBlobConnector();
            Assert.NotNull(c.GetOptionValues());
        }

        [Fact]
        public void AzureBlobConnector_Aliases_ContainsBlob()
        {
            var c = new AzureBlobConnector();
            Assert.Contains("BLOB", c.Aliases, StringComparer.OrdinalIgnoreCase);
        }

        // ── Syntax class coverage (BigQuery, Snowflake) ───────────────────────

        [Fact]
        public void BigQueryConnector_GetSupportedFunctions_NotEmpty()
        {
            var c = new BigQueryConnector();
            Assert.NotEmpty(c.GetSupportedFunctions());
        }

        [Fact]
        public void BigQueryConnector_GetSupportedKeywords_NotEmpty()
        {
            var c = new BigQueryConnector();
            Assert.NotEmpty(c.GetSupportedKeywords());
        }

        [Fact]
        public void BigQueryConnector_GetExcludedKeywords_NotEmpty()
        {
            var c = new BigQueryConnector();
            Assert.NotEmpty(c.GetExcludedKeywords());
        }

        [Fact]
        public void SnowflakeConnector_GetSupportedFunctions_NotEmpty()
        {
            var c = new SnowflakeConnector();
            Assert.NotEmpty(c.GetSupportedFunctions());
        }

        [Fact]
        public void SnowflakeConnector_GetSupportedKeywords_NotEmpty()
        {
            var c = new SnowflakeConnector();
            Assert.NotEmpty(c.GetSupportedKeywords());
        }

        [Fact]
        public void SnowflakeConnector_GetExcludedKeywords_NotEmpty()
        {
            var c = new SnowflakeConnector();
            Assert.NotEmpty(c.GetExcludedKeywords());
        }

        [Fact]
        public void MockDbConnector_GetSupportedFunctions_NotNull()
        {
            var c = new MockDbConnector();
            Assert.NotNull(c.GetSupportedFunctions());
        }

        [Fact]
        public void MockDbConnector_GetSupportedKeywords_NotNull()
        {
            var c = new MockDbConnector();
            Assert.NotNull(c.GetSupportedKeywords());
        }
    }
}
