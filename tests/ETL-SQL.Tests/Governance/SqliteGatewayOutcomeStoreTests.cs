using System.Diagnostics;
using System.Text.Json;
using ETL_SQL.Core.Governance;
using ETL_SQL.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace ETL_SQL.Tests.Governance;

public sealed class SqliteGatewayOutcomeStoreTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gateway-outcomes-" + Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_root, "outcomes.db");

    [Fact]
    public void SecondDaemonCannotPromoteAnActiveDaemonsWrites()
    {
        using var store = new SqliteGatewayOutcomeStore(DatabasePath);
        store.Save(Receipt("active") with { State = GatewayOutcomeState.InFlight });
        Assert.Throws<GatewayProtocolException>(() => new SqliteGatewayOutcomeStore(DatabasePath));
        Assert.Equal(GatewayOutcomeState.InFlight, store.Find("tenant", "active")!.State);
    }

    [Fact]
    public void RetentionExpiresReadsButPreservesWriteDecisionsAndAmbiguousMetadata()
    {
        var clock = new LedgerClock();
        var policy = new GatewayOutcomeRetentionOptions { ReadOnlyRetentionDays = 1, ReceiptDetailRetentionDays = 2, MaintenanceBatchSize = 2 };
        using (var store = new SqliteGatewayOutcomeStore(DatabasePath, policy, clock))
        {
            store.Save(Receipt("read") with { Effect = GatewayOperationEffect.ReadOnly });
            store.Save(Receipt("committed"));
            store.Save(Receipt("failed") with { State = GatewayOutcomeState.Failed });
            store.Save(Receipt("ambiguous") with { State = GatewayOutcomeState.Ambiguous });
            store.Save(Receipt("interrupted") with { State = GatewayOutcomeState.InFlight });
            store.Save(Receipt("committed") with { TenantId = "other", RowsProduced = 7 });
            clock.Now = clock.Now.AddDays(3);
            store.Maintain();
            store.Maintain();
            Assert.Null(store.Find("tenant", "read"));
            Assert.Null(store.Find("tenant", "committed")!.Detail);
            Assert.Equal("triage detail", store.Find("tenant", "ambiguous")!.Detail);
        }
        using var restarted = new SqliteGatewayOutcomeStore(DatabasePath, policy, clock);
        var ledger = new GatewayOutcomeLedger(restarted);
        Assert.Equal(GatewayReconnectAction.ReturnRecordedOutcome, ledger.DecideReconnect("tenant", "committed"));
        Assert.Equal(GatewayReconnectAction.RetrySafely, ledger.DecideReconnect("tenant", "failed"));
        Assert.Equal(GatewayReconnectAction.EscalateAmbiguous, ledger.DecideReconnect("tenant", "interrupted"));
        Assert.Equal(GatewayReconnectAction.EscalateAmbiguous, ledger.DecideReconnect("tenant", "ambiguous"));
        Assert.Equal(7, ledger.Find("other", "committed")!.RowsProduced);
        Assert.Equal(2, ledger.ListAmbiguousMutating("tenant").Count);
        Assert.Empty(ledger.ListAmbiguousMutating("other"));
    }

    [Fact]
    public async Task LegacyImportIsAtomicAndDoesNotOverwriteNewerReceipts()
    {
        using var store = new SqliteGatewayOutcomeStore(DatabasePath);
        var legacyPath = Path.Combine(_root, "outcomes.json");
        await File.WriteAllTextAsync(legacyPath, "[" + JsonSerializer.Serialize(Receipt("first")) + ",broken]");
        await Assert.ThrowsAsync<GatewayProtocolException>(() => store.ImportLegacyAsync(legacyPath));
        Assert.Null(store.Find("tenant", "first"));
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new[] { Receipt("first") with { State = GatewayOutcomeState.InFlight } }));
        await store.ImportLegacyAsync(legacyPath);
        Assert.Equal(GatewayOutcomeState.Ambiguous, store.Find("tenant", "first")!.State);
        store.Save(Receipt("first"));
        await store.ImportLegacyAsync(legacyPath);
        Assert.Equal(GatewayOutcomeState.Committed, store.Find("tenant", "first")!.State);
        Assert.True(File.Exists(legacyPath));
    }

    [Fact]
    public void FailedSaveDoesNotPublishAnUndurableOutcome()
    {
        using var store = new SqliteGatewayOutcomeStore(DatabasePath);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER RejectWrite BEFORE INSERT ON Outcomes BEGIN SELECT RAISE(ABORT, 'injected disk failure'); END";
        command.ExecuteNonQuery();
        var ledger = new GatewayOutcomeLedger(store);
        var operation = new GatewayOperation("failed", "tenant", "gateway", "resource", GatewayOperationClass.Read,
            GatewayOperationEffect.Mutating, GatewayOperationBounds.Default, "correlation");
        Assert.Throws<GatewayProtocolException>(() => ledger.RecordDispatched(operation));
        Assert.Null(ledger.Find("tenant", "failed"));
    }

    [Fact]
    public async Task TenThousandReceiptsDoNotAmplifyTransitionWritesOrRestartMemory()
    {
        var policy = new GatewayOutcomeRetentionOptions { MaintenanceEveryTransitions = int.MaxValue };
        long earlyBytes;
        using (var store = new SqliteGatewayOutcomeStore(DatabasePath, policy))
        {
            earlyBytes = MeasureTransitions(store, "early");
            var legacyPath = Path.Combine(_root, "outcomes.json");
            await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(
                Enumerable.Range(0, 10_000).Select(i => Receipt("history-" + i) with { Detail = new string('x', 2048) })));
            await store.ImportLegacyAsync(legacyPath);
            var lateBytes = MeasureTransitions(store, "late");
            output.WriteLine($"100 transitions: empty history WAL={earlyBytes:N0} bytes; 10,000 receipts WAL={lateBytes:N0} bytes.");
            Assert.True(lateBytes < earlyBytes * 4, $"Transition writes amplified: {earlyBytes} -> {lateBytes}.");
        }
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        using var restarted = new SqliteGatewayOutcomeStore(DatabasePath, policy);
        Assert.Equal(GatewayOutcomeState.Committed, restarted.Find("tenant", "history-9999")!.State);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        output.WriteLine($"Restart + indexed lookup: {allocated:N0} managed bytes, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.True(allocated < 2_000_000, $"Restart materialized history: {allocated} bytes.");
    }

    private long MeasureTransitions(SqliteGatewayOutcomeStore store, string prefix)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        command.ExecuteNonQuery();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) store.Save(Receipt(prefix + i));
        output.WriteLine($"{prefix}: 100 durable transitions in {timer.Elapsed.TotalMilliseconds:F1} ms.");
        return new FileInfo(DatabasePath + "-wal").Length;
    }

    private static GatewayOperationOutcome Receipt(string operationId) => new(
        operationId, "tenant", GatewayOutcomeState.Committed, GatewayOperationEffect.Mutating, 3,
        "triage detail", "gateway", "resource", "correlation", DateTimeOffset.UtcNow);

    private sealed class LedgerClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
