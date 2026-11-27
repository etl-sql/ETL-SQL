using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Portal.Models;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Synchronizes Portal-managed <c>SHARED:</c> connection definitions and <c>SECRET:</c> store entries
/// to connected remote Orchestrator workers so scheduled/background jobs resolve the same governed
/// credentials without sharing a local SQLite database or filesystem.
/// </summary>
public sealed class OrchestratorGovernanceSyncService(
    PortalConnectionCatalogService catalog,
    PortalSecretStoreService secretStore,
    OrchestratorProxyService proxy,
    ILogger<OrchestratorGovernanceSyncService> logger)
{
    public bool IsRemoteOrchestratorConfigured => proxy.IsConfigured;

    public async Task TrySyncConnectionAsync(string alias, CancellationToken ct = default)
    {
        if (!proxy.IsConfigured) return;
        try
        {
            var entries = await catalog.ExportForOrchestratorSyncAsync(alias, ct);
            if (entries.Count == 0) return;
            if (!await proxy.ImportRemoteConnectionsAsync(entries, ct))
            {
                logger.LogWarning("Failed to synchronize SHARED:{Alias} to remote Orchestrator worker.",
                    LogSanitizer.Clean(alias));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Error synchronizing SHARED:{Alias} to remote Orchestrator worker ({FailureType}).",
                LogSanitizer.Clean(alias), ex.GetType().Name);
        }
    }

    public async Task TrySyncConnectionStateAsync(string alias, bool enabled, CancellationToken ct = default)
    {
        if (!proxy.IsConfigured) return;
        try
        {
            if (!await proxy.SetRemoteConnectionEnabledAsync(alias, enabled, ct))
            {
                logger.LogWarning("Failed to synchronize SHARED:{Alias} state ({Enabled}) to remote Orchestrator.",
                    LogSanitizer.Clean(alias), enabled);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Error synchronizing SHARED:{Alias} state to remote Orchestrator ({FailureType}).",
                LogSanitizer.Clean(alias), ex.GetType().Name);
        }
    }

    public async Task TryDeleteConnectionAsync(string alias, CancellationToken ct = default)
    {
        if (!proxy.IsConfigured) return;
        try
        {
            if (!await proxy.DeleteRemoteConnectionAsync(alias, ct))
            {
                logger.LogWarning("Failed to delete SHARED:{Alias} from remote Orchestrator.",
                    LogSanitizer.Clean(alias));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Error deleting SHARED:{Alias} from remote Orchestrator ({FailureType}).",
                LogSanitizer.Clean(alias), ex.GetType().Name);
        }
    }

    public async Task TrySyncConnectionsBatchAsync(IReadOnlyCollection<string> aliases, CancellationToken ct = default)
    {
        if (!proxy.IsConfigured || aliases.Count == 0) return;
        try
        {
            var all = await catalog.ExportForOrchestratorSyncAsync(cancellationToken: ct);
            var lookup = new HashSet<string>(aliases, StringComparer.OrdinalIgnoreCase);
            var selected = all.Where(entry => lookup.Contains(entry.Alias)).ToList();
            if (selected.Count == 0) return;
            if (!await proxy.ImportRemoteConnectionsAsync(selected, ct))
            {
                logger.LogWarning("Failed to synchronize {Count} imported SHARED: connection(s) to remote Orchestrator.",
                    selected.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Error synchronizing imported connections to remote Orchestrator ({FailureType}).",
                ex.GetType().Name);
        }
    }

    public async Task TrySyncSecretAsync(string name, string value, CancellationToken ct = default)
    {
        if (!proxy.IsConfigured) return;
        try
        {
            if (!await proxy.UpsertRemoteSecretAsync(name, value, ct))
            {
                logger.LogWarning("Failed to synchronize SECRET:{Name} to remote Orchestrator worker.",
                    LogSanitizer.Clean(name));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Error synchronizing SECRET:{Name} to remote Orchestrator ({FailureType}).",
                LogSanitizer.Clean(name), ex.GetType().Name);
        }
    }

    public async Task TrySyncSecretStateAsync(string name, bool enabled, CancellationToken ct = default)
    {
        if (!proxy.IsConfigured) return;
        try
        {
            if (!await proxy.SetRemoteSecretEnabledAsync(name, enabled, ct))
            {
                logger.LogWarning("Failed to synchronize SECRET:{Name} state ({Enabled}) to remote Orchestrator.",
                    LogSanitizer.Clean(name), enabled);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Error synchronizing SECRET:{Name} state to remote Orchestrator ({FailureType}).",
                LogSanitizer.Clean(name), ex.GetType().Name);
        }
    }

    public async Task TryDeleteSecretAsync(string name, CancellationToken ct = default)
    {
        if (!proxy.IsConfigured) return;
        try
        {
            if (!await proxy.DeleteRemoteSecretAsync(name, ct))
            {
                logger.LogWarning("Failed to delete SECRET:{Name} from remote Orchestrator.",
                    LogSanitizer.Clean(name));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Error deleting SECRET:{Name} from remote Orchestrator ({FailureType}).",
                LogSanitizer.Clean(name), ex.GetType().Name);
        }
    }

    public async Task<GovernanceSyncSummaryDto?> SyncAllAsync(CancellationToken ct = default)
    {
        if (!proxy.IsConfigured) return null;

        var warnings = new List<string>();
        var secretsSynced = 0;
        var secretSummaries = await secretStore.ListAsync(ct);
        foreach (var summary in secretSummaries)
        {
            if (summary.Disabled)
            {
                if (await proxy.SetRemoteSecretEnabledAsync(summary.Name, enabled: false, ct))
                    secretsSynced++;
                else
                    warnings.Add($"Secret '{summary.Name}' could not be disabled on Orchestrator.");
                continue;
            }

            try
            {
                var value = await secretStore.ResolveAsync(summary.Name, ct);
                if (await proxy.UpsertRemoteSecretAsync(summary.Name, value, ct))
                    secretsSynced++;
                else
                    warnings.Add($"Secret '{summary.Name}' could not be pushed to Orchestrator.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add($"Secret '{summary.Name}' could not be resolved ({ex.GetType().Name}).");
            }
        }

        var connections = await catalog.ExportForOrchestratorSyncAsync(cancellationToken: ct);
        var connectionsSynced = 0;
        if (connections.Count > 0)
        {
            if (await proxy.ImportRemoteConnectionsAsync(connections, ct))
                connectionsSynced = connections.Count;
            else
                warnings.Add($"Batch connection import ({connections.Count} entries) did not succeed on Orchestrator.");
        }

        return new GovernanceSyncSummaryDto(secretsSynced, connectionsSynced, warnings);
    }
}
