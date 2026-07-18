using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using ETL_SQL.Connectors.Avro;
using ETL_SQL.Connectors.BigQuery;
using ETL_SQL.Connectors.Directory;
using ETL_SQL.Connectors.Email;
using ETL_SQL.Connectors.Excel;
using ETL_SQL.Connectors.FlatFile;
using ETL_SQL.Connectors.Kafka;
using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Connectors.MySql;
using ETL_SQL.Connectors.Json;
using ETL_SQL.Connectors.Mongodb;
using ETL_SQL.Connectors.Neo4j;
using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Orchestrator;
using ETL_SQL.Connectors.Portal;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.Parquet;
using ETL_SQL.Connectors.Rest;
using ETL_SQL.Connectors.Sqlite;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Connectors.Snowflake;
using ETL_SQL.Connectors.Xml;
using ETL_SQL.Data;
using ETL_SQL.Engine.Storage;
using Xunit;

namespace ETL_SQL.Tests.Core;

public class DataSourceCancellationTests
{
    [Fact]
    public async Task InMemoryDataSource_ReadBatches_ObservesExplicitCancellation()
    {
        await using var source = new InMemoryDataSource();
        var table = new DataTable();
        table.SetColumns(new[] { "id" });
        var row = table.NewRow();
        row["id"] = 1;
        await table.AddRowAsync(row);
        await source.WriteBatches(new[] { table }.ToAsyncEnumerable());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.ReadBatches(1, cancellation.Token))
            {
            }
        });
    }

    [Fact]
    public void SqlProviderDataSources_DeclareNativeCancellationOverloads()
    {
        foreach (var providerType in new[]
        {
            typeof(SqliteDataSource),
            typeof(SqlServerDataSource),
            typeof(PostgresDataSource),
            typeof(MySqlDataSource),
            typeof(OdbcDataSource),
            typeof(OracleDataSource)
        })
        {
            AssertDeclares(providerType, nameof(IDataSource.ReadBatches), typeof(int), typeof(CancellationToken));
            AssertDeclares(
                providerType,
                nameof(IDataSource.WriteBatches),
                typeof(IAsyncEnumerable<DataTable>),
                typeof(bool),
                typeof(CancellationToken));
            AssertDeclares(
                providerType,
                nameof(IDatabaseSource.ExecuteRawSql),
                typeof(string),
                typeof(IEnumerable<object?>),
                typeof(CancellationToken));
        }
    }

    [Fact]
    public void MongoDataSource_DeclaresNativeCancellationOverloads()
    {
        AssertDeclares(typeof(MongodbDataSource), nameof(IDataSource.ReadBatches), typeof(int), typeof(CancellationToken));
        AssertDeclares(
            typeof(MongodbDataSource),
            nameof(IDataSource.WriteBatches),
            typeof(IAsyncEnumerable<DataTable>),
            typeof(bool),
            typeof(CancellationToken));
    }

    [Fact]
    public void GraphDataSources_DeclareNativeCancellationOverloads()
    {
        AssertDeclares(typeof(Neo4jDataSource), nameof(IDataSource.ReadBatches), typeof(int), typeof(CancellationToken));
        AssertDeclares(
            typeof(Neo4jDataSource),
            nameof(IDataSource.WriteBatches),
            typeof(IAsyncEnumerable<DataTable>),
            typeof(bool),
            typeof(CancellationToken));
        AssertDeclares(
            typeof(Neo4jDataSource),
            nameof(IDatabaseSource.ExecuteRawSql),
            typeof(string),
            typeof(IEnumerable<object?>),
            typeof(CancellationToken));
    }

    [Fact]
    public void RestDataSource_DeclaresNativeCancellationOverloads()
    {
        AssertDeclares(typeof(RestDataSource), nameof(IDataSource.ReadBatches), typeof(int), typeof(CancellationToken));
        AssertDeclares(
            typeof(RestDataSource),
            nameof(IDataSource.WriteBatches),
            typeof(IAsyncEnumerable<DataTable>),
            typeof(bool),
            typeof(CancellationToken));
        AssertDeclares(
            typeof(RestDataSource),
            nameof(IDatabaseSource.ExecuteRawSql),
            typeof(string),
            typeof(IEnumerable<object?>),
            typeof(CancellationToken));
    }

    [Fact]
    public void WarehouseDataSources_DeclareNativeCancellationOverloads()
    {
        foreach (var providerType in new[]
        {
            typeof(BigQueryDataSource),
            typeof(SnowflakeDataSource)
        })
        {
            AssertDeclares(providerType, nameof(IDataSource.ReadBatches), typeof(int), typeof(CancellationToken));
            AssertDeclares(
                providerType,
                nameof(IDataSource.WriteBatches),
                typeof(IAsyncEnumerable<DataTable>),
                typeof(bool),
                typeof(CancellationToken));
            AssertDeclares(
                providerType,
                nameof(IDatabaseSource.ExecuteRawSql),
                typeof(string),
                typeof(IEnumerable<object?>),
                typeof(CancellationToken));
        }
    }

    [Fact]
    public void StructuredFileDataSources_DeclareNativeCancellationOverloads()
    {
        foreach (var providerType in new[]
        {
            typeof(AvroDataSource),
            typeof(FlatFileDataSource),
            typeof(ParquetDataSource),
            typeof(JsonDataSource),
            typeof(XmlDataSource)
        })
        {
            AssertDeclares(providerType, nameof(IDataSource.ReadBatches), typeof(int), typeof(CancellationToken));
            AssertDeclares(
                providerType,
                nameof(IDataSource.WriteBatches),
                typeof(IAsyncEnumerable<DataTable>),
                typeof(bool),
                typeof(CancellationToken));
            AssertDeclares(
                providerType,
                nameof(IDatabaseSource.ExecuteRawSql),
                typeof(string),
                typeof(IEnumerable<object?>),
                typeof(CancellationToken));
        }
    }

    [Fact]
    public void ExcelDataSource_DeclaresNativeCancellationOverloads()
    {
        AssertDeclares(typeof(ExcelDataSource), nameof(IDataSource.ReadBatches), typeof(int), typeof(CancellationToken));
        AssertDeclares(
            typeof(ExcelDataSource),
            nameof(IDataSource.WriteBatches),
            typeof(IAsyncEnumerable<DataTable>),
            typeof(bool),
            typeof(CancellationToken));
    }

    [Fact]
    public void MessagingAndUtilityDataSources_DeclareNativeCancellationOverloads()
    {
        foreach (var providerType in new[]
        {
            typeof(DirectoryDataSource),
            typeof(KafkaDataSource),
            typeof(SmtpDataSource),
            typeof(MockSqlDataSource),
            typeof(OrchestratorDataSource),
            typeof(PortalDataSource),
            typeof(LineageDataSource),
            typeof(LineageTagsDataSource),
            typeof(VariableDataSource)
        })
        {
            AssertDeclares(providerType, nameof(IDataSource.ReadBatches), typeof(int), typeof(CancellationToken));
            AssertDeclares(
                providerType,
                nameof(IDataSource.WriteBatches),
                typeof(IAsyncEnumerable<DataTable>),
                typeof(bool),
                typeof(CancellationToken));
        }
    }

    private static void AssertDeclares(Type type, string methodName, params Type[] parameterTypes)
    {
        var method = type.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        Assert.NotNull(method);
        Assert.Equal(type, method!.DeclaringType);
    }
}
