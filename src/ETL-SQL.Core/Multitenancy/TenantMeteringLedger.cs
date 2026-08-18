using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Multitenancy;

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

    public void Validate()
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
