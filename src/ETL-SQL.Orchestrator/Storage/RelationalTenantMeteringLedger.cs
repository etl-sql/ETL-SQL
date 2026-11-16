using System.Data.Common;
using ETL_SQL.Core.Multitenancy;

namespace ETL_SQL.Orchestrator.Storage;

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
            INSERT INTO tenant_metering_ledger (
                tenant_id, source_event_id, source, workload_class, connector_class, status,
                rows, bytes_read, bytes_written, sandbox_cpu_milliseconds, sandbox_peak_memory_bytes,
                sandbox_io_read_bytes, sandbox_io_write_bytes, gateway_ingress_bytes, gateway_egress_bytes,
                storage_bytes, concurrency_units, duration_milliseconds, recorded_at_utc)
            VALUES (
                @tenant, @event, @source, @workload, @connector, @status,
                @rows, @bytesRead, @bytesWritten, @cpu, @memory, @ioRead, @ioWrite,
                @gatewayIn, @gatewayOut, @storage, @concurrency, @duration, @recorded)
            ON CONFLICT (tenant_id, source, source_event_id) DO NOTHING;";
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
            SELECT id, tenant_id, source_event_id, source, workload_class, connector_class, status,
                   rows, bytes_read, bytes_written, sandbox_cpu_milliseconds, sandbox_peak_memory_bytes,
                   sandbox_io_read_bytes, sandbox_io_write_bytes, gateway_ingress_bytes, gateway_egress_bytes,
                   storage_bytes, concurrency_units, duration_milliseconds, recorded_at_utc
              FROM tenant_metering_ledger
             WHERE tenant_id = @tenant" +
            (fromUtc is null ? string.Empty : " AND recorded_at_utc >= @from") + @"
             ORDER BY recorded_at_utc DESC, id DESC
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
                CREATE TABLE IF NOT EXISTS tenant_metering_ledger (
                    id {dialect.AutoIncrementPrimaryKey},
                    tenant_id TEXT NOT NULL,
                    source_event_id TEXT NOT NULL,
                    source TEXT NOT NULL,
                    workload_class TEXT NOT NULL,
                    connector_class TEXT NOT NULL,
                    status TEXT NOT NULL,
                    rows {dialect.Int64Type} NOT NULL,
                    bytes_read {dialect.Int64Type} NOT NULL,
                    bytes_written {dialect.Int64Type} NOT NULL,
                    sandbox_cpu_milliseconds {dialect.Int64Type} NOT NULL,
                    sandbox_peak_memory_bytes {dialect.Int64Type} NOT NULL,
                    sandbox_io_read_bytes {dialect.Int64Type} NOT NULL,
                    sandbox_io_write_bytes {dialect.Int64Type} NOT NULL,
                    gateway_ingress_bytes {dialect.Int64Type} NOT NULL,
                    gateway_egress_bytes {dialect.Int64Type} NOT NULL,
                    storage_bytes {dialect.Int64Type} NOT NULL,
                    concurrency_units INTEGER NOT NULL,
                    duration_milliseconds {dialect.Int64Type} NOT NULL,
                    recorded_at_utc TEXT NOT NULL,
                    UNIQUE (tenant_id, source, source_event_id)
                );
                CREATE INDEX IF NOT EXISTS idx_tenant_metering_time
                    ON tenant_metering_ledger (tenant_id, recorded_at_utc DESC);";
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
