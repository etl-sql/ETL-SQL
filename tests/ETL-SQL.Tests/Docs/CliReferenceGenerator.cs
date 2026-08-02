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

    private static readonly Dictionary<string, string> Examples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["run"] = @"```bash
# Simplest run
ETL-SQL run nightly_load.etlsql

# With perf metrics and logging
ETL-SQL run nightly_load.etlsql --perf --log C:\Logs\etlsql\

# Inject runtime parameters
ETL-SQL run monthly_report.etlsql --var @env=PROD --var @month=2026-03

# Headless with JSON output for automation
ETL-SQL run nightly_load.etlsql --json --silent

# Counts-only quality summary plus a versioned CI evidence artifact
ETL-SQL run nightly_load.etlsql --quality-summary --output-json artifacts/quality.json

# Persistent session — connections survive between runs
ETL-SQL run setup_connections.etlsql --session prod-session
ETL-SQL run nightly_load.etlsql --session prod-session

# Live progress tree in the terminal
ETL-SQL run heavy_transform.etlsql --progress --perf
```",

        ["scan"] = @"```bash
# Inspect one local file without reading or printing row values
ETL-SQL scan ./data/customers.parquet --pii

# Inspect supported schema files under a directory and emit the versioned JSON contract
ETL-SQL scan ./data --pii --json

# Inspect one table through a credential-safe shared connection-catalog alias
ETL-SQL scan SHARED:warehouse --pii --table sales.customers --json
```

The scanner reads schema names only. It supports CSV/TSV/text, JSON, XML, Parquet, Excel, and Avro files, recurses at most five directory levels, and stops at 100 files. Database scans require a configured `SHARED:` alias and an explicit `--table`; raw connection strings and credentials are not accepted. Suggestions and transparent component scores use the nearest `etlsql-policy.json`.",

        ["ui-edit"] = @"```bash
# Open the IDE with a file pre-loaded
ETL-SQL ui edit nightly_load.etlsql

# Open the IDE with a persistent session
ETL-SQL ui edit --session dev-workspace
```",

        ["encrypt"] = @"```bash
# Encrypt a connection string
ETL-SQL encrypt ""Server=prod-sql;Database=DW;User Id=sa;Password=S3cr3t!"" --pass MyMasterKey

# Output:
# Encrypted: ENC:U2FsdGVkX1+...

# Use in a script:
# CREATE CONNECTION prod AS MSSQL('ENC:U2FsdGVkX1+...', TRUSTED_CONNECTION=FALSE);
```

> [!IMPORTANT]
> The master password must be the same each time you run scripts referencing `ENC:` strings. Pass it at runtime with `--pass MyMasterKey` or set `USE PASSWORD = '...';` at the top of your script.",

        ["session-clear"] = @"```bash
ETL-SQL session clear dev-workspace
```",

        ["gen-script"] = @"```bash
ETL-SQL gen-script --schema ./specs/customer_feed.json --output ./scripts/load_customers.etlsql
```

Generated scripts include schema gates, casting, lineage tags, AI review/evidence comments when present, validation issue summaries, and optional quarantine scaffolding. Review the JSON, complete the generated `#staging` extraction block, and test with real vendor files before production use. See [Spec-Driven Development](../../spec-import/spec-driven-development.md) and Cookbook recipe 25 for the full workflow.",

        ["extract-spec"] = @"```bash
ETL-SQL extract-spec --input ./specs/vendor_api_spec.pdf --output ./specs/trimmed_schema_spec.pdf
```"
    };


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
        sb.AppendLine("## Exit Codes");
        sb.AppendLine();
        sb.AppendLine("| Code | Meaning |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine("| `0` | Script completed successfully |");
        sb.AppendLine("| `1` | Parse error, lint error, or runtime exception |");
        sb.AppendLine();
        sb.AppendLine("Exit codes are suitable for use in CI/CD pipeline gating.");
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

        var slug = Slug(path);
        if (Examples.TryGetValue(slug, out var examples))
        {
            sb.AppendLine("## Examples");
            sb.AppendLine();
            sb.AppendLine(examples);
            sb.AppendLine();
        }

        sb.AppendLine("## References");
        sb.AppendLine();
        sb.AppendLine("- [CLI Reference](README.md)");
        sb.AppendLine("- [Syntax Index](../../syntax-index.md)");
        sb.AppendLine();

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
