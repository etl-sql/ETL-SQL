using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles ALTER CONNECTION statements.
    /// Patches an existing connection: preserves all previous options and only overwrites
    /// the keys (and optionally the type / connection-string) supplied in the statement.
    /// </summary>
    public class AlterConnectionStatementHandler(IConnectorRegistry connectorRegistry, ILogger logger) : IStatementHandler
    {
        private readonly IConnectorRegistry _connectorRegistry = connectorRegistry;
        private readonly ILogger _logger = logger;

        public Type SupportedStatementType => typeof(AlterConnectionStatement);

        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (AlterConnectionStatement)statement;

            if (!context.Connections.TryGetValue(stmt.ConnectionName, out var existingDs) || existingDs == null)
                throw new ExecutionException(
                    $"Cannot ALTER CONNECTION '{stmt.ConnectionName}': connection does not exist.",
                    null, stmt.Line, stmt.Column);

            // Start from the existing state — type, path, and options are all inherited
            var connectionType = stmt.ConnectionType ?? existingDs.ConnectorType;
            var target         = existingDs.Path;
            var options        = new Dictionary<string, string>(
                existingDs.Options ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);

            // Merge new options (only provided keys are overwritten)
            if (stmt.Options != null)
                foreach (var kvp in stmt.Options)
                    options[kvp.Key] = kvp.Value;

            // Replace target if a new one was provided
            if (stmt.TargetExpression != null)
                target = (await context.EvaluateValue(stmt.TargetExpression, new Row()))?.ToString() ?? "";

            target = Interpolate(target ?? "");

            // Decrypt if encrypted
            if (target.StartsWith("ENC:"))
            {
                string? key = context.MasterPassword ?? context.ScriptPassword;
                if (string.IsNullOrEmpty(key))
                    throw new ExecutionException("A password is required to decrypt the connection string.");
                try
                {
                    target = CryptoUtils.Decrypt(target, key);
                }
                catch (Exception ex)
                {
                    throw new ExecutionException($"Failed to decrypt connection string: {ex.Message}");
                }
            }

            // Resolve path for file-based connectors
            var fileConnectors = new[] { "FLATFILE", "CSV", "JSON", "XML", "EXCEL", "PARQUET", "AVRO", "DIRECTORY", "SQLITE" };
            if (fileConnectors.Contains(connectionType?.ToUpperInvariant()))
                target = context.ResolvePath(target);

            var interpolatedOptions = options.ToDictionary(kvp => kvp.Key, kvp => Interpolate(kvp.Value), StringComparer.OrdinalIgnoreCase);

            var connector = _connectorRegistry.GetConnector(connectionType ?? string.Empty)
                ?? throw new ExecutionException($"Connection type '{connectionType}' is not registered.");

            if (string.IsNullOrEmpty(target) && interpolatedOptions.Count > 0)
            {
                try { target = connector.BuildConnectionString(interpolatedOptions); }
                catch (Exception ex) { throw new ExecutionException($"Failed to build connection string: {ex.Message}"); }
            }

            var newDs = connector.CreateDataSource(context, target, interpolatedOptions);

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

        private static string Interpolate(string value) =>
            Regex.Replace(value, @"\${(\w+)}", m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }
}
