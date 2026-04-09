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
                _logger.WriteLine("Available commands: CREATE CONNECTION, CREATE TABLE, SELECT, INSERT, UPDATE, DELETE, etc.");
                _logger.WriteLine("Use HELP DIRECTORY or HELP FILE for details on file/directory operations.");
                _logger.WriteLine("Use HELP CONNECTION <type> for details on a specific connection type (e.g. HELP CONNECTION MSSQL).");
                _logger.WriteLine("Use HELP DOCKER for details on container operations.");
                _logger.WriteLine("Use HELP SHOW for details on introspection commands.");
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
                        _logger.WriteLine($"HELP: {connector.Name}", ConsoleColor.Cyan);
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
            }
            else if (stmt.Topic.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
            {
                var functionRegistry = context.FunctionRegistry;
                if (string.IsNullOrEmpty(stmt.SubTopic))
                {
                    _logger.WriteLine("Available Functions:", ConsoleColor.Cyan);
                    var names = functionRegistry.GetRegisteredNames().OrderBy(n => n).ToList();
                    for (int i = 0; i < names.Count; i += 3)
                    {
                        _logger.WriteLine(string.Join("\t", names.Skip(i).Take(3)));
                    }
                    _logger.WriteLine("\nUse HELP FUNCTION <name> for details.");
                }
                else
                {
                    var help = functionRegistry.GetHelp(stmt.SubTopic);
                    if (help != null)
                    {
                        _logger.WriteLine($"HELP: {stmt.SubTopic.ToUpperInvariant()}", ConsoleColor.Cyan);
                        _logger.WriteLine(help);
                    }
                    else if (stmt.SubTopic.Equals("SYSDATE", StringComparison.OrdinalIgnoreCase) || 
                             stmt.SubTopic.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.WriteLine($"HELP: {stmt.SubTopic.ToUpperInvariant()}", ConsoleColor.Cyan);
                        _logger.WriteLine($"{stmt.SubTopic.ToUpperInvariant()}: System constant returning the current date and time. Use without parentheses.");
                        _logger.WriteLine("Supports arithmetic (e.g., SYSDATE + 1 for tomorrow).");
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
            }
            else if (stmt.Topic.Equals("DIRECTORY", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("Directory Operations:", ConsoleColor.Cyan);
                _logger.WriteLine("\nCommands:", ConsoleColor.Yellow);
                
                _logger.WriteLine("  VERBOSE:   CREATE DIRECTORY 'path'");
                _logger.WriteLine("  SHORTHAND: CREATE_DIRECTORY('path')");
                
                _logger.WriteLine("\n  VERBOSE:   DELETE DIRECTORY 'path'");
                _logger.WriteLine("  SHORTHAND: DELETE_DIRECTORY('path')");
                
                _logger.WriteLine("\n  VERBOSE:   RENAME DIRECTORY 'old' TO 'new'");
                _logger.WriteLine("  SHORTHAND: RENAME_DIRECTORY('old', 'new')");
                
                _logger.WriteLine("\n  VERBOSE:   MOVE DIRECTORY 'src' TO 'dest' [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: MOVE_DIRECTORY('src', 'dest', [overwrite])");
                
                _logger.WriteLine("\n  VERBOSE:   COPY DIRECTORY 'src' TO 'dest' [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: COPY_DIRECTORY('src', 'dest', [overwrite])");

                _logger.WriteLine("\n  VERBOSE:   COMPRESS DIRECTORY 'src' TO 'dest.zip' [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: COMPRESS_DIRECTORY('src', 'dest.zip', [overwrite])");

                _logger.WriteLine("\n  VERBOSE:   ENCRYPT DIRECTORY 'src' TO 'dest' PASSWORD('pwd') [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: ENCRYPT_DIRECTORY('src', 'dest', 'pwd', [overwrite])");

                _logger.WriteLine("\n  VERBOSE:   DECRYPT DIRECTORY 'src' TO 'dest' PASSWORD('pwd') [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: DECRYPT_DIRECTORY('src', 'dest', 'pwd', [overwrite])");

                _logger.WriteLine("\nNote: OVERWRITE=ON is equivalent to RECURSIVE=ON for directories.");
            }
            else if (stmt.Topic.Equals("FILE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("File Operations:", ConsoleColor.Cyan);
                _logger.WriteLine("\nCommands:", ConsoleColor.Yellow);

                _logger.WriteLine("  VERBOSE:   COPY FILE 'src' TO 'dest' [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: COPY_FILE('src', 'dest', [overwrite])");

                _logger.WriteLine("\n  VERBOSE:   MOVE FILE 'src' TO 'dest' [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: MOVE_FILE('src', 'dest', [overwrite])");

                _logger.WriteLine("\n  VERBOSE:   RENAME FILE 'old' TO 'new'");
                _logger.WriteLine("  SHORTHAND: RENAME_FILE('old', 'new')");

                _logger.WriteLine("\n  VERBOSE:   DELETE FILE 'path'");
                _logger.WriteLine("  SHORTHAND: DELETE_FILE('path')");

                _logger.WriteLine("\n  VERBOSE:   COMPRESS FILE 'src' TO 'dest.zip' [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: COMPRESS_FILE('src', 'dest.zip', [overwrite])");

                _logger.WriteLine("\n  VERBOSE:   ENCRYPT FILE 'src' TO 'dest' PASSWORD('pwd') [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: ENCRYPT_FILE('src', 'dest', 'pwd', [overwrite])");

                _logger.WriteLine("\n  VERBOSE:   DECRYPT FILE 'src' TO 'dest' PASSWORD('pwd') [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: DECRYPT_FILE('src', 'dest', 'pwd', [overwrite])");
            }
            else if (stmt.Topic.Equals("TRANSFER", StringComparison.OrdinalIgnoreCase) || 
                     stmt.Topic.Equals("SEND", StringComparison.OrdinalIgnoreCase) || 
                     stmt.Topic.Equals("RECEIVE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("File Transfer Operations:", ConsoleColor.Cyan);
                _logger.WriteLine("\nCommands:", ConsoleColor.Yellow);

                _logger.WriteLine("  VERBOSE:   SEND FILE 'local' TO 'remote' AT conn [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: SEND_FILE('local', 'remote', conn, [overwrite])");

                _logger.WriteLine("\n  VERBOSE:   RECEIVE FILE 'remote' TO 'local' AT conn [WITH(OVERWRITE=ON|OFF)]");
                _logger.WriteLine("  SHORTHAND: RECEIVE_FILE('remote', 'local', conn, [overwrite])");
            }
            else if (stmt.Topic.Equals("EMAIL", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("Email Operations:", ConsoleColor.Cyan);
                _logger.WriteLine("\nCommands:", ConsoleColor.Yellow);

                _logger.WriteLine("  VERBOSE:   SEND EMAIL TO 'to' FROM 'from' SUBJECT 'subj' BODY 'body' AT conn [ATTACH 'file'] [CC 'cc'] [BCC 'bcc']");
                _logger.WriteLine("  SHORTHAND: SEND_EMAIL(conn, 'to', 'from', 'subj', 'body', [attachments], [cc], [bcc])");
            }
            else if (stmt.Topic.Equals("SSH_KEY_PAIR", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("SSH Key Pair Operations:", ConsoleColor.Cyan);
                _logger.WriteLine("\nCommands:", ConsoleColor.Yellow);
                _logger.WriteLine("  VERBOSE:   CREATE SSH_KEY_PAIR 'path' WITH(BITS=2048, ALGORITHM='RSA', PASSPHRASE='pwd')");
                _logger.WriteLine("  SHORTHAND: SSH_KEY_PAIR('path', 2048, 'RSA', 'pwd')");
            }
            else if (stmt.Topic.Equals("DOCKER", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("Docker Operations:", ConsoleColor.Cyan);
                _logger.WriteLine("\nCommands:", ConsoleColor.Yellow);
                _logger.WriteLine("  START_DOCKER <image> [AS <alias>]");
                _logger.WriteLine("  STOP_DOCKER <alias>");
                _logger.WriteLine("  PAUSE_DOCKER <alias>");
                _logger.WriteLine("  RESUME_DOCKER <alias>");
                _logger.WriteLine("  CLOSE_DOCKER <alias|image>");
                _logger.WriteLine("\nNote: All commands support optional parentheses, e.g., START_DOCKER('mysql').");
            }
            else if (stmt.Topic.Equals("SHOW", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("Introspection Commands (SHOW):", ConsoleColor.Cyan);
                _logger.WriteLine("\nCommands:", ConsoleColor.Yellow);
                _logger.WriteLine("  SHOW JOBS [INTO #temp]");
                _logger.WriteLine("  SHOW JOB HISTORY [<name>] [INTO #temp]");
                _logger.WriteLine("  SHOW CONNECTIONS [INTO #temp]");
                _logger.WriteLine("  SHOW TABLES [ON <conn>] [INTO #temp]");
                _logger.WriteLine("  SHOW COLUMNS FOR [<table>] [INTO #temp]");
                _logger.WriteLine("  SHOW TAGS FOR TABLE <tbl> [COLUMN <col>] [INTO #temp]");
                _logger.WriteLine("  SHOW TAG VALUE FOR TABLE <tbl> [COLUMN <col>] WITH TAG <tag> [INTO #temp]");
                _logger.WriteLine("  SHOW PROFILE [INTO #temp]");
            }
            else
            {
                _logger.WriteLine($"Help for topic '{stmt.Topic}' is not yet implemented.", ConsoleColor.Yellow);
                _logger.WriteLine("Available topics: CONNECTION, FUNCTION, DIRECTORY, FILE, TRANSFER, EMAIL, SSH_KEY_PAIR, DOCKER, SHOW");
            }
            await Task.CompletedTask;
        }
    }
}
