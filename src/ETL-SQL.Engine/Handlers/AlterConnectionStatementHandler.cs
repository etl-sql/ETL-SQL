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

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles ALTER CONNECTION statements.
/// Patches an existing connection: preserves all previous options and only overwrites
/// the keys (and optionally the type / connection-string) supplied in the statement.
/// </summary>
public class AlterConnectionStatementHandler(
    IConnectorRegistry connectorRegistry,
    ILogger logger,
    ISecretProvider? secretProvider = null) : IStatementHandler
{
    private readonly IConnectorRegistry _connectorRegistry = connectorRegistry;
    private readonly ILogger _logger = logger;
    private readonly ConnectionSecretResolver _secretResolver = new(secretProvider);

    public Type SupportedStatementType => typeof(AlterConnectionStatement);

    private static readonly HashSet<string> FileConnectorTypes = new(StringComparer.OrdinalIgnoreCase)
        { "FLATFILE", "CSV", "JSON", "XML", "EXCEL", "PARQUET", "AVRO", "DIRECTORY", "SQLITE" };

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AlterConnectionStatement)statement;

        if (!context.Connections.TryGetValue(stmt.ConnectionName, out var existingDs) || existingDs == null)
            throw new ExecutionException(
                $"Cannot ALTER CONNECTION '{stmt.ConnectionName}': connection does not exist.",
                null, stmt.Line, stmt.Column);

        // Start from the existing state — type, path, and options are all inherited
        var connectionType = stmt.ConnectionType ?? existingDs.ConnectorType;
        var target = existingDs.Path;
        var options = new Dictionary<string, string>(
            existingDs.Options ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        // Resolve and decrypt options
        if (stmt.Options != null)
        {
            foreach (var kvp in stmt.Options)
            {
                var val = StringifyOption(await context.EvaluateValue(kvp.Value, new Row(), decryptSensitive: true));
                options[kvp.Key] = Interpolate(val);
            }
        }

        // Replace target if a new one was provided
        if (stmt.TargetExpression != null)
            target = (await context.EvaluateValue(stmt.TargetExpression, new Row(), decryptSensitive: true))?.ToString() ?? "";

        // Decrypt target if necessary
        if (target != null && target.StartsWith("ENC:"))
        {
            target = context.DecryptValue(target);
        }
        target = Interpolate(target ?? "");
        target = await _secretResolver.ResolveTargetAsync(target, context.CancellationToken);
        options = await _secretResolver.ResolveOptionsAsync(options, context.CancellationToken);

        // Resolve path for file-based connectors
        if (FileConnectorTypes.Contains(connectionType ?? string.Empty))
            target = context.ResolvePath(target);

        var connector = _connectorRegistry.GetConnector(connectionType ?? string.Empty)
            ?? throw new ExecutionException($"Connection type '{connectionType}' is not registered.");

        if (string.IsNullOrEmpty(target) && options.Count > 0)
        {
            try { target = connector.BuildConnectionString(options); }
            catch (Exception ex) { throw new ExecutionException($"Failed to build connection string: {ex.Message}"); }
        }

        var newDs = connector.CreateDataSource(context, target, options);

        if (context.IsWhatIf)
        {
            _logger.WriteLine($"WHAT IF: Would alter connection '{stmt.ConnectionName}'", ConsoleColor.Yellow);
            return;
        }

        await existingDs.DisposeAsync();
        context.Connections[stmt.ConnectionName] = newDs;
        _logger.WriteLine($"Connection '{stmt.ConnectionName}' altered.", ConsoleColor.Green);

        context.LastResult = new DataTable();
    }

    private string Interpolate(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Regex.Replace(value, @"\${(\w+)}", m =>
        {
            var varName = m.Groups[1].Value;
            var envValue = Environment.GetEnvironmentVariable(varName);
            if (envValue == null)
                _logger.Warning("ALTER CONNECTION: environment variable '{VarName}' is not set; placeholder left as-is.", varName);
            return envValue ?? m.Value;
        });
    }

    private string StringifyOption(object? val)
    {
        if (val is bool b) return b ? "ON" : "OFF";
        return val?.ToString() ?? "";
    }
}
