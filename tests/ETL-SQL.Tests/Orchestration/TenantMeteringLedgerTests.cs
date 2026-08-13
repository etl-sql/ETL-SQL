using ETL_SQL.Core.Multitenancy;
using ETL_SQL.Orchestrator.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

public sealed class TenantMeteringLedgerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"etlsql-metering-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task EqualSourceIdsAreTenantPartitionedIdempotentAndDurable()
    {
        var alpha = TenantContext.FromVerifiedCredential("tenant-alpha");
        var beta = TenantContext.FromVerifiedCredential("tenant-beta");
        var first = Ledger();
        await first.AppendAsync(alpha, Usage("attempt-1", rows: 11));
        await first.AppendAsync(beta, Usage("attempt-1", rows: 29));
        await first.AppendAsync(alpha, Usage("attempt-1", rows: 999));

        var reopened = Ledger();
        var alphaRows = await reopened.ListAsync(alpha);
        var betaRows = await reopened.ListAsync(beta);

        Assert.Equal(11, Assert.Single(alphaRows).Event.Rows);
        Assert.Equal("tenant-alpha", Assert.Single(alphaRows).TenantId);
        Assert.Equal(29, Assert.Single(betaRows).Event.Rows);
        Assert.DoesNotContain(alphaRows, row => row.TenantId == "tenant-beta");
    }

    [Fact]
    public async Task FixedSchemaCarriesEveryRequiredSharedFleetMeasureWithoutPayloadFields()
    {
        var tenant = TenantContext.FromHostConfiguration("tenant-alpha");
        var usage = Usage("all-dimensions", rows: 3) with
        {
            ConnectorClass = TenantConnectorClass.Gateway,
            BytesRead = 101,
            BytesWritten = 102,
            SandboxCpuMilliseconds = 103,
            SandboxPeakMemoryBytes = 104,
            SandboxIoReadBytes = 105,
            SandboxIoWriteBytes = 106,
            GatewayIngressBytes = 107,
            GatewayEgressBytes = 108,
            StorageBytes = 109,
            ConcurrencyUnits = 2
        };
        var ledger = Ledger();
        await ledger.AppendAsync(tenant, usage);

        var actual = Assert.Single(await ledger.ListAsync(tenant)).Event;

        Assert.Equal(usage, actual);
        var names = typeof(TenantMeteringEvent).GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var forbidden in new[]
                 {
                     "TenantId", "Script", "Parameters", "Payload", "RowContent", "Secret",
                     "ConnectorTarget", "ResourceId", "ObjectName", "Authorized", "Allowed"
                 })
            Assert.DoesNotContain(forbidden, names);
    }

    [Fact]
    public async Task PlatformGrantCannotTurnMeteringIntoTenantRuntimeAuthority()
    {
        var now = DateTimeOffset.UtcNow;
        var grant = PlatformAccessGrant.Issue(
            "tenant-alpha", "operator", "ticket-1", "support", now.AddMinutes(5), now);
        var platform = TenantContext.FromPlatformGrant(grant, now);
        var ledger = Ledger();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ledger.AppendAsync(platform, Usage("attempt-1")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ledger.ListAsync(platform));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public async Task NegativeMeasuresAreRejected(long rows, long bytes, int concurrency)
    {
        var usage = Usage("bad") with
        {
            Rows = rows,
            BytesRead = bytes,
            ConcurrencyUnits = concurrency
        };

        await Assert.ThrowsAsync<ArgumentException>(() => Ledger().AppendAsync(
            TenantContext.FromVerifiedCredential("tenant-alpha"), usage));
    }

    private ITenantMeteringLedger Ledger()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Orchestrator:Database:Provider"] = "Sqlite" }).Build();
        return new OrchestratorStoreFactory(configuration).CreateTenantMeteringLedger(_dbPath);
    }

    private static TenantMeteringEvent Usage(string eventId, long rows = 0) => new()
    {
        SourceEventId = eventId,
        Source = TenantMeteringSource.Scheduler,
        WorkloadClass = TenantWorkloadClass.Script,
        ConnectorClass = TenantConnectorClass.None,
        Status = TenantMeteringStatus.Succeeded,
        Rows = rows,
        DurationMilliseconds = 10,
        RecordedAtUtc = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero)
    };

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
