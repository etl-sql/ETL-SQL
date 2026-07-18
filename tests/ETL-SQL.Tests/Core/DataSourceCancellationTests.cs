using System;
using System.Threading;
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
}
