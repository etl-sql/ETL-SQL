using ETL_SQL.Common;
using ETL_SQL.Data;
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
    public class CreateConnectionStatementHandler : IStatementHandler
    {
        private readonly IConnectorRegistry _connectorRegistry;
        public CreateConnectionStatementHandler(IConnectorRegistry connectorRegistry)
        {
            _connectorRegistry = connectorRegistry;
        }

        public Type SupportedStatementType => typeof(CreateConnectionStatement);
        /// <summary>Executes the CREATE CONNECTION statement, resolving the target string and options.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (CreateConnectionStatement)statement;
            Logger.Verbose($"Initializing connection {stmt.ConnectionName} of type {stmt.ConnectionType}");

            if (stmt.Mode == ObjectCreationMode.Create && context.Connections.ContainsKey(stmt.ConnectionName))
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' already exists.");
            if (stmt.Mode == ObjectCreationMode.Alter && !context.Connections.ContainsKey(stmt.ConnectionName))
                throw new ExecutionException($"Connection '{stmt.ConnectionName}' does not exist.");

            string target = (await context.EvaluateValue(stmt.TargetExpression, new Row()))?.ToString() ?? "";
            if (target.StartsWith("ENC:"))
            {
                if (string.IsNullOrEmpty(context.MasterPassword))
                {
                    throw new ExecutionException("Master password is required to decrypt connection string.");
                }
                target = CryptoUtils.Decrypt(target, context.MasterPassword);
            }
            target = Interpolate(target);

            IDataSource ds;
            var connector = _connectorRegistry.GetConnector(stmt.ConnectionType);
            if (connector != null && (target.Contains("Demo", StringComparison.OrdinalIgnoreCase) || target.Contains("Sample", StringComparison.OrdinalIgnoreCase)))
            {
                // Fallback to MockDb for demo connection strings
                var mock = _connectorRegistry.GetConnector("MOCKDB");
                if (mock != null) connector = mock;
            }

            var interpolatedOptions = stmt.Options?.ToDictionary(
                kvp => kvp.Key,
                kvp => Interpolate(kvp.Value),
                StringComparer.OrdinalIgnoreCase
            );

            if (connector != null)
            {
                ds = connector.CreateDataSource(target, interpolatedOptions);
            }
            else
            {
                throw new ExecutionException($"Connection type '{stmt.ConnectionType}' is not registered or implemented.");
            }

            context.Connections[stmt.ConnectionName] = ds;
            Logger.WriteLine($"Connection {stmt.ConnectionName} {(stmt.Mode == ObjectCreationMode.Alter ? "altered" : "defined")}.", ConsoleColor.Green);

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
                        foreach (var r in b.Rows.Take(10)) preview.AddRow(r);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Verbose($"Preview data not available for {stmt.ConnectionName}: {ex.Message}");
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
