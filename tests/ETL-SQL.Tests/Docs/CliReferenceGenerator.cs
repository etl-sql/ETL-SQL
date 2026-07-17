using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.App;

namespace ETL_SQL.Tests.Docs;

/// <summary>
/// Generates the CLI command reference (docs/reference/cli/**) from the single System.CommandLine
/// command tree in <c>CliOrchestrator.BuildRootCommand</c>, so the pages never drift from the code.
/// Consumed by <see cref="CliReferenceTests"/>: normally it verifies the committed pages match; set
/// <c>ETLSQL_REGEN_CLI_DOCS=1</c> to rewrite them.
/// </summary>
public static class CliReferenceGenerator
{
    public const string CliDir = "docs/reference/cli";

    /// <summary>Returns a map of repo-relative page path -> expected markdown content.</summary>
    public static IReadOnlyDictionary<string, string> Generate()
    {
        var root = CliOrchestrator.BuildRootCommand(_ => Task.FromResult(0));

        var commands = new List<(string[] Path, Command Cmd)>();
        Collect(root, Array.Empty<string>(), isRoot: true, commands);

        var pages = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{CliDir}/README.md"] = BuildIndex(commands),
        };
        foreach (var (path, cmd) in commands)
            pages[$"{CliDir}/{Slug(path)}.md"] = BuildPage(path, cmd);

        return pages;
    }

    private static void Collect(Command cmd, string[] path, bool isRoot,
        List<(string[], Command)> acc)
    {
        var here = isRoot ? path : path.Append(cmd.Name).ToArray();
        if (!isRoot) acc.Add((here, cmd));
        foreach (var sub in cmd.Subcommands.OrderBy(c => c.Name, StringComparer.Ordinal))
            Collect(sub, here, isRoot: false, acc);
    }

    private static string Slug(string[] path) => string.Join("-", path);

    private static string BuildIndex(List<(string[] Path, Command Cmd)> commands)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# CLI Reference");
        sb.AppendLine();
        sb.AppendLine("Command-line interface for the ETL-SQL engine. One page per command, generated from");
        sb.AppendLine("the command definitions so they stay in sync with the code.");
        sb.AppendLine();
        sb.AppendLine("## Commands");
        sb.AppendLine();
        sb.AppendLine("| Command | Description |");
        sb.AppendLine("| :--- | :--- |");
        foreach (var (path, cmd) in commands.OrderBy(c => Slug(c.Path), StringComparer.Ordinal))
            sb.AppendLine($"| [`etl-sql {string.Join(" ", path)}`]({Slug(path)}.md) | {Clean(cmd.Description)} |");
        sb.AppendLine();
        AppendGeneratedMarker(sb);
        return sb.ToString();
    }

    private static string BuildPage(string[] path, Command cmd)
    {
        var full = string.Join(" ", path);
        var sb = new StringBuilder();
        sb.AppendLine($"# etl-sql {full}");
        sb.AppendLine();
        sb.AppendLine(Clean(cmd.Description).Length == 0 ? "_No description._" : Clean(cmd.Description));
        sb.AppendLine();

        var args = cmd.Arguments.ToList();
        var opts = cmd.Options.Where(o => !IsBuiltIn(o)).ToList();
        var subs = cmd.Subcommands.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();

        sb.AppendLine("## Synopsis");
        sb.AppendLine();
        sb.AppendLine("```text");
        var synopsis = new StringBuilder($"etl-sql {full}");
        if (subs.Count > 0) synopsis.Append(" <subcommand>");
        foreach (var a in args)
            synopsis.Append(IsRequired(a) ? $" <{a.Name}>" : $" [{a.Name}]");
        if (opts.Count > 0) synopsis.Append(" [options]");
        sb.AppendLine(synopsis.ToString());
        sb.AppendLine("```");
        sb.AppendLine();

        if (args.Count > 0)
        {
            sb.AppendLine("## Arguments");
            sb.AppendLine();
            sb.AppendLine("| Argument | Required | Description |");
            sb.AppendLine("| :--- | :--- | :--- |");
            foreach (var a in args)
                sb.AppendLine($"| `{a.Name}` | {(IsRequired(a) ? "yes" : "no")} | {Clean(a.Description)} |");
            sb.AppendLine();
        }

        if (opts.Count > 0)
        {
            sb.AppendLine("## Options");
            sb.AppendLine();
            sb.AppendLine("| Option | Description |");
            sb.AppendLine("| :--- | :--- |");
            foreach (var o in opts.OrderBy(o => o.Name, StringComparer.Ordinal))
                sb.AppendLine($"| `{Names(o)}` | {Clean(o.Description)} |");
            sb.AppendLine();
        }

        if (subs.Count > 0)
        {
            sb.AppendLine("## Subcommands");
            sb.AppendLine();
            sb.AppendLine("| Subcommand | Description |");
            sb.AppendLine("| :--- | :--- |");
            foreach (var s in subs)
                sb.AppendLine($"| [`{s.Name}`]({Slug(path.Append(s.Name).ToArray())}.md) | {Clean(s.Description)} |");
            sb.AppendLine();
        }

        AppendGeneratedMarker(sb);
        return sb.ToString();
    }

    private static void AppendGeneratedMarker(StringBuilder sb)
    {
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("<!-- Generated from src/ETL-SQL.App/App/CliOrchestrator.cs by CliReferenceGenerator.");
        sb.AppendLine("     Do not edit by hand; regenerate with ETLSQL_REGEN_CLI_DOCS=1 dotnet test --filter CliReferenceTests. -->");
    }

    private static string Names(Option o)
    {
        var names = new List<string> { o.Name };
        names.AddRange(o.Aliases);
        return string.Join(", ", names.Distinct());
    }

    private static bool IsBuiltIn(Option o) =>
        o.Name is "--help" or "--version" || o.Aliases.Contains("-h") || o.Aliases.Contains("-?");

    private static bool IsRequired(Argument a) => a.Arity.MinimumNumberOfValues > 0;

    private static string Clean(string? s) =>
        (s ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|").Trim();
}
