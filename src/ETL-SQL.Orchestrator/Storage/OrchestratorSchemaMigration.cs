using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Common;

namespace ETL_SQL.Orchestrator.Storage;

public static class OrchestratorSchemaMigration
{
    // Frozen legacy spellings: PostgreSQL folded unquoted PascalCase identifiers, losing the
    // word boundaries. Do not infer those boundaries from a live lowercase column name.
    private static readonly Dictionary<string, string> LegacyColumns = """
        AcquiredAt Action ActiveCount ActiveRelease AdmissionId AtTime Authority AuthorizationReference
        AvgCpuPercent AvgHostCpuPercent AvgMemoryLoadPercent BundleName BytesRead BytesWritten CapacityPool
        CapturedAt ClaimedByNode ClaimHeartbeatUtc ColumnName CompletedAtUtc ConcurrencyUnits ConnectionName
        ConnectorClass Content ContentHash ContentType CpuTimeMs CpuTimeSeconds CreatedAtUtc CreatedBy Cron
        Day DeletedAtUtc DerivedFromDescriptions Description DisplayName DqRowsQuarantined DqRowsValidated
        DqRowsWarned DqValidationMs DurationMilliseconds DurationMs EncryptionMetadata EncryptionMode EndTime
        EnqueuedUtc EntryPath Epoch EpochKey ErrorMessage ExpiresAt Failed FailureCount FenceEpoch FenceToken
        FirstSeenAt FromPath FunctionsApplied GatewayEgressBytes GatewayIngressBytes GrantedBy HostCpuPercent
        Id IndexUsed Interval IsEnabled JobHistoryId JobId JobName JobType Kind LastHeartbeatAt LastRun
        LeaseExpiresUtc LeaseFenceToken LeaseOwner Line LockName LockWaitMs MachineName MaxConcurrentAttempts
        MaxConcurrentJobs MaxCpuPercent MaxHostCpuPercent MaxMemoryLoadPercent MaxPeakMemoryBytes
        MaxQueuedAttempts MaxReportSessions MaxRetries MaxStorageMb MaxTimestampUtc MemoryLoadPercent Metadata
        MinSpillDiskFreeBytes MinStateDiskFreeBytes ModifiedBy Name NextRun NodeId NodeTenantId NotificationId
        NullRows ObjectId ObjectKind Operation OperationId Options Ordinal Owner Partitions PeakMemoryBytes
        Permission PlatformOperator PoolId PrincipalId PrincipalKind ProcessCpuPercent ProcessId PublishedAt
        Publisher QueueWaitMs Recipient ReconciliationReason RecordedAtUtc RetryDelaySeconds Role Rows
        RowsProcessed RuleText RunAt RunCount SandboxCpuMilliseconds SandboxIoReadBytes SandboxIoWriteBytes
        SandboxPeakMemoryBytes ScheduleId Scope Script ScriptPath Sequence SizeBytes Source SourceActiveExecutions
        SourceColumns SourceEventId SourceFile SourceSchedulesEnabled SourceTables SpillDiskFreeBytes SpilledBytes
        SpillReadBytes StartedAtUtc StartTime State StateDiskFreeBytes StateKey Statement StateValue Status
        StorageBytes Tags TargetColumn TargetMaxConcurrentJobs TargetMaxReportSessions TargetMaxStorageMb
        TargetPath TargetRelease TargetSchedulesEnabled TargetTable TenantId TenantWeight TimeZone ToPath
        TotalRows TransformationExpression TransformationKind TriggerCondition Unit UpdatedAt UpdatedAtUtc
        UpdatedUtc Version VirtualBase VirtualPath VirtualTime WorkloadClass WorkloadKind
        ScriptHash HashPolicy LeaseExpiresAt ScriptHashAtRunTime HashMatched RowsQuarantined RowsWarned
        DataQualityFailures SessionId CheckpointLabel
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .ToDictionary(name => name, DatabaseNamingConvention.ToSnakeCase, StringComparer.OrdinalIgnoreCase);

    public static async Task MigrateLegacyTableAsync(
        IOrchestratorStoreDialect dialect,
        DbConnection connection,
        string oldTable,
        string newTable,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var tables = await dialect.GetTableNamesAsync(connection, ct);
        var temporaryTable = $"_tmp_snake_{newTable}";
        if (tables.Contains(temporaryTable))
            throw new InvalidOperationException($"Cannot migrate '{oldTable}': an interrupted migration table exists. Reconcile '{temporaryTable}' before restarting.");
        var candidates = new[] { oldTable, oldTable.ToLowerInvariant(), newTable }
            .Distinct(StringComparer.Ordinal).Where(tables.Contains).ToArray();
        if (candidates.Length == 0) return;
        if (candidates.Length != 1)
            throw new InvalidOperationException($"Cannot migrate '{oldTable}': legacy and canonical tables coexist. Reconcile their data before restarting.");

        var sourceTable = candidates[0];
        var columns = await dialect.GetExactColumnNamesAsync(connection, sourceTable, ct);
        var renames = columns.Select(column => (Source: column, Target:
            LegacyColumns.TryGetValue(column, out var canonical) ? canonical : DatabaseNamingConvention.ToSnakeCase(column)))
            .ToArray();
        if (renames.Select(rename => rename.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
            throw new InvalidOperationException($"Cannot migrate '{sourceTable}': column names collide after conversion to snake_case.");

        if (sourceTable == newTable && renames.All(rename => rename.Source == rename.Target)) return;

        // Both providers support transactional DDL. A failed rename must preserve the original
        // table and rows, and must stop bootstrap before it can create an empty replacement.
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (sourceTable != newTable)
        {
            await ExecuteAsync($"ALTER TABLE {Quote(sourceTable)} RENAME TO {Quote(temporaryTable)};");
            await ExecuteAsync($"ALTER TABLE {Quote(temporaryTable)} RENAME TO {Quote(newTable)};");
        }
        foreach (var (source, target) in renames)
        {
            if (source != target)
                await ExecuteAsync($"ALTER TABLE {Quote(newTable)} RENAME COLUMN {Quote(source)} TO {Quote(target)};");
        }
        await transaction.CommitAsync(ct);

        async Task ExecuteAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
