using System.Runtime.CompilerServices;
using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Connectors.Postgres;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Gateway;

namespace ETL_SQL.App;

internal static class GatewayRuntimeService
{
    public static async Task<int> RunAsync(IConnectorRegistry connectors, ILogger logger)
    {
        if (!File.Exists(GatewaySetupService.ConfigPath))
        {
            logger.Error("Gateway is not enrolled. Run 'etlsql gateway setup' first.");
            return 1;
        }

        GatewayConfig config;
        try
        {
            config = JsonSerializer.Deserialize<GatewayConfig>(
                await File.ReadAllTextAsync(GatewaySetupService.ConfigPath).ConfigureAwait(false))
                ?? throw new InvalidDataException();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            logger.Error("The Gateway configuration could not be loaded.");
            return 1;
        }

        var entropy = $"gateway:{config.TenantId}:{config.GatewayId}";
        string privateKey;
        try { privateKey = CryptoUtils.Unprotect(config.ProtectedWorkloadPrivateKeyPkcs8, entropy); }
        catch (Exception ex)
        {
            logger.Error("The protected Gateway workload identity could not be opened on this machine.", ex);
            return 1;
        }

        var registry = new GatewayResourceRegistry(Path.Combine(GatewaySetupService.ConfigDirectory, "resources.protected"));
        var ledger = new GatewayOutcomeLedger(Path.Combine(GatewaySetupService.ConfigDirectory, "outcomes.json"));
        var dispatcher = new GatewayOperationDispatcher(
            registry, new ConnectorResourceExecutor(connectors), ledger, CreateViewerContextVerifier(logger));
        var publishedResources = await registry.PublishAsync().ConfigureAwait(false);
        var host = new GatewayHost(new GatewayHostOptions(new GatewaySessionOptions(
            new Uri(config.BrokerUrl), config.TenantId, config.GatewayId,
            config.WorkloadPublicKeyThumbprint, config.NodeId,
            WorkloadPrivateKeyPkcs8Base64: privateKey,
            PublishedResources: publishedResources)), dispatcher);

        host.StatusChanged += status => logger.Info("Gateway status: {Status}", status);
        host.ErrorOccurred += ex => logger.Error("Gateway broker connection failed; reconnecting.", ex);
        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; shutdown.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            logger.Info("Gateway {GatewayId} is starting on node {NodeId}.", config.GatewayId, config.NodeId);
            await host.RunAsync(shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static IViewerContextEnvelopeVerifier? CreateViewerContextVerifier(ILogger logger)
    {
        var encodedKey = Environment.GetEnvironmentVariable("ETLSQL_VIEWER_CONTEXT_HMAC_KEY");
        if (string.IsNullOrWhiteSpace(encodedKey)) return null;
        try
        {
            var keyId = Environment.GetEnvironmentVariable("ETLSQL_VIEWER_CONTEXT_KEY_ID") ?? "portal-gateway-v1";
            return new HmacViewerContextEnvelopeService(
                keyId, Convert.FromBase64String(encodedKey), new ViewerContextReplayStore(
                    Path.Combine(GatewaySetupService.ConfigDirectory, "viewer-context-replay.json")));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            logger.Error("The Gateway viewer context verification key is malformed.");
            return null;
        }
    }

    private sealed record ConnectorRequest(
        string Table,
        IReadOnlyList<string>? Columns = null,
        IReadOnlyList<IReadOnlyList<string?>>? Rows = null,
        bool Append = false);

    private sealed class ConnectorResourceExecutor(IConnectorRegistry connectors) : IGatewayResourceExecutor
    {
        private readonly Dictionary<string, SemaphoreSlim> _limits = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _gate = new();

        public async Task<GatewayExecutionResult> ExecuteAsync(
            GatewayResource resource, GatewayOperationClass operationClass,
            string? request, IReadOnlyList<string>? parameters,
            GatewayOperationBounds bounds, CancellationToken cancellationToken)
            => await ExecuteAsync(resource, operationClass, request, parameters, null, bounds, cancellationToken)
                .ConfigureAwait(false);

        public async Task<GatewayExecutionResult> ExecuteAsync(
            GatewayResource resource, GatewayOperationClass operationClass,
            string? request, IReadOnlyList<string>? parameters,
            VerifiedViewerContext? viewerContext,
            GatewayOperationBounds bounds, CancellationToken cancellationToken)
        {
            var operation = ParseRequest(request);
            var connector = connectors.GetConnector(resource.ConnectorType)
                ?? throw new InvalidOperationException("The registered connector type is unavailable.");
            var secret = ResolveCredential(resource.LocalCredentialReference);
            var target = resource.LocalTarget.Replace("${CREDENTIAL}", secret, StringComparison.Ordinal);
            var limiter = Limiter(resource);
            await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var source = connector.CreateDataSource(SystemExecutionContext.Instance, target).WithTable(operation.Table);
                PostgresDataSource? contextSource = null;
                if (viewerContext is not null)
                {
                    contextSource = source as PostgresDataSource
                        ?? throw new InvalidOperationException("Verified viewer context is supported only by the PostgreSQL connector.");
                    await contextSource.BeginVerifiedViewerContextAsync(viewerContext, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (operationClass == GatewayOperationClass.Execute)
                    throw new NotSupportedException("The local connector does not expose a bounded execute operation.");
                if (operationClass == GatewayOperationClass.Write)
                {
                    if (operation.Rows is null) throw new InvalidOperationException("A write requires rows.");
                    var columns = operation.Columns ?? throw new InvalidOperationException("A write requires columns.");
                    await source.WriteBatches(ToBatches(columns, operation.Rows, cancellationToken), operation.Append, cancellationToken)
                        .ConfigureAwait(false);
                    if (contextSource is not null) await contextSource.CommitAsync().ConfigureAwait(false);
                    return new GatewayExecutionResult(columns, [], false);
                }

                if (operationClass != GatewayOperationClass.Read)
                    throw new InvalidOperationException("The typed operation class is invalid.");
                var resultColumns = new List<string>();
                var rows = new List<IReadOnlyList<string?>>();
                long approximateBytes = 0;
                var truncated = false;
                await foreach (var batch in source.ReadBatches((int)Math.Min(bounds.MaxRows, 10_000), cancellationToken))
                {
                    if (resultColumns.Count == 0) resultColumns.AddRange(batch.ColumnNames);
                    foreach (var row in batch.Rows)
                    {
                        var values = resultColumns.Select(column => Convert.ToString(row[column])).ToArray();
                        approximateBytes += values.Sum(value => value?.Length * sizeof(char) ?? 0);
                        if (rows.Count >= bounds.MaxRows || approximateBytes > bounds.MaxResponseBytes) { truncated = true; break; }
                        rows.Add(values);
                    }
                    if (truncated) break;
                }
                if (contextSource is not null) await contextSource.CommitAsync().ConfigureAwait(false);
                return new GatewayExecutionResult(resultColumns, rows, truncated);
            }
            finally { limiter.Release(); }
        }

        private SemaphoreSlim Limiter(GatewayResource resource)
        {
            lock (_gate)
            {
                if (!_limits.TryGetValue(resource.ResourceId, out var limiter))
                    _limits[resource.ResourceId] = limiter = new SemaphoreSlim(resource.Limits.MaxConcurrency);
                return limiter;
            }
        }

        private static ConnectorRequest ParseRequest(string? request)
        {
            if (string.IsNullOrWhiteSpace(request)) throw new InvalidOperationException("A typed connector request is required.");
            var parsed = JsonSerializer.Deserialize<ConnectorRequest>(request);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Table)
                || !parsed.Table.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                throw new InvalidOperationException("The typed connector request is invalid.");
            return parsed;
        }

        private static string ResolveCredential(string reference)
        {
            if (!reference.StartsWith("ENV:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Gateway credential references must use ENV:name.");
            var name = reference[4..];
            if (string.IsNullOrWhiteSpace(name) || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
                throw new InvalidOperationException("The Gateway credential reference is invalid.");
            return Environment.GetEnvironmentVariable(name)
                ?? throw new InvalidOperationException("The Gateway credential is unavailable.");
        }

        private static async IAsyncEnumerable<DataTable> ToBatches(
            IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string?>> rows,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var table = new DataTable();
            table.SetColumns(columns);
            foreach (var values in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (values.Count != columns.Count) throw new InvalidOperationException("A write row does not match its columns.");
                await table.AddRowAsync(new Row(table.Schema, values.Cast<object?>().ToArray())).ConfigureAwait(false);
            }
            yield return table;
        }
    }
}
