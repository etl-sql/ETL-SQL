using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Core.Common;
using ETL_SQL.Data;
using Testcontainers.MsSql;
using Xunit;

namespace ETL_SQL.Tests.Integration.Connectors;

[Trait("Category", "Integration")]
[Trait("Connector", "ODBC")]
[Trait("CompatBreak", "0.21.0")]
public sealed class OdbcTransactionCloneTests
{
    [Fact]
    public async Task WriteThroughTableCloneRollsBackWithItsOwner()
    {
        // This opt-in lane requires Microsoft's ODBC Driver 17 and a Docker runtime.
        await using var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await server.StartAsync();
        var sql = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(server.GetConnectionString());
        var connection = new System.Data.Odbc.OdbcConnectionStringBuilder
        {
            ["Driver"] = "ODBC Driver 17 for SQL Server",
            ["Server"] = sql.DataSource,
            ["Database"] = sql.InitialCatalog,
            ["UID"] = sql.UserID,
            ["PWD"] = sql.Password,
            ["TrustServerCertificate"] = "yes"
        };
        await using var source = new OdbcDataSource(SystemExecutionContext.Instance, connection.ConnectionString, null, null);
        await source.ExecuteRawSql("CREATE TABLE clone_audit (id INT NOT NULL)").ToListAsync();
        await source.BeginTransactionAsync();
        await using var clone = source.WithTable("clone_audit");
        await clone.WriteBatches(Batches(), append: true);
        await source.RollbackAsync();
        var results = await source.ExecuteRawSql("SELECT COUNT(*) AS total FROM clone_audit").ToListAsync();
        Assert.Equal(0m, Convert.ToDecimal(Assert.Single(results.SelectMany(b => b.Rows))["total"]));
        // The same view sees later transactions and disposing another view cannot end one.
        await source.BeginTransactionAsync();
        await clone.WriteBatches(Batches(), append: true);
        await source.WithTable("clone_audit").DisposeAsync();
        await source.CommitAsync();
        results = await source.ExecuteRawSql("SELECT COUNT(*) AS total FROM clone_audit").ToListAsync();
        Assert.Equal(1m, Convert.ToDecimal(Assert.Single(results.SelectMany(b => b.Rows))["total"]));
    }

    private static async IAsyncEnumerable<DataTable> Batches()
    {
        var batch = new DataTable();
        batch.SetColumns(["id"]);
        var row = batch.NewRow();
        row["id"] = 1m;
        await batch.AddRowAsync(row);
        yield return batch;
    }
}
