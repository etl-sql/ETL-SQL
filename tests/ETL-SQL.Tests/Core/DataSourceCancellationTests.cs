using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using ETL_SQL.Connectors.MySql;
using ETL_SQL.Connectors.Odbc;
using ETL_SQL.Connectors.Oracle;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Connectors.Sqlite;
using ETL_SQL.Connectors.SqlServer;
using ETL_SQL.Data;
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
