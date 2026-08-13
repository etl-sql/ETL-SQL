using System.Data.Common;
using System.Text.RegularExpressions;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Orchestrator.Storage;

public enum TenantMeteringSource
{
    Scheduler,
    Sandbox,
    Gateway,
    Storage
}

public enum TenantConnectorClass
{
    None,
    Database,
    Warehouse,
    File,
    ObjectStorage,
    Api,
    Messaging,
    Directory,
    Gateway
}

public enum TenantWorkloadClass
{
    Script,
    Report,
    Refresh,
    Interactive,
    Gateway,
    Storage,
    Support,
    Export,
    Other
}

public enum TenantMeteringStatus
{
    Succeeded,
    Failed,
    Cancelled,
    Ambiguous,
    Sampled
}

/// <summary>
/// Fixed-schema counts-only usage. It intentionally has no tenant, script, parameter, resource,
/// connector target, object name, row sample, secret, or authorization field.
/// </summary>
public sealed partial record TenantMeteringEvent
{
    public required string SourceEventId { get; init; }
    public required TenantMeteringSource Source { get; init; }
    public required TenantWorkloadClass WorkloadClass { get; init; }
    public required TenantConnectorClass ConnectorClass { get; init; }
    public required TenantMeteringStatus Status { get; init; }
    public long Rows { get; init; }
    public long BytesRead { get; init; }
    public long BytesWritten { get; init; }
    public long SandboxCpuMilliseconds { get; init; }
    public long SandboxPeakMemoryBytes { get; init; }
    public long SandboxIoReadBytes { get; init; }
    public long SandboxIoWriteBytes { get; init; }
    public long GatewayIngressBytes { get; init; }
    public long GatewayEgressBytes { get; init; }
    public long StorageBytes { get; init; }
    public int ConcurrencyUnits { get; init; }
    public long DurationMilliseconds { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }

    internal void Validate()
    {
        if (!CanonicalId().IsMatch(SourceEventId ?? string.Empty))
            throw new ArgumentException("Metering source event ids must be canonical opaque identifiers.");
        var measures = new long[]
        {
            Rows, BytesRead, BytesWritten, SandboxCpuMilliseconds, SandboxPeakMemoryBytes,
            SandboxIoReadBytes, SandboxIoWriteBytes, GatewayIngressBytes, GatewayEgressBytes,
            StorageBytes, ConcurrencyUnits, DurationMilliseconds
        };
        if (measures.Any(value => value < 0))
            throw new ArgumentException("Metering measures cannot be negative.");
        if (RecordedAtUtc == default)
            throw new ArgumentException("Metering events require a recorded timestamp.");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalId();

}

public sealed record TenantMeteringRecord(long Id, string TenantId, TenantMeteringEvent Event);

/// <summary>
/// Append/query evidence only. The API deliberately cannot answer an authorization, quota, lease,
/// placement, or admission question; execution policy has no dependency on this interface.
/// </summary>
public interface ITenantMeteringLedger
{
    Task AppendAsync(
        TenantContext tenant,
        TenantMeteringEvent usage,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantMeteringRecord>> ListAsync(
        TenantContext tenant,
        DateTimeOffset? fromUtc = null,
        int limit = 1000,
        CancellationToken cancellationToken = default);
}

public sealed class RelationalTenantMeteringLedger(
    IOrchestratorStoreDialect dialect) : ITenantMeteringLedger
{
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    public async Task AppendAsync(
        TenantContext tenant,
        TenantMeteringEvent usage,
        CancellationToken cancellationToken = default)
    {
        RequireRuntimeTenant(tenant);
        ArgumentNullException.ThrowIfNull(usage);
        usage.Validate();
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO TenantMeteringLedger (
                TenantId, SourceEventId, Source, WorkloadClass, ConnectorClass, Status,
                Rows, BytesRead, BytesWritten, SandboxCpuMilliseconds, SandboxPeakMemoryBytes,
                SandboxIoReadBytes, SandboxIoWriteBytes, GatewayIngressBytes, GatewayEgressBytes,
                StorageBytes, ConcurrencyUnits, DurationMilliseconds, RecordedAtUtc)
            VALUES (
                @tenant, @event, @source, @workload, @connector, @status,
                @rows, @bytesRead, @bytesWritten, @cpu, @memory, @ioRead, @ioWrite,
                @gatewayIn, @gatewayOut, @storage, @concurrency, @duration, @recorded)
            ON CONFLICT (TenantId, Source, SourceEventId) DO NOTHING;";
        Add(command, "@tenant", tenant.Tenant.Value);
        Add(command, "@event", usage.SourceEventId);
        Add(command, "@source", usage.Source.ToString());
        Add(command, "@workload", usage.WorkloadClass.ToString());
        Add(command, "@connector", usage.ConnectorClass.ToString());
        Add(command, "@status", usage.Status.ToString());
        Add(command, "@rows", usage.Rows);
        Add(command, "@bytesRead", usage.BytesRead);
        Add(command, "@bytesWritten", usage.BytesWritten);
        Add(command, "@cpu", usage.SandboxCpuMilliseconds);
        Add(command, "@memory", usage.SandboxPeakMemoryBytes);
        Add(command, "@ioRead", usage.SandboxIoReadBytes);
        Add(command, "@ioWrite", usage.SandboxIoWriteBytes);
        Add(command, "@gatewayIn", usage.GatewayIngressBytes);
        Add(command, "@gatewayOut", usage.GatewayEgressBytes);
        Add(command, "@storage", usage.StorageBytes);
        Add(command, "@concurrency", usage.ConcurrencyUnits);
        Add(command, "@duration", usage.DurationMilliseconds);
        Add(command, "@recorded", usage.RecordedAtUtc.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantMeteringRecord>> ListAsync(
        TenantContext tenant,
        DateTimeOffset? fromUtc = null,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        RequireRuntimeTenant(tenant);
        limit = Math.Clamp(limit, 1, 10_000);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = dialect.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, TenantId, SourceEventId, Source, WorkloadClass, ConnectorClass, Status,
                   Rows, BytesRead, BytesWritten, SandboxCpuMilliseconds, SandboxPeakMemoryBytes,
                   SandboxIoReadBytes, SandboxIoWriteBytes, GatewayIngressBytes, GatewayEgressBytes,
                   StorageBytes, ConcurrencyUnits, DurationMilliseconds, RecordedAtUtc
              FROM TenantMeteringLedger
             WHERE TenantId = @tenant" +
            (fromUtc is null ? string.Empty : " AND RecordedAtUtc >= @from") + @"
             ORDER BY RecordedAtUtc DESC, Id DESC
             LIMIT @limit;";
        Add(command, "@tenant", tenant.Tenant.Value);
        if (fromUtc is not null)
            Add(command, "@from", fromUtc.Value.ToUniversalTime().ToString("O"));
        Add(command, "@limit", limit);
        var rows = new List<TenantMeteringRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(Read(reader));
        return rows;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var connection = dialect.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
                CREATE TABLE IF NOT EXISTS TenantMeteringLedger (
                    Id {dialect.AutoIncrementPrimaryKey},
                    TenantId TEXT NOT NULL,
                    SourceEventId TEXT NOT NULL,
                    Source TEXT NOT NULL,
                    WorkloadClass TEXT NOT NULL,
                    ConnectorClass TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    Rows {dialect.Int64Type} NOT NULL,
                    BytesRead {dialect.Int64Type} NOT NULL,
                    BytesWritten {dialect.Int64Type} NOT NULL,
                    SandboxCpuMilliseconds {dialect.Int64Type} NOT NULL,
                    SandboxPeakMemoryBytes {dialect.Int64Type} NOT NULL,
                    SandboxIoReadBytes {dialect.Int64Type} NOT NULL,
                    SandboxIoWriteBytes {dialect.Int64Type} NOT NULL,
                    GatewayIngressBytes {dialect.Int64Type} NOT NULL,
                    GatewayEgressBytes {dialect.Int64Type} NOT NULL,
                    StorageBytes {dialect.Int64Type} NOT NULL,
                    ConcurrencyUnits INTEGER NOT NULL,
                    DurationMilliseconds {dialect.Int64Type} NOT NULL,
                    RecordedAtUtc TEXT NOT NULL,
                    UNIQUE (TenantId, Source, SourceEventId)
                );
                CREATE INDEX IF NOT EXISTS idx_tenant_metering_time
                    ON TenantMeteringLedger (TenantId, RecordedAtUtc DESC);";
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private static TenantMeteringRecord Read(DbDataReader reader)
    {
        var usage = new TenantMeteringEvent
        {
            SourceEventId = reader.GetString(2),
            Source = Enum.Parse<TenantMeteringSource>(reader.GetString(3)),
            WorkloadClass = Enum.Parse<TenantWorkloadClass>(reader.GetString(4)),
            ConnectorClass = Enum.Parse<TenantConnectorClass>(reader.GetString(5)),
            Status = Enum.Parse<TenantMeteringStatus>(reader.GetString(6)),
            Rows = reader.GetInt64(7),
            BytesRead = reader.GetInt64(8),
            BytesWritten = reader.GetInt64(9),
            SandboxCpuMilliseconds = reader.GetInt64(10),
            SandboxPeakMemoryBytes = reader.GetInt64(11),
            SandboxIoReadBytes = reader.GetInt64(12),
            SandboxIoWriteBytes = reader.GetInt64(13),
            GatewayIngressBytes = reader.GetInt64(14),
            GatewayEgressBytes = reader.GetInt64(15),
            StorageBytes = reader.GetInt64(16),
            ConcurrencyUnits = reader.GetInt32(17),
            DurationMilliseconds = reader.GetInt64(18),
            RecordedAtUtc = DateTimeOffset.Parse(reader.GetString(19), null,
                System.Globalization.DateTimeStyles.RoundtripKind)
        };
        return new TenantMeteringRecord(reader.GetInt64(0), reader.GetString(1), usage);
    }

    private static void RequireRuntimeTenant(TenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenant.Origin is not (TenantContextOrigin.HostFixed or TenantContextOrigin.VerifiedCredential))
            throw new UnauthorizedAccessException(
                "Runtime metering requires host-fixed or verified-credential tenant authority.");
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
