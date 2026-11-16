using ETL_SQL.Connectors.MockDb;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;

namespace ETL_SQL.Tests.Connectors;

[Trait("Category", "Connectors")]
[Trait("CompatBreak", "0.22")]
public sealed class MockDbInitializationTests
{
    [Fact]
    public async Task DeclaredMetadata_DoesNotGenerateRows()
    {
        var seeder = new RecordingSeeder();
        await using var source = new MockSqlDataSource(SystemExecutionContext.Instance, "", "MockDB", seeder: seeder);
        Assert.Equal(0, seeder.Calls);
        Assert.Equal(["Users"], await source.GetTablesAsync());
        Assert.Equal(["Value"], await source.GetColumnsAsync("Users"));
        source.WithTable("Users");
        Assert.Equal(["Value"], await source.GetColumnsAsync());
        var columns = await source.GetCatalogProvider()!.GetColumnMetadataAsync("", "Users");
        Assert.Equal("DECIMAL", Assert.Single(columns).DataType);
        Assert.Equal(0, seeder.Calls);
    }

    [Fact]
    public async Task ConcurrentFirstReads_AwaitOneCompleteInitialization()
    {
        var seeder = new RecordingSeeder { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var source = new MockSqlDataSource(SystemExecutionContext.Instance, "", "MockDB", seeder: seeder);
        await using var first = source.WithTable("Users").ReadBatches().GetAsyncEnumerator();
        await using var second = source.ReadBatches().GetAsyncEnumerator();
        var firstRead = first.MoveNextAsync().AsTask();
        var secondRead = second.MoveNextAsync().AsTask();
        // flaky-wait-budget-ok: observe the controlled seeder entering its deliberately closed gate.
        await seeder.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(firstRead.IsCompleted);
        Assert.False(secondRead.IsCompleted);
        seeder.Release.SetResult();
        Assert.True(await firstRead);
        Assert.True(await secondRead);
        Assert.Equal(42m, Assert.Single(first.Current.Rows)["Value"]);
        Assert.Equal(42m, Assert.Single(second.Current.Rows)["Value"]);
        Assert.Equal(1, seeder.Calls);
    }

    [Fact]
    public async Task AlreadyCancelledRead_DoesNotStartInitialization()
    {
        var seeder = new RecordingSeeder();
        await using var source = new MockSqlDataSource(SystemExecutionContext.Instance, "", "MockDB", seeder: seeder);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.ReadBatches(10000, cancellation.Token)) { }
        });
        Assert.Equal(0, seeder.Calls);
    }

    [Fact]
    public async Task CancellingAReader_DoesNotCancelSharedInitialization()
    {
        var seeder = new RecordingSeeder { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var source = new MockSqlDataSource(SystemExecutionContext.Instance, "", "MockDB", seeder: seeder);
        using var cancellation = new CancellationTokenSource();
        await using var cancelled = source.ReadBatches(10000, cancellation.Token).GetAsyncEnumerator();
        var cancelledRead = cancelled.MoveNextAsync().AsTask();
        // flaky-wait-budget-ok: the controlled seeder waits on its closed gate before cancellation.
        await seeder.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            // flaky-wait-budget-ok: cancellation must finish while the seeding gate remains closed.
            await cancelledRead.WaitAsync(TimeSpan.FromSeconds(10));
        });
        seeder.Release.SetResult();
        await foreach (var batch in source.ReadBatches()) Assert.Equal(42m, Assert.Single(batch.Rows)["Value"]);
        Assert.Equal(1, seeder.Calls);
    }

    [Fact]
    public async Task UndeclaredMetadata_LoadsTheSeededSchemaOnce()
    {
        var seeder = new RecordingSeeder { DeclareSchema = false };
        await using var source = new MockSqlDataSource(SystemExecutionContext.Instance, "", "MockDB", seeder: seeder);
        Assert.Equal(["Users"], await source.GetTablesAsync());
        Assert.Equal(["Value"], await source.GetColumnsAsync("Users"));
        Assert.Equal(1, seeder.Calls);
    }

    [Fact]
    public async Task TruncateBeforeFirstRead_EmptiesTheSeededTable()
    {
        var seeder = new RecordingSeeder();
        await using var source = new MockSqlDataSource(SystemExecutionContext.Instance, "", "MockDB", seeder: seeder);
        source.WithTable("Users");
        await source.TruncateAsync();
        await foreach (var batch in source.ReadBatches()) Assert.Empty(batch.Rows);
        Assert.Equal(1, seeder.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedInitialization_NeverReturnsPartialRowsOrProviderDetails(bool throwSynchronously)
    {
        var seeder = new RecordingSeeder
        {
            Failure = new InvalidOperationException("private-seeder-detail"),
            ThrowSynchronously = throwSynchronously
        };
        await using var source = new MockSqlDataSource(SystemExecutionContext.Instance, "", "MockDB", seeder: seeder);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failure = await Assert.ThrowsAsync<ExecutionException>(async () =>
            {
                await foreach (var _ in source.ReadBatches()) { }
            });
            Assert.Equal("MOCKDB data initialization failed.", failure.Message);
            Assert.Null(failure.InnerException);
        }
        Assert.Equal(1, seeder.Calls);
    }

    private sealed class RecordingSeeder : IMockDataSeeder
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public bool DeclareSchema { get; init; } = true;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release { get; init; }
        public Exception? Failure { get; init; }
        public bool ThrowSynchronously { get; init; }

        public IReadOnlyDictionary<string, IReadOnlyList<CatalogColumn>> GetDeclaredSchema()
            => DeclareSchema ? new Dictionary<string, IReadOnlyList<CatalogColumn>>
            {
                ["Users"] = [new("Value", "DECIMAL", false, false, null, new Dictionary<string, string>())]
            } : new Dictionary<string, IReadOnlyList<CatalogColumn>>();

        public Task SeedDataAsync(Dictionary<string, DataTable> tables, Random rng)
        {
            Interlocked.Increment(ref calls);
            Started.TrySetResult();
            if (ThrowSynchronously && Failure is not null) throw Failure;
            return PopulateAsync(tables);
        }

        private async Task PopulateAsync(Dictionary<string, DataTable> tables)
        {
            if (Release is not null) await Release.Task;
            var table = new DataTable();
            table.SetColumns(["Value"]);
            await table.AddRowAsync(new Row { ["Value"] = 42m });
            tables["Users"] = table;
            if (Failure is not null) throw Failure;
        }
    }
}
