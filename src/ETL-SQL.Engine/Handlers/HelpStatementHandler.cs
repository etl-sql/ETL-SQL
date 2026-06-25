using System;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;
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
        string topic = stmt.Topic?.Trim() ?? "";
        string subTopic = stmt.SubTopic?.Trim() ?? "";

        // 1. Root Help
        if (string.IsNullOrEmpty(topic))
        {
            _logger.WriteLine("ETL-SQL Help System", ConsoleColor.Cyan);
            _logger.WriteLine("Categories: CONNECTION, VISUAL, REPORT, LOOP, FUNCTION, SHOW, SET, VARIABLES, SNIPPETS");
            _logger.WriteLine("Examples:");
            _logger.WriteLine("  HELP SELECT           -- Direct command help");
            _logger.WriteLine("  HELP CONNECTION       -- List all data source types");
            _logger.WriteLine("  HELP VISUAL           -- List all chart/widget types");
            _logger.WriteLine("  HELP CONNECTION MSSQL -- Detailed connector options");
            _logger.WriteLine("  HELP @@ROWCOUNT       -- System variable documentation");
            _logger.WriteLine("  HELP SNIPPETS         -- List all $trigger snippet templates");
            return;
        }

        // 2. Specialized Redirects & Categories
        if (topic.Equals("STATEMENT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(subTopic))
        {
            topic = subTopic; subTopic = "";
        }

        // 3. Category: CONNECTION (Grouped)
        if (topic.Equals("CONNECTION", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(subTopic))
            {
                var helpText = context.LanguageHelp.GetHelp("CONNECTION", "INDEX");
                _logger.WriteLine("HELP: CONNECTION", ConsoleColor.Cyan);
                _logger.WriteLine(helpText ?? "List of connection types not found.");
                return;
            }

            var connector = _connectorRegistry.GetConnector(subTopic);
            if (connector != null)
            {
                _logger.WriteLine($"HELP: CONNECTION {connector.Name}", ConsoleColor.Cyan);
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
                return;
            }
        }

        // 4. Category: FUNCTION
        if (topic.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase))
        {
            var functionRegistry = context.FunctionRegistry;
            if (string.IsNullOrEmpty(subTopic))
            {
                _logger.WriteLine("Available Functions:", ConsoleColor.Cyan);
                var names = functionRegistry.GetRegisteredNames().OrderBy(n => n).ToList();
                for (int i = 0; i < names.Count; i += 3)
                    _logger.WriteLine(string.Join("\t", names.Skip(i).Take(3).Select(n => n.PadRight(20))));
                _logger.WriteLine("\nUse HELP FUNCTION <name> for details.");
            }
            else
            {
                var helpDoc = functionRegistry.GetHelp(subTopic);
                if (helpDoc != null)
                {
                    _logger.WriteLine($"HELP: FUNCTION {subTopic.ToUpperInvariant()}", ConsoleColor.Cyan);
                    _logger.WriteLine(helpDoc);
                }
                else
                {
                    _logger.WriteLine($"Function '{subTopic}' not found.", ConsoleColor.Red);
                }
            }
            return;
        }

        // 5. Category: SNIPPETS
        if (topic.Equals("SNIPPETS", StringComparison.OrdinalIgnoreCase))
        {
            var snippets = ETL_SQL.Core.Metadata.SnippetLibrary.Instance.GetAll();

            if (string.IsNullOrEmpty(subTopic))
            {
                _logger.WriteLine("HELP: SNIPPETS", ConsoleColor.Cyan);
                _logger.WriteLine("Type $<trigger> at the start of a line to expand a scaffold template.\n");
                foreach (var s in snippets)
                    _logger.WriteLine($"  {s.Trigger,-16} {s.Description}");
                _logger.WriteLine("\nUse HELP SNIPPETS <trigger> for the full template body. Example: HELP SNIPPETS bar");
            }
            else
            {
                var trigger = subTopic.StartsWith("$") ? subTopic : $"${subTopic}";
                var snippet = snippets.FirstOrDefault(s => s.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
                if (snippet != null)
                {
                    _logger.WriteLine($"HELP: SNIPPET {snippet.Trigger}", ConsoleColor.Cyan);
                    _logger.WriteLine($"{snippet.Description}\n");
                    _logger.WriteLine(snippet.TuiBody);
                }
                else
                {
                    _logger.WriteLine($"Snippet '{trigger}' not found.", ConsoleColor.Yellow);
                    _logger.WriteLine("Use HELP SNIPPETS to list all available snippets.");
                }
            }
            return;
        }

        // 6. Category: REPORT / VISUAL (Redirect to Index if no subtopic)
        if (topic.Equals("REPORT", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(subTopic))
        {
            subTopic = "INDEX";
        }
        if (topic.Equals("VISUAL", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(subTopic))
        {
            subTopic = "VISUAL";
            topic = "REPORT";
        }

        // 7. Registry-based lookup (Direct or Scoped)
        var help = context.LanguageHelp.GetHelp(topic, subTopic);
        if (help != null)
        {
            _logger.WriteLine($"HELP: {topic.ToUpperInvariant()} {(string.IsNullOrEmpty(subTopic) ? "" : subTopic.ToUpperInvariant())}", ConsoleColor.Cyan);
            _logger.WriteLine(help);
            return;
        }

        // 8. Shorthand Fallback (e.g. HELP BAR or HELP MSSQL)
        if (string.IsNullOrEmpty(subTopic))
        {
            // Try searching all subtopics in the registry
            foreach (var topTopic in context.LanguageHelp.GetTopics())
            {
                var subHelp = context.LanguageHelp.GetHelp(topTopic, topic);
                if (subHelp != null)
                {
                    _logger.WriteLine($"HELP: {topTopic.ToUpperInvariant()} {topic.ToUpperInvariant()}", ConsoleColor.Cyan);
                    _logger.WriteLine(subHelp);
                    return;
                }
            }

            // Try direct topic search in registry (for things like DECLARE, SET, etc.)
            var directHelp = context.LanguageHelp.GetHelp(topic);
            if (directHelp != null)
            {
                _logger.WriteLine($"HELP: {topic.ToUpperInvariant()}", ConsoleColor.Cyan);
                _logger.WriteLine(directHelp);
                return;
            }
        }

        _logger.WriteLine($"Help for topic '{topic}' {(string.IsNullOrEmpty(subTopic) ? "" : subTopic)} not found.", ConsoleColor.Yellow);
        _logger.WriteLine("Available categories: CONNECTION, FUNCTION, VISUAL, REPORT, LOOP, SHOW, VARIABLES, SET, SECURITY");
        await Task.CompletedTask;
    }
}
