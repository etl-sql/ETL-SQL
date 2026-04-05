using ETL_SQL.Common;
using ETL_SQL.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the HELP statement, providing documentation for commands and connection types.
    /// </summary>
    public class HelpStatementHandler : IStatementHandler
    {
        private readonly IConnectorRegistry _connectorRegistry;
        public HelpStatementHandler(IConnectorRegistry connectorRegistry)
        {
            _connectorRegistry = connectorRegistry;
        }

        public Type SupportedStatementType => typeof(HelpStatement);
        /// <summary>Executes the HELP statement, displaying information about topics or sub-topics.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (HelpStatement)statement;
            if (string.IsNullOrEmpty(stmt.Topic))
            {
                Logger.WriteLine("ETL-SQL Help", ConsoleColor.Cyan);
                Logger.WriteLine("Available commands: CREATE CONNECTION, CREATE TABLE, SELECT, INSERT, UPDATE, DELETE, etc.");
                Logger.WriteLine("Use HELP CONNECTION <type> for details on a specific connection type (e.g. HELP CONNECTION MSSQL).");
                return;
            }

            if (stmt.Topic.Equals("CONNECTION", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(stmt.SubTopic))
                {
                    Logger.WriteLine("Connection Types:", ConsoleColor.Cyan);
                    foreach (var name in _connectorRegistry.GetRegisteredNames())
                    {
                        Logger.WriteLine($"- {name}");
                    }
                    Logger.WriteLine("Use HELP CONNECTION <type> for details.");
                }
                else
                {
                    var connector = _connectorRegistry.GetConnector(stmt.SubTopic);
                    if (connector != null)
                    {
                        Logger.WriteLine($"HELP: {connector.Name}", ConsoleColor.Cyan);
                        Logger.WriteLine(connector.GetHelp());
                        
                        var options = connector.GetSupportedOptions();
                        if (options.Any())
                        {
                            Logger.WriteLine("\nOptions:", ConsoleColor.Yellow);
                            foreach (var opt in options)
                            {
                                string values = opt.Value.Any() ? " (" + string.Join("|", opt.Value) + ")" : "";
                                Logger.WriteLine($"  {opt.Key}{values}");
                            }
                        }

                        var functions = connector.GetSupportedFunctions();
                        if (functions.Any())
                        {
                            Logger.WriteLine("\nSupported Functions:", ConsoleColor.Yellow);
                            Logger.WriteLine("  " + string.Join(", ", functions.OrderBy(f => f)));
                        }
                    }
                    else
                    {
                        Logger.WriteLine($"Connection type '{stmt.SubTopic}' not found.", ConsoleColor.Red);
                    }
                }
            }
            else if (stmt.Topic.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                var functionRegistry = context.FunctionRegistry;
                if (string.IsNullOrEmpty(stmt.SubTopic))
                {
                    Logger.WriteLine("Available Functions:", ConsoleColor.Cyan);
                    var names = functionRegistry.GetRegisteredNames().OrderBy(n => n).ToList();
                    for (int i = 0; i < names.Count; i += 3)
                    {
                        Logger.WriteLine(string.Join("\t", names.Skip(i).Take(3)));
                    }
                    Logger.WriteLine("\nUse HELP FUNCTION <name> for details.");
                }
                else
                {
                    var help = functionRegistry.GetHelp(stmt.SubTopic);
                    if (help != null)
                    {
                        Logger.WriteLine($"HELP: {stmt.SubTopic.ToUpperInvariant()}", ConsoleColor.Cyan);
                        Logger.WriteLine(help);
                    }
                    else if (functionRegistry.IsRegistered(stmt.SubTopic))
                    {
                        Logger.WriteLine($"Function '{stmt.SubTopic}' is registered but has no help documentation.", ConsoleColor.Yellow);
                    }
                    else
                    {
                        Logger.WriteLine($"Function '{stmt.SubTopic}' not found.", ConsoleColor.Red);
                    }
                }
            }
            else
            {
                Logger.WriteLine($"Help for topic '{stmt.Topic}' is not yet implemented.", ConsoleColor.Yellow);
                Logger.WriteLine("Available topics: CONNECTION, FUNCTION");
            }
            await Task.CompletedTask;
        }
    }
}
