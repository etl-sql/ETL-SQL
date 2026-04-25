using ETL_SQL.Common;
using ETL_SQL.Data;
using ETL_SQL.Core;
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
        private readonly ILogger _logger;

        public HelpStatementHandler(IConnectorRegistry connectorRegistry, ILogger logger)
        {
            _connectorRegistry = connectorRegistry;
            _logger = logger;
        }

        public Type SupportedStatementType => typeof(HelpStatement);
        /// <summary>Executes the HELP statement, displaying information about topics or sub-topics.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (HelpStatement)statement;
            if (string.IsNullOrEmpty(stmt.Topic))
            {
                _logger.WriteLine("ETL-SQL Help", ConsoleColor.Cyan);
                _logger.WriteLine("Available commands: CREATE CONNECTION, CREATE TABLE, SELECT, INSERT, UPDATE, DELETE, CREATE TEMPLATE, etc.");
                _logger.WriteLine("Use HELP DIRECTORY or HELP FILE for details on file/directory operations.");
                _logger.WriteLine("Use HELP CONNECTION <type> for details on a specific connection type (e.g. HELP CONNECTION MSSQL).");
                _logger.WriteLine("Use HELP REPORT for details on dashboard and visual commands (Report-SQL).");
                _logger.WriteLine("Use HELP DOCKER for details on container operations.");
                _logger.WriteLine("Use HELP SHOW for details on introspection commands.");
                _logger.WriteLine("Use HELP SET for details on system configuration (e.g. SET TEMPLATE_PATH).");
                _logger.WriteLine("Use HELP <topic> (e.g. HELP DECLARE) for core statement syntax.");
                return;
            }

            if (stmt.Topic.Equals("CONNECTION", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(stmt.SubTopic))
                {
                    _logger.WriteLine("Connection Types:", ConsoleColor.Cyan);
                    foreach (var name in _connectorRegistry.GetRegisteredNames())
                    {
                        _logger.WriteLine($"- {name}");
                    }
                    _logger.WriteLine("Use HELP CONNECTION <type> for details.");
                }
                else
                {
                    var connector = _connectorRegistry.GetConnector(stmt.SubTopic);
                    if (connector != null)
                    {
                        _logger.WriteLine($"HELP: CONNECTON {connector.Name}", ConsoleColor.Cyan);
                        _logger.WriteLine(connector.GetHelp());
                        
                        var options = connector.GetSupportedOptions();
                        if (options.Any())
                        {
                            _logger.WriteLine("\nOptions:", ConsoleColor.Yellow);
                            foreach (var opt in options)
                            {
                                string values = opt.Value.Any() ? " (" + string.Join("|", opt.Value) + ")" : "";
                                _logger.WriteLine($"  {opt.Key}{values}");
                            }
                        }

                        var functions = connector.GetSupportedFunctions();
                        if (functions.Any())
                        {
                            _logger.WriteLine("\nSupported Functions:", ConsoleColor.Yellow);
                            _logger.WriteLine("  " + string.Join(", ", functions.OrderBy(f => f)));
                        }
                    }
                    else
                    {
                        _logger.WriteLine($"Connection type '{stmt.SubTopic}' not found.", ConsoleColor.Red);
                    }
                }
                return;
            }

            if (stmt.Topic.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                var functionRegistry = context.FunctionRegistry;
                if (string.IsNullOrEmpty(stmt.SubTopic))
                {
                    _logger.WriteLine("Available Functions:", ConsoleColor.Cyan);
                    var names = functionRegistry.GetRegisteredNames().OrderBy(n => n).ToList();
                    for (int i = 0; i < names.Count; i += 3)
                    {
                        _logger.WriteLine(string.Join("\t", names.Skip(i).Take(3).Select(n => n.PadRight(20))));
                    }
                    _logger.WriteLine("\nUse HELP FUNCTION <name> for details.");
                }
                else
                {
                    var helpDoc = functionRegistry.GetHelp(stmt.SubTopic);
                    if (helpDoc != null)
                    {
                        _logger.WriteLine($"HELP: FUNCTION {stmt.SubTopic.ToUpperInvariant()}", ConsoleColor.Cyan);
                        _logger.WriteLine(helpDoc);
                    }
                    else if (functionRegistry.IsRegistered(stmt.SubTopic))
                    {
                        _logger.WriteLine($"Function '{stmt.SubTopic}' is registered but has no help documentation.", ConsoleColor.Yellow);
                    }
                    else
                    {
                        _logger.WriteLine($"Function '{stmt.SubTopic}' not found.", ConsoleColor.Red);
                    }
                }
                return;
            }

            // ── Registry-based lookup (shared with LSP) ─────────────────────
            var topic = stmt.Topic;
            var subTopic = stmt.SubTopic;

            // Handle HELP STATEMENT <CMD> redirect
            if (topic.Equals("STATEMENT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(subTopic))
            {
                topic = subTopic;
                subTopic = null;
            }

            var help = context.LanguageHelp.GetHelp(topic, subTopic);
            if (help != null)
            {
                _logger.WriteLine($"HELP: {topic.ToUpperInvariant()} {(string.IsNullOrEmpty(subTopic) ? "" : subTopic.ToUpperInvariant())}", ConsoleColor.Cyan);
                _logger.WriteLine(help);
            }
            else
            {
                _logger.WriteLine($"Help for topic '{stmt.Topic}' is not yet implemented.", ConsoleColor.Yellow);
                _logger.WriteLine("Available topics: CONNECTION, FUNCTION, DIRECTORY, FILE, TRANSFER, EMAIL, SSH_KEY_PAIR, DOCKER, SHOW, VARIABLES, SECURITY, STATEMENT, REPORT, SET");
            }
            await Task.CompletedTask;
        }
    }
}
