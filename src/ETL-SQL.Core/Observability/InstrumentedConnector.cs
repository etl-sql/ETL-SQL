using System.Diagnostics;
using ETL_SQL.Core;
using ETL_SQL.Core.Diagnostics;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Observability;

internal sealed class InstrumentedConnector(IConnector inner) : IConnector, IConnectionDiagnosticAuthProbe
{
    public string Name => inner.Name;
    public IReadOnlyList<string> Aliases => inner.Aliases;
    public bool IsFileBased => inner.IsFileBased;
    public int CommandTimeoutSeconds => inner.CommandTimeoutSeconds;
    public bool IsDataWarehouse => inner.IsDataWarehouse;

    public HashSet<string> GetSupportedFunctions() => inner.GetSupportedFunctions();
    public HashSet<string> GetSupportedKeywords() => inner.GetSupportedKeywords();
    public HashSet<string> GetExcludedKeywords() => inner.GetExcludedKeywords();
    public Dictionary<string, string[]> GetSupportedOptions() => inner.GetSupportedOptions();
    public Dictionary<string, string[]> GetOptionValues() => inner.GetOptionValues();
    public string GetHelp() => inner.GetHelp();
    public IReadOnlyList<ConnectorOptionDescriptor> GetOptionDescriptors() => inner.GetOptionDescriptors();
    public ConnectorSchemaDescriptor GetSchemaDescriptor() => inner.GetSchemaDescriptor();
    public string BuildConnectionString(Dictionary<string, string> properties) => inner.BuildConnectionString(properties);
    public string? GetHost(string connectionString, Dictionary<string, string>? options = null) =>
        inner.GetHost(connectionString, options);
    public ICatalogMetadataProvider? GetCatalogProvider(string connectionString) =>
        inner.GetCatalogProvider(connectionString);

    public Task<string> GetVersionAsync(IExecutionContext context, string connectionString) =>
        ObserveAsync("version", () => inner.GetVersionAsync(context, connectionString));

    public IDataSource CreateDataSource(
        IExecutionContext context,
        string connectionString,
        Dictionary<string, string>? options = null) =>
        Observe("create_data_source", () => inner.CreateDataSource(context, connectionString, options));

    public IDataSource CreateDataSource(
        IExecutionContext context,
        string connectionString,
        Dictionary<string, string>? options,
        IEnumerable<ColumnDefinition>? templateSchema) =>
        Observe("create_data_source", () => inner.CreateDataSource(context, connectionString, options, templateSchema));

    public Task<IEnumerable<string>> GetTablesAsync(IExecutionContext context, string connectionString) =>
        ObserveAsync("tables", () => inner.GetTablesAsync(context, connectionString));

    public Task<IEnumerable<string>> GetViewsAsync(IExecutionContext context, string connectionString) =>
        ObserveAsync("views", () => inner.GetViewsAsync(context, connectionString));

    public Task<IEnumerable<string>> GetColumnsAsync(
        IExecutionContext context,
        string connectionString,
        string tableName) =>
        ObserveAsync("columns", () => inner.GetColumnsAsync(context, connectionString, tableName));

    public Task<IEnumerable<string>> GetProceduresAsync(IExecutionContext context, string connectionString) =>
        ObserveAsync("procedures", () => inner.GetProceduresAsync(context, connectionString));

    public Task<IReadOnlyList<DiagnosticStep>> DiagnoseAuthenticationAsync(
        ConnectionDiagnosticAuthContext context,
        CancellationToken cancellationToken = default)
    {
        if (inner is not IConnectionDiagnosticAuthProbe probe)
        {
            return Task.FromResult<IReadOnlyList<DiagnosticStep>>(
            [
                new DiagnosticStep("AUTH", DiagnosticStatus.Skipped,
                    "Credential authentication is not supported by this connector diagnostic.",
                    "Run a query or connector-specific operation to confirm the credentials are accepted.")
            ]);
        }

        return ObserveAsync("diagnostic_auth", () => probe.DiagnoseAuthenticationAsync(context, cancellationToken));
    }

    private T Observe<T>(string operation, Func<T> action)
    {
        var sw = Stopwatch.StartNew();
        using var activity = ConnectorObservability.StartOperation(Name, operation);
        try
        {
            var result = action();
            sw.Stop();
            ConnectorObservability.CompleteOperation(activity, Name, operation, "success", sw.ElapsedMilliseconds);
            return result;
        }
        catch
        {
            sw.Stop();
            ConnectorObservability.CompleteOperation(activity, Name, operation, "failure", sw.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<T> ObserveAsync<T>(string operation, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        using var activity = ConnectorObservability.StartOperation(Name, operation);
        try
        {
            var result = await action().ConfigureAwait(false);
            sw.Stop();
            ConnectorObservability.CompleteOperation(activity, Name, operation, "success", sw.ElapsedMilliseconds);
            return result;
        }
        catch
        {
            sw.Stop();
            ConnectorObservability.CompleteOperation(activity, Name, operation, "failure", sw.ElapsedMilliseconds);
            throw;
        }
    }
}
