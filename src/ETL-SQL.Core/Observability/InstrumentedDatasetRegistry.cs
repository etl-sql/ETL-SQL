using System.Diagnostics;
using ETL_SQL.Core.Data;

namespace ETL_SQL.Core.Observability;

internal sealed class InstrumentedDatasetRegistry(IDatasetRegistry inner) : IDatasetRegistry
{
    public Task<int> RegisterOrUpdate(DatasetMetadata metadata) =>
        ObserveAsync("register_or_update", async () =>
        {
            var id = await inner.RegisterOrUpdate(metadata).ConfigureAwait(false);
            return (id, (int?)id);
        });

    public Task<DatasetMetadata?> Lookup(string name, string callerPermissions = "") =>
        ObserveAsync("lookup", async () =>
        {
            var result = await inner.Lookup(name, callerPermissions).ConfigureAwait(false);
            return (result, result?.Id);
        });

    public Task<bool> Exists(string name) =>
        ObserveAsync("exists", async () =>
        {
            var result = await inner.Exists(name).ConfigureAwait(false);
            return (result, (int?)null);
        });

    public Task<bool> CanEditAsync(string name, string callerPermissions) =>
        ObserveAsync("can_edit", async () =>
        {
            var result = await inner.CanEditAsync(name, callerPermissions).ConfigureAwait(false);
            return (result, (int?)null);
        });

    public Task<bool> CanRefreshAsync(string name, string callerPermissions) =>
        ObserveAsync("can_refresh", async () =>
        {
            var result = await inner.CanRefreshAsync(name, callerPermissions).ConfigureAwait(false);
            return (result, (int?)null);
        });

    public Task SetStale(string name) =>
        ObserveAsync("set_stale", async () =>
        {
            await inner.SetStale(name).ConfigureAwait(false);
            return (true, (int?)null);
        });

    public Task<IEnumerable<DatasetMetadata>> ListAll(string callerPermissions) =>
        ObserveAsync("list", async () =>
        {
            var result = await inner.ListAll(callerPermissions).ConfigureAwait(false);
            return (result, (int?)null);
        });

    public Task Delete(string name) =>
        ObserveAsync("delete", async () =>
        {
            await inner.Delete(name).ConfigureAwait(false);
            return (true, (int?)null);
        });

    public Task RegisterRefreshJobAsync(int reportId, string orchestratorJobName, string refreshInterval) =>
        ObserveAsync("register_refresh_job", async () =>
        {
            await inner.RegisterRefreshJobAsync(reportId, orchestratorJobName, refreshInterval).ConfigureAwait(false);
            return (true, (int?)null);
        });

    public Task<DatasetPublishTarget?> AuthorizePublishAsync(string targetFolderPath, string callerPermissions) =>
        ObserveAsync("authorize_publish", async () =>
        {
            var result = await inner.AuthorizePublishAsync(targetFolderPath, callerPermissions).ConfigureAwait(false);
            return (result, (int?)null);
        });

    public Task AuditPublishAsync(int? userId, string datasetName, string targetFolderPath, bool succeeded,
        string? failureReason = null) =>
        ObserveAsync("audit_publish", async () =>
        {
            await inner.AuditPublishAsync(userId, datasetName, targetFolderPath, succeeded, failureReason)
                .ConfigureAwait(false);
            return (true, (int?)null);
        });

    public string BuildDatasetFilePath(int datasetId, string name)
    {
        var sw = Stopwatch.StartNew();
        using var activity = DatasetObservability.StartOperation("build_path");
        try
        {
            var result = inner.BuildDatasetFilePath(datasetId, name);
            sw.Stop();
            DatasetObservability.CompleteOperation(activity, "build_path", "success", sw.ElapsedMilliseconds, datasetId);
            return result;
        }
        catch
        {
            sw.Stop();
            DatasetObservability.CompleteOperation(activity, "build_path", "failure", sw.ElapsedMilliseconds, datasetId);
            throw;
        }
    }

    private static async Task<T> ObserveAsync<T>(string operation, Func<Task<(T Result, int? DatasetId)>> action)
    {
        var sw = Stopwatch.StartNew();
        using var activity = DatasetObservability.StartOperation(operation);
        try
        {
            var (result, datasetId) = await action().ConfigureAwait(false);
            sw.Stop();
            DatasetObservability.CompleteOperation(activity, operation, "success", sw.ElapsedMilliseconds, datasetId);
            return result;
        }
        catch
        {
            sw.Stop();
            DatasetObservability.CompleteOperation(activity, operation, "failure", sw.ElapsedMilliseconds);
            throw;
        }
    }
}
