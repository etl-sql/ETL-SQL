using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the CREATE CONNECTION statement, registering new data sources in the execution context.
    /// Supports various connector types (SQL, File, specialized) and connection string interpolation.
    /// </summary>
    public class CreateConnectionStatementHandler(IConnectorRegistry connectorRegistry, ILogger logger) : IStatementHandler
    {
        private readonly IConnectorRegistry _connectorRegistry = connectorRegistry;
        private readonly ILogger _logger = logger;


        public Type SupportedStatementType => typeof(CreateConnectionStatement);
        /// <summary>Executes the CREATE CONNECTION statement, resolving the target string and options.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateConnectionStatement)statement;
            
            bool alreadyExists = context.Connections.TryGetValue(stmt.ConnectionName, out var existingDataSource);

            if (stmt.Mode == ObjectCreationMode.Create && alreadyExists)
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' already exists.");
            if (stmt.Mode == ObjectCreationMode.Alter && !alreadyExists)
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' does not exist.");

            string? connectionType = stmt.ConnectionType;
            string? target = null;
            Dictionary<string, string>? options = null;

            if (stmt.Mode == ObjectCreationMode.Alter || (stmt.Mode == ObjectCreationMode.CreateOrAlter && alreadyExists))
            {
                // Patching existing connection
                if (existingDataSource == null) throw new ExecutionException($"Connection '{stmt.ConnectionName}' exists but its data source is null.");
                
                connectionType ??= existingDataSource.ConnectorType;
                options = new Dictionary<string, string>(existingDataSource.Options ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                target = existingDataSource.Path;

                // Merge options
                if (stmt.Options != null)
                {
                    foreach (var kvp in stmt.Options) options[kvp.Key] = kvp.Value;
                }

                // Update target if provided
                if (stmt.TargetExpression != null)
                {
                    target = (await context.EvaluateValue(stmt.TargetExpression, new Row()))?.ToString() ?? "";
                }
            }
            else
            {
                // New connection
                if (connectionType == null) throw new ExecutionException("Connection type must be specified for CREATE CONNECTION.");
                if (stmt.TargetExpression != null)
                {
                    target = (await context.EvaluateValue(stmt.TargetExpression, new Row()))?.ToString() ?? "";
                }
                else
                {
                    target = "";
                }
                options = stmt.Options != null ? new Dictionary<string, string>(stmt.Options, StringComparer.OrdinalIgnoreCase) : null;
            }

            _logger.Debug($"{(alreadyExists ? "Altering" : "Initializing")} connection {stmt.ConnectionName} of type {connectionType}");

            if (target != null && target.StartsWith("ENC:"))
            {
                string? decryptionKey = context.MasterPassword ?? context.ScriptPassword;
                if (string.IsNullOrEmpty(decryptionKey))
                {
                    throw new ExecutionException("A password is required to decrypt the connection string. Use 'USE PASSWORD' in the script or provide a master password.");
                }
                
                try 
                {
                    target = CryptoUtils.Decrypt(target, decryptionKey);
                }
                catch (Exception ex)
                {
                    if (context.MasterPassword != null && context.ScriptPassword != null && context.MasterPassword != context.ScriptPassword)
                    {
                         try { target = CryptoUtils.Decrypt(target, context.ScriptPassword); }
                         catch { throw new ExecutionException($"Failed to decrypt connection string with provided passwords: {ex.Message}"); }
                    }
                    else
                    {
                        throw new ExecutionException($"Failed to decrypt connection string: {ex.Message}");
                    }
                }
            }
            target = Interpolate(target ?? "");

            // Security Hardening: Validate path for file-based connectors
            var fileConnectors = new[] { "FLATFILE", "CSV", "JSON", "XML", "EXCEL", "PARQUET", "AVRO", "DIRECTORY", "SQLITE" };
            if (fileConnectors.Contains(connectionType?.ToUpperInvariant()))
            {
                target = context.ResolvePath(target);
            }

            IDataSource ds;
            var connector = _connectorRegistry.GetConnector(connectionType ?? string.Empty);
            if (connector != null && (target.Contains("Demo", StringComparison.OrdinalIgnoreCase) || target.Contains("Sample", StringComparison.OrdinalIgnoreCase)))
            {
                var mock = _connectorRegistry.GetConnector("MOCKDB");
                if (mock != null) connector = mock;
            }

            var interpolatedOptions = options?.ToDictionary(
                kvp => kvp.Key,
                kvp => Interpolate(kvp.Value),
                StringComparer.OrdinalIgnoreCase
            );

            if (connector != null)
            {
                if (string.IsNullOrEmpty(target) && interpolatedOptions != null)
                {
                    try
                    {
                        target = connector.BuildConnectionString(interpolatedOptions);
                    }
                    catch (Exception ex)
                    {
                        throw new ExecutionException($"Failed to build connection string for {connectionType}: {ex.Message}");
                    }
                }

                IEnumerable<ColumnDefinition>? templateSchema = null;
                if (interpolatedOptions != null && interpolatedOptions.TryGetValue("TEMPLATE", out var templateName))
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

                ds = connector.CreateDataSource(target, interpolatedOptions, templateSchema);
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

            // Generate a preview result for the Result Panel
            var preview = new DataTable();
            var cols = (await ds.GetColumnsAsync()).ToList();
            if (cols.Any())
            {
                preview.SetColumns(cols.Take(10));
                try
                {
                    var sampleBatches = ds.ReadBatches(10).Take(1);
                    await foreach (var b in sampleBatches)
                    {
                        foreach (var r in b.Rows.Take(10)) await preview.AddRowAsync(r);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Preview data not available for {stmt.ConnectionName}: {ex.Message}");
                }
            }
            preview.TotalRowsMatched = preview.Rows.Count;
            preview.ExecutionTimeMs = 0; // Instant metadata preview
            context.LastResult = preview;
        }

        private string Interpolate(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            return Regex.Replace(value, @"\${(\w+)}", match =>
            {
                var varName = match.Groups[1].Value;
                var envValue = Environment.GetEnvironmentVariable(varName);
                return envValue ?? match.Value; // Keep as is if not found
            });
        }
    }
}
