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
                Logger.WriteLine("Use HELP DIRECTORY or HELP FILE for details on file/directory operations.");
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
            else if (stmt.Topic.Equals("DIRECTORY", StringComparison.OrdinalIgnoreCase))
            {
                Logger.WriteLine("Directory Operations:", ConsoleColor.Cyan);
                Logger.WriteLine("All directory operations support both VERBOSE (SQL-style) and SHORTHAND (function-style) syntax.");
                Logger.WriteLine("\nCommands:", ConsoleColor.Yellow);
                
                Logger.WriteLine("  CREATE DIRECTORY 'path'");
                Logger.WriteLine("    SHORTHAND: CREATE_DIRECTORY('path')");
                
                Logger.WriteLine("\n  DELETE DIRECTORY 'path'");
                Logger.WriteLine("    SHORTHAND: DELETE_DIRECTORY('path')");
                
                Logger.WriteLine("\n  RENAME DIRECTORY 'old' TO 'new'");
                Logger.WriteLine("    SHORTHAND: RENAME_DIRECTORY('old', 'new')");
                
                Logger.WriteLine("\n  MOVE DIRECTORY 'src' TO 'dest' [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: MOVE_DIRECTORY('src', 'dest', [overwrite])");
                
                Logger.WriteLine("\n  COPY DIRECTORY 'src' TO 'dest' [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: COPY_DIRECTORY('src', 'dest', [overwrite])");

                Logger.WriteLine("\n  COMPRESS DIRECTORY 'src' TO 'dest.zip' [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: COMPRESS_DIRECTORY('src', 'dest.zip', [overwrite])");

                Logger.WriteLine("\n  ENCRYPT DIRECTORY 'src' TO 'dest' PASSWORD('pwd') [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: ENCRYPT_DIRECTORY('src', 'dest', 'pwd', [overwrite])");

                Logger.WriteLine("\n  DECRYPT DIRECTORY 'src' TO 'dest' PASSWORD('pwd') [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: DECRYPT_DIRECTORY('src', 'dest', 'pwd', [overwrite])");

                Logger.WriteLine("\nNote: OVERWRITE=ON is equivalent to RECURSIVE=ON for directories.");
            }
            else if (stmt.Topic.Equals("FILE", StringComparison.OrdinalIgnoreCase))
            {
                Logger.WriteLine("File Operations:", ConsoleColor.Cyan);
                Logger.WriteLine("All file operations support both VERBOSE (SQL-style) and SHORTHAND (function-style) syntax.");
                Logger.WriteLine("\nCommands:", ConsoleColor.Yellow);

                Logger.WriteLine("  COPY FILE 'src' TO 'dest' [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: COPY_FILE('src', 'dest', [overwrite])");

                Logger.WriteLine("\n  MOVE FILE 'src' TO 'dest' [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: MOVE_FILE('src', 'dest', [overwrite])");

                Logger.WriteLine("\n  RENAME FILE 'old' TO 'new'");
                Logger.WriteLine("    SHORTHAND: RENAME_FILE('old', 'new')");

                Logger.WriteLine("\n  DELETE FILE 'path'");
                Logger.WriteLine("    SHORTHAND: DELETE_FILE('path')");

                Logger.WriteLine("\n  COMPRESS FILE 'src' TO 'dest.zip' [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: COMPRESS_FILE('src', 'dest.zip', [overwrite])");

                Logger.WriteLine("\n  ENCRYPT FILE 'src' TO 'dest' PASSWORD('pwd') [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: ENCRYPT_FILE('src', 'dest', 'pwd', [overwrite])");

                Logger.WriteLine("\n  DECRYPT FILE 'src' TO 'dest' PASSWORD('pwd') [WITH(OVERWRITE=ON|OFF)]");
                Logger.WriteLine("    SHORTHAND: DECRYPT_FILE('src', 'dest', 'pwd', [overwrite])");
            }
            else
            {
                Logger.WriteLine($"Help for topic '{stmt.Topic}' is not yet implemented.", ConsoleColor.Yellow);
                Logger.WriteLine("Available topics: CONNECTION, FUNCTION, DIRECTORY, FILE");
            }
            await Task.CompletedTask;
        }
    }
}
