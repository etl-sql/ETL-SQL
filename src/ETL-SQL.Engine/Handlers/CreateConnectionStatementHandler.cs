using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles the CREATE CONNECTION statement, registering new data sources in the execution context.
/// Supports various connector types (SQL, File, specialized) and connection string interpolation.
/// </summary>
public class CreateConnectionStatementHandler(
    IConnectorRegistry connectorRegistry,
    ILogger logger,
    IConfiguration? config = null,
    ISecretProvider? secretProvider = null,
    IConnectionCatalogProvider? connectionCatalog = null) : IStatementHandler
{
    private readonly IConnectorRegistry _connectorRegistry = connectorRegistry;
    private readonly ILogger _logger = logger;
    private readonly IConfiguration? _config = config;
    private readonly ConnectionSecretResolver _secretResolver = new(secretProvider);
    private readonly SharedConnectionExpander _sharedExpander = new(connectionCatalog);


    public Type SupportedStatementType => typeof(CreateConnectionStatement);
    /// <summary>Executes the CREATE CONNECTION statement, resolving the target string and options.</summary>
    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateConnectionStatement)statement;

        bool alreadyExists = context.Connections.TryGetValue(stmt.ConnectionName, out var existingDataSource);
        bool isInteractive = context.InteractiveMode;

        if (!alreadyExists && !context.IsWhatIf)
            LiveObjectLimits.EnsureConnectionCapacity(context);

        if (stmt.Mode == ObjectCreationMode.Create && alreadyExists && !isInteractive)
            throw new ExecutionException($"Connection '{stmt.ConnectionName}' already exists. Use ALTER CONNECTION to modify it.");

        // In Interactive Mode, force CreateOrAlter behavior if it already exists
        var effectiveMode = (isInteractive && alreadyExists) ? ObjectCreationMode.CreateOrAlter : stmt.Mode;

        string? connectionType = stmt.ConnectionType;
        string? target = null;
        Dictionary<string, string>? options = null;

        if (effectiveMode == ObjectCreationMode.CreateOrAlter && alreadyExists)
        {
            // CREATE OR ALTER with existing connection — patches and preserves options
            if (existingDataSource == null) throw new ExecutionException($"Connection '{stmt.ConnectionName}' exists but its data source is null.");
            connectionType ??= existingDataSource.ConnectorType;
            options = new Dictionary<string, string>(existingDataSource.Options ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            target = existingDataSource.Path;

            if (stmt.Options != null)
            {
                foreach (var kvp in stmt.Options)
                {
                    var val = StringifyOption(await context.EvaluateValue(kvp.Value, new Row(), decryptSensitive: true), kvp.Value);
                    options[kvp.Key] = val;
                }
            }

            if (stmt.TargetExpression != null)
                target = (await context.EvaluateValue(stmt.TargetExpression, new Row(), decryptSensitive: true))?.ToString() ?? "";
        }
        else
        {
            // CREATE (new) or CREATE OR ALTER (not yet existing)
            if (connectionType == null) throw new ExecutionException("Connection type must be specified for CREATE CONNECTION.");
            target = stmt.TargetExpression != null
                ? (await context.EvaluateValue(stmt.TargetExpression, new Row(), decryptSensitive: true))?.ToString() ?? ""
                : "";

            options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (stmt.Options != null)
            {
                foreach (var kvp in stmt.Options)
                {
                    var val = StringifyOption(await context.EvaluateValue(kvp.Value, new Row(), decryptSensitive: true), kvp.Value);
                    options[kvp.Key] = Interpolate(val);
                }
            }
        }

        _logger.Debug("{Action} connection {ConnectionName} of type {ConnectionType}", alreadyExists ? "Upserting" : "Creating", stmt.ConnectionName, connectionType);

        // Decrypt target if necessary (already handled by EvaluateValue if it was a variable, 
        // but for literals or complex expressions we call DecryptValue directly)
        if (target != null && target.StartsWith("ENC:"))
        {
            target = context.DecryptValue(target);
        }
        target = Interpolate(target ?? "");
        IReadOnlyCollection<string>? entrySensitiveFields = null;
        if (SharedConnectionExpander.IsSharedReference(target))
        {
            var expanded = await _sharedExpander.ExpandAsync(
                connectionType, target, options, context.ExecutionIdentity, context.CancellationToken);
            target = expanded.Target;
            options = expanded.Options;
            entrySensitiveFields = expanded.SensitiveFields;
        }
        target = await _secretResolver.ResolveTargetAsync(
            target, context.CancellationToken, connectionType, entrySensitiveFields);
        if (options != null)
            options = await _secretResolver.ResolveOptionsAsync(
                options, context.CancellationToken, connectionType, entrySensitiveFields);

        var connector = _connectorRegistry.GetConnector(connectionType ?? string.Empty);
        if (connector == null)
        {
            throw new ExecutionException($"Connection type '{connectionType}' is not registered or implemented.");
        }

        // Security Hardening: Validate path for file-based connectors
        if (connector.IsFileBased)
        {
            target = context.ResolvePath(target);
            // Empty targets are built from options below; ${placeholder} targets defer
            // resolution to use time — both are authorized on the fully resolved path later.
            if (!string.IsNullOrWhiteSpace(target) && !target.Contains("${"))
            {
                target = new FileSystemPolicyAuthorizer(context.SecurityService)
                    .Authorize(context, target, FileSystemAccessKind.Enumerate, validateFileType: false)
                    .CanonicalPath;
            }
        }

        IDataSource ds;
        if (!Path.IsPathRooted(target) && (target.Contains("Demo", StringComparison.OrdinalIgnoreCase) || target.Contains("Sample", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mock:", StringComparison.OrdinalIgnoreCase)))
        {
            var mock = _connectorRegistry.GetConnector("MOCKDB");
            if (mock != null) connector = mock;
        }

        if (connector != null)
        {
            if (string.IsNullOrEmpty(target) && options != null)
            {
                try
                {
                    target = connector.BuildConnectionString(options);
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to build connection string for connector '{ConnectorType}' (connection '{ConnectionName}'): {Error}", ex, connectionType, stmt.ConnectionName, ex.Message);
                    throw new ExecutionException($"Failed to build connection string for {connectionType}: {ex.Message}");
                }
            }

            IEnumerable<ColumnDefinition>? templateSchema = null;
            if (options != null && options.TryGetValue("TEMPLATE", out var templateName))
            {
                if (context.Connections.TryGetValue(templateName, out var templateDs) && templateDs is InMemoryDataSource imds)
                {
                    templateSchema = imds.Schema.Values;
                }
                else
                {
                    throw new ExecutionException($"Template table '{templateName}' not found in in-memory session.");
                }
            }

            // Security Hardening: authorize connector type + destination host before the data
            // source is created (i.e. before any DNS resolution / connection attempt). Enforce
            // the canonical connector name so aliases cannot bypass or unexpectedly fail policy.
            new ConnectorPolicyAuthorizer(context.SecurityService).Authorize(
                context, connector.Name, connector.GetHost(target, options), target);

            ds = connector.CreateDataSource(context, target, options, templateSchema);
        }
        else
        {
            throw new ExecutionException($"Connection type '{connectionType}' is not registered or implemented.");
        }

        if (context.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would {(alreadyExists ? "alter" : "create")} connection {stmt.ConnectionName}", ConsoleColor.Yellow);
            return;
        }

        // Dispose existing one if we are replacing it
        if (alreadyExists && existingDataSource != null)
        {
            await existingDataSource.DisposeAsync();
        }

        context.Connections[stmt.ConnectionName] = ds;
        _logger.WriteLine($"Connection {stmt.ConnectionName} {(alreadyExists ? "altered" : "created")}.", ConsoleColor.Green);

        context.LastResult = await BuildConnectionPreviewAsync(ds, stmt.ConnectionName, context);
    }

    private async Task<DataTable> BuildConnectionPreviewAsync(IDataSource ds, string connectionName, IExecutionContext context)
    {
        int previewLimit = context.PreviewLimit ?? _config?.GetValue<int>("Engine:ConnectionPreviewLimit") ?? 10;
        if (previewLimit <= 0)
        {
            _logger.Debug("CREATE CONNECTION: preview disabled for {ConnectionName}; datasource remains unopened.", connectionName);
            return new DataTable { TotalRowsMatched = 0, ExecutionTimeMs = 0 };
        }

        _logger.Debug("CREATE CONNECTION: Fetching columns for {ConnectionName}...", connectionName);
        context.CancellationToken.ThrowIfCancellationRequested();
        var preview = new DataTable();
        var cols = (await ds.GetColumnsAsync(context.CancellationToken)).ToList();
        _logger.Debug("CREATE CONNECTION: Found {ColumnCount} columns for {ConnectionName}.", cols.Count, connectionName);
        if (cols.Any())
        {
            preview.SetColumns(cols.Take(previewLimit));
            try
            {
                _logger.Debug("CREATE CONNECTION: Reading preview rows for {ConnectionName}...", connectionName);
                // batchSize=previewLimit here is the preview row limit, not a batch-count.
                // Take(1) ensures we stop after the first batch.
                var sampleBatches = ds.ReadBatches(batchSize: previewLimit).Take(1);
                await foreach (var b in sampleBatches.WithCancellation(context.CancellationToken))
                {
                    _logger.Debug("CREATE CONNECTION: Preview batch has {RowCount} rows.", b.Rows.Count);
                    context.CancellationToken.ThrowIfCancellationRequested();
                    foreach (var r in b.Rows.Take(previewLimit))
                    {
                        context.CancellationToken.ThrowIfCancellationRequested();
                        await preview.AddRowAsync(r);
                    }
                }
                _logger.Debug("CREATE CONNECTION: Preview complete for {ConnectionName}.", connectionName);
            }
            catch (Exception ex)
            {
                _logger.Warning("CREATE CONNECTION: preview data not available for {ConnectionName}: {Message}", connectionName, ex.Message);
            }
        }
        preview.TotalRowsMatched = preview.Rows.Count;
        preview.ExecutionTimeMs = 0;
        return preview;
    }

    private string StringifyOption(object? val, Expression? expr = null)
    {
        if (val is bool b) return b ? "ON" : "OFF";
        if (val != null) return val.ToString()!;
        // Unquoted bareword identifiers (e.g. DELIMITER = COMMA) — use the identifier name directly.
        if (expr is IdentifierExpression id) return id.Name;
        return "";
    }

    private string Interpolate(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        return Regex.Replace(value, @"\${(\w+)}", match =>
        {
            var varName = match.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(varName);
            if (envValue == null)
                _logger.Warning("CREATE CONNECTION: environment variable '{VarName}' is not set; placeholder left as-is.", varName);
            return envValue ?? match.Value;
        });
    }
}
