using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Connectors.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ETL_SQL.Tests.Core;

/// <summary>
/// SQLite exposes declared types, NOT NULL and primary-key position through
/// <c>PRAGMA table_info</c>, but the connector previously read only the column name, so the metadata
/// layer fell back to <c>ANY</c> for every SQLite column in the schema and session explorers.
///
/// These run against a real on-disk SQLite database — the engine is embedded, so no container or
/// external server is needed and this is a normal unit test rather than an integration one.
/// </summary>
[Trait("Category", "Connectors")]
public sealed class SqliteCatalogProviderTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"etlsql-sqlite-catalog-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={_dbPath}";

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch
        {
            // A leftover temp database must never fail a test run.
        }
    }

    [Fact]
    public async Task GetColumnMetadata_ReturnsDeclaredTypesNullabilityAndPrimaryKey()
    {
        await ExecuteAsync("""
            CREATE TABLE Orders (
                OrderID   INTEGER PRIMARY KEY,
                Customer  TEXT NOT NULL,
                Total     REAL,
                Placed    DATETIME NOT NULL,
                Notes     TEXT
            );
            """);

        var columns = await new SqliteCatalogProvider(ConnectionString)
            .GetColumnMetadataAsync(schema: string.Empty, tableName: "Orders");

        Assert.Equal(5, columns.Count);

        var orderId = columns.Single(c => c.ColumnName == "OrderID");
        Assert.Equal("INTEGER", orderId.DataType);
        Assert.True(orderId.IsPrimaryKey);

        // Declared types are reported verbatim rather than normalised — REAL and DATETIME are what
        // the schema actually says, and rewriting them would misrepresent the source.
        Assert.Equal("REAL", columns.Single(c => c.ColumnName == "Total").DataType);
        Assert.Equal("DATETIME", columns.Single(c => c.ColumnName == "Placed").DataType);

        Assert.False(columns.Single(c => c.ColumnName == "Customer").IsNullable);
        Assert.True(columns.Single(c => c.ColumnName == "Notes").IsNullable);
        Assert.False(columns.Single(c => c.ColumnName == "Customer").IsPrimaryKey);
    }

    [Fact]
    public async Task GetColumnMetadata_ReportsAnyForAnUntypedColumn()
    {
        // SQLite is dynamically typed and permits a column with no declared type at all. Reporting
        // ANY is honest; inventing a type from the stored values would be a guess presented as fact.
        await ExecuteAsync("CREATE TABLE Loose (Anything, Named TEXT);");

        var columns = await new SqliteCatalogProvider(ConnectionString)
            .GetColumnMetadataAsync(string.Empty, "Loose");

        Assert.Equal("ANY", columns.Single(c => c.ColumnName == "Anything").DataType);
        Assert.Equal("TEXT", columns.Single(c => c.ColumnName == "Named").DataType);
    }

    [Fact]
    public async Task GetColumnMetadata_ReturnsEmptyForUnknownTable()
    {
        await ExecuteAsync("CREATE TABLE Present (Id INTEGER);");

        var columns = await new SqliteCatalogProvider(ConnectionString)
            .GetColumnMetadataAsync(string.Empty, "NoSuchTable");

        // PRAGMA yields no rows rather than failing; an unknown table must not throw and abort the
        // caller's whole schema read.
        Assert.Empty(columns);
    }

    [Fact]
    public async Task GetColumnMetadata_HandlesAnIdentifierContainingAQuote()
    {
        // The table name is embedded in the PRAGMA text because PRAGMA takes no parameters, so the
        // quote-doubling has to hold or the statement terminates early.
        await ExecuteAsync("""CREATE TABLE "Odd""Name" (Id INTEGER, Label TEXT);""");

        var columns = await new SqliteCatalogProvider(ConnectionString)
            .GetColumnMetadataAsync(string.Empty, "Odd\"Name");

        Assert.Equal(2, columns.Count);
        Assert.Equal("TEXT", columns.Single(c => c.ColumnName == "Label").DataType);
    }

    [Fact]
    public async Task GetRelationships_ReturnsDeclaredForeignKeys()
    {
        await ExecuteAsync("""
            CREATE TABLE Customers (CustomerID INTEGER PRIMARY KEY, Name TEXT);
            CREATE TABLE Invoices (
                InvoiceID  INTEGER PRIMARY KEY,
                CustomerID INTEGER REFERENCES Customers(CustomerID)
            );
            """);

        var relationships = await new SqliteCatalogProvider(ConnectionString)
            .GetRelationshipsAsync(string.Empty, "Invoices");

        var fk = Assert.Single(relationships);
        Assert.Equal("CustomerID", fk.ForeignKeyColumn);
        Assert.Equal("Customers", fk.ReferencedTable);
        Assert.Equal("CustomerID", fk.ReferencedColumn);
    }

    [Fact]
    public async Task GetRelationships_ReturnsEmptyWhenNoneAreDeclared()
    {
        await ExecuteAsync("CREATE TABLE Standalone (Id INTEGER PRIMARY KEY);");

        Assert.Empty(await new SqliteCatalogProvider(ConnectionString)
            .GetRelationshipsAsync(string.Empty, "Standalone"));
    }
}
