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
            else if (stmt.Topic.Equals("VARIABLES", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("System Variables (@@):", ConsoleColor.Cyan);
                _logger.WriteLine("  @@VERSION:     Engine version string.");
                _logger.WriteLine("  @@TRANCOUNT:   Active transaction nesting level.");
                _logger.WriteLine("  @@ROWCOUNT:    Rows processed by the last DML statement.");
                _logger.WriteLine("  @@RESULTSETS:  Count of result sets returned by the last operation.");
                _logger.WriteLine("\nSession Variables (@):", ConsoleColor.Yellow);
                _logger.WriteLine("  Defined via DECLARE @varname. View all with SHOW VARIABLES.");
            }
            else if (stmt.Topic.Equals("SECURITY", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("Zero-Trust Security Guardrails:", ConsoleColor.Cyan);
                _logger.WriteLine("\n1. Path Isolation:", ConsoleColor.Yellow);
                _logger.WriteLine("   - Access to root (C:\\, /), System32, /etc, .git, .ssh is blocked.");
                _logger.WriteLine("   - All paths must be absolute or resolved via WORKSPACE.");
                _logger.WriteLine("\n2. Script Immutability:", ConsoleColor.Yellow);
                _logger.WriteLine("   - Scripts cannot create or edit .sql, .etlsql, or .rptsql files.");
                _logger.WriteLine("\n3. Runaway Protection:", ConsoleColor.Yellow);
                _logger.WriteLine("   - Default limit: 100 file operations / 5 levels of recursion.");
                _logger.WriteLine("   - Overrides (### ALLOW_...) only valid in approved Safe Zones.");
            }
            else if (stmt.Topic.Equals("STATEMENT", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(stmt.SubTopic))
                {
                    _logger.WriteLine("Statement Syntax Help:", ConsoleColor.Cyan);
                    _logger.WriteLine("  Use HELP STATEMENT <name> (e.g., HELP STATEMENT SELECT).");
                    _logger.WriteLine("\nCore Statements: SELECT, INSERT, UPDATE, DELETE, MERGE, WAITFOR, PRINT, CREATE CONNECTION");
                }
                else
                {
                    var sub = stmt.SubTopic.ToUpperInvariant();
                    _logger.WriteLine($"HELP: STATEMENT {sub}", ConsoleColor.Cyan);
                    switch (sub)
                    {
                        case "SELECT":
                            _logger.WriteLine("Syntax: SELECT [TOP n] <cols> [INTO <table>] FROM <src> [JOIN...] [WHERE...] [GROUP BY...] [ORDER BY...]");
                            break;
                        case "MERGE":
                            _logger.WriteLine("Syntax: MERGE INTO <target> USING <source> ON <condition> WHEN MATCHED THEN UPDATE... WHEN NOT MATCHED THEN INSERT...");
                            break;
                        case "WAITFOR":
                            _logger.WriteLine("Syntax: WAITFOR DELAY 'hh:mm:ss' | TIME 'hh:mm:ss' | (condition)");
                            break;
                        case "INSERT":
                            _logger.WriteLine("Syntax: INSERT INTO <target> [(cols)] SELECT... | VALUES(...)");
                            break;
                        case "UPDATE":
                            _logger.WriteLine("Syntax: UPDATE <target> SET <col>=<val> [WHERE...]");
                            break;
                        case "DELETE":
                            _logger.WriteLine("Syntax: DELETE FROM <target> [WHERE...]");
                            break;
                        case "PRINT":
                            _logger.WriteLine("Syntax: PRINT <expression> [, timestamp=TRUE|FALSE]");
                            break;
                        case "CREATE":
                            _logger.WriteLine("Syntax: CREATE CONNECTION <name> ON <type>(<conn_string>) [WITH(...)];");
                            break;
                        default:
                            _logger.WriteLine($"No syntax summary available for statement '{sub}'.", ConsoleColor.Yellow);
                            break;
                    }
                }
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
                _logger.WriteLine("  SHOW VARIABLES [LOCAL] [INTO #temp]");
                _logger.WriteLine("  SHOW TAGS FOR TABLE <tbl> [COLUMN <col>] [INTO #temp]");
                _logger.WriteLine("  SHOW TAG VALUE FOR TABLE <tbl> [COLUMN <col>] WITH TAG <tag> [INTO #temp]");
                _logger.WriteLine("  SHOW PROFILE [INTO #temp]");
                _logger.WriteLine("  SHOW VERSION [INTO #temp]");
            }
            else if (stmt.Topic.Equals("REPORT", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(stmt.SubTopic))
                {
                    _logger.WriteLine("Report-SQL Help Index:", ConsoleColor.Cyan);
                    _logger.WriteLine("  USE HELP REPORT <sub-topic> for syntax and examples:");
                    _logger.WriteLine("  - VISUAL:     Charts, cards, and filter controls.");
                    _logger.WriteLine("  - PAGE:       Layout arrangement and parameters.");
                    _logger.WriteLine("  - DATASET:    Pre-computed tables with caching/encryption.");
                    _logger.WriteLine("  - CONTAINER:  Grouping visuals (Box/Scroll).");
                    _logger.WriteLine("  - STYLE:      Reusable CSS-like property bundles.");
                    _logger.WriteLine("  - TEMPLATE:   Global dashboard theme definitions.");
                    _logger.WriteLine("  - NAVIGATION: Menu bars and tab-based routing.");
                    _logger.WriteLine("\nMetadata:");
                    _logger.WriteLine("  - SET REPORT TITLE = '...'");
                    _logger.WriteLine("  - SET REPORT DESCRIPTION = '...'");
                }
                else
                {
                    var sub = stmt.SubTopic.ToUpperInvariant();
                    _logger.WriteLine($"HELP: REPORT {sub}", ConsoleColor.Cyan);
                    switch (sub)
                    {
                        case "VISUAL":
                            _logger.WriteLine("Syntax: CREATE VISUAL <name> AS <TYPE> (SOURCE=..., MAPPINGS(...), OPTIONS(...), STYLE(...))");
                            _logger.WriteLine("Types: BAR, HBAR, LINE, SCATTER, PIE, DONUT, COMBO, GAUGE, FUNNEL, WATERFALL, TABLE, CARD, TEXT");
                            _logger.WriteLine("Filters: SLICER, DATEPICKER, SLIDER, MULTISELECT, SEARCH");
                            _logger.WriteLine("\nExample:");
                            _logger.WriteLine("  CREATE VISUAL Sales AS BAR (SOURCE=#data, MAPPINGS(X=month, Y=total));");
                            break;
                        case "PAGE":
                            _logger.WriteLine("Syntax: CREATE PAGE <name> AS LAYOUT (STRUCTURE='...', MAP(...)) [WITH PARAMETERS (...)]");
                            _logger.WriteLine("Structure: 'A A / B C' (CSS grid-template-areas)");
                            _logger.WriteLine("\nExample:");
                            _logger.WriteLine("  CREATE PAGE Main AS LAYOUT (STRUCTURE='A', MAP('A'=Chart)) WITH PARAMETERS (@reg='All');");
                            break;
                        case "DATASET":
                            _logger.WriteLine("Syntax: CREATE DATASET #name [REFRESH EVERY '...'] [ENCRYPT = ...] AS (SELECT ...)");
                            _logger.WriteLine("Options: COMPRESS=ON|OFF, TTL='...', PASSWORD='...'");
                            _logger.WriteLine("\nExample:");
                            _logger.WriteLine("  CREATE DATASET #Cache REFRESH EVERY '1h' AS (SELECT * FROM LargeTable);");
                            break;
                        case "CONTAINER":
                            _logger.WriteLine("Syntax: CREATE CONTAINER <name> AS <BOX|SCROLL> (VISUALS = (V1, V2, ...))");
                            _logger.WriteLine("\nExample:");
                            _logger.WriteLine("  CREATE CONTAINER Info AS SCROLL (VISUALS = (Chart1, Chart2)) STYLE(HEIGHT=400);");
                            break;
                        case "STYLE":
                            _logger.WriteLine("Syntax: CREATE STYLE <name> (PROPERTY = 'value', ...)");
                            _logger.WriteLine("Properties: BACKGROUND-COLOR, COLOR, BORDER, BORDER-RADIUS, PADDING, FONT-SIZE, TOOLTIP");
                            _logger.WriteLine("\nExample:");
                            _logger.WriteLine("  CREATE STYLE RedCard (BACKGROUND-COLOR='red', COLOR='white', PADDING='10px');");
                            break;
                        case "TEMPLATE":
                            _logger.WriteLine("Syntax: CREATE TEMPLATE <name> AS (key = value, ...)");
                            _logger.WriteLine("Usage: Global dashboard color schemes. Persisted as JSON in TEMPLATE_PATH.");
                            break;
                        case "NAVIGATION":
                            _logger.WriteLine("Syntax: CREATE NAVIGATION <name> AS <TAB|BUTTON|LINK> (PAGES = (P1, P2, ...), DEFAULT='P1')");
                            break;
                        default:
                            _logger.WriteLine($"No detailed help available for Report-SQL sub-topic '{sub}'.", ConsoleColor.Yellow);
                            break;
                    }
                }
            }
            else if (stmt.Topic.Equals("TEMPLATE", StringComparison.OrdinalIgnoreCase))
            {
                // Alias to HELP REPORT TEMPLATE
                _logger.WriteLine("HELP: TEMPLATE", ConsoleColor.Cyan);
                _logger.WriteLine("Syntax: CREATE TEMPLATE <name> AS (key = value, ...)");
                _logger.WriteLine("Templates provide global UI overrides for the ReportPlayer dashboard.");
                _logger.WriteLine("Options: BG_COLOR, TEXT_COLOR, ACCENT_COLOR, FONT_FAMILY");
                _logger.WriteLine("\nLifecycles: CREATE, ALTER, DROP [IF EXISTS]");
            }
            else if (stmt.Topic.Equals("SET", StringComparison.OrdinalIgnoreCase))
            {
                _logger.WriteLine("System Configuration (SET):", ConsoleColor.Cyan);
                _logger.WriteLine("\nReporting:", ConsoleColor.Yellow);
                _logger.WriteLine("  SET TEMPLATE_PATH = 'path'      -- Override dashboard template directory.");
                _logger.WriteLine("  SET REPORT TITLE = 'string'     -- Set global report header.");
                
                _logger.WriteLine("\nEngine Tweaks:", ConsoleColor.Yellow);
                _logger.WriteLine("  SET BATCHSIZE = n               -- Set rows per batch (default 10000).");
                _logger.WriteLine("  SET MAX_IN_MEMORY_BATCHES = n    -- Control RAM usage for #temp tables.");
                _logger.WriteLine("  SET JOIN_SPILL_THRESHOLD = n    -- Rows before join spills to disk.");
                
                _logger.WriteLine("\nSecurity & Behavior:", ConsoleColor.Yellow);
                _logger.WriteLine("  SET WHAT_IF <ON|OFF>            -- Enable/disable dry-run mode.");
                _logger.WriteLine("  SET SHOW_PASSWORD <ON|OFF>      -- Mask/unmask passwords in logs.");
                _logger.WriteLine("  SET PROFILE <ON|OFF>            -- Enable/disable execution profiling.");
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
