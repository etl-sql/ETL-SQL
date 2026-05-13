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
            bool isInteractive = context.InteractiveMode;

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
                    target = (await context.EvaluateValue(stmt.TargetExpression, new Row()))?.ToString() ?? "";
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
            
            var connector = _connectorRegistry.GetConnector(connectionType ?? string.Empty);
            if (connector == null)
            {
                throw new ExecutionException($"Connection type '{connectionType}' is not registered or implemented.");
            }

            // Security Hardening: Validate path for file-based connectors
            if (connector.IsFileBased)
            {
                target = context.ResolvePath(target);
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

                ds = connector.CreateDataSource(context, target, options, templateSchema);

                // Security Hardening: Validate host for network-based connectors
                var host = connector.GetHost(target, options);
                if (host != null)
                {
                    context.SecurityService.ValidateHost(host);
                }
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

            Console.Error.WriteLine("[TRACE] CREATE CONNECTION: Fetching columns...");
            // Generate a preview result for the Result Panel
            context.CancellationToken.ThrowIfCancellationRequested();
            var preview = new DataTable();
            var cols = (await ds.GetColumnsAsync()).ToList();
            Console.Error.WriteLine($"[TRACE] CREATE CONNECTION: Found {cols.Count} columns.");
            if (cols.Any())
            {
                preview.SetColumns(cols.Take(10));
                try
                {
                    Console.Error.WriteLine("[TRACE] CREATE CONNECTION: Reading sample batches...");
                    var sampleBatches = ds.ReadBatches(10).Take(1);
                    await foreach (var b in sampleBatches.WithCancellation(context.CancellationToken))
                    {
                        Console.Error.WriteLine($"[TRACE] CREATE CONNECTION: Got batch with {b.Rows.Count} rows.");
                        context.CancellationToken.ThrowIfCancellationRequested();
                        foreach (var r in b.Rows.Take(10)) 
                        {
                            context.CancellationToken.ThrowIfCancellationRequested();
                            await preview.AddRowAsync(r);
                        }
                    }
                    Console.Error.WriteLine("[TRACE] CREATE CONNECTION: Preview complete.");
                }
                catch (Exception ex)
                {
                    _logger.Debug("Preview data not available for {ConnectionName}: {Message}", stmt.ConnectionName, ex.Message);
                }
            }
            preview.TotalRowsMatched = preview.Rows.Count;
            preview.ExecutionTimeMs = 0; // Instant metadata preview
            context.LastResult = preview;
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
                return envValue ?? match.Value; // Keep as is if not found
            });
        }
    }
}
