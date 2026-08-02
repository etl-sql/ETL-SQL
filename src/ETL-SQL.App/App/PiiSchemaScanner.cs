using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Governance;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.App;

internal sealed record PiiScanFinding(
    string Source,
    string Table,
    string Column,
    string SuggestedTag,
    string SuggestedValue,
    decimal Confidence,
    string EvidenceKind,
    string Evidence,
    string Reason,
    int Line);

internal sealed record PiiScanReport(
    string DefinitionVersion,
    DateTimeOffset EvaluatedAtUtc,
    int SchemaCount,
    int ColumnCount,
    IReadOnlyList<PiiScanFinding> Findings,
    IReadOnlyList<StewardshipScore> Scores,
    IReadOnlyList<StewardshipGap> Gaps);

/// <summary>
/// Schema-only protected-data scanner. It deliberately inspects names and connector metadata only;
/// row values and resolved credentials never enter the report model.
/// </summary>
internal static class PiiSchemaScanner
{
    internal const string DefinitionVersion = "1.0";
    private const int MaxFiles = 100;
    private const int MaxDepth = 5;
    private static readonly Regex SharedAliasPattern = new("^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);
    private static readonly IReadOnlyDictionary<string, string> FileConnectors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"] = "FLATFILE", [".tsv"] = "FLATFILE", [".txt"] = "FLATFILE",
            [".json"] = "JSON", [".xml"] = "XML", [".parquet"] = "PARQUET",
            [".xlsx"] = "EXCEL", [".xls"] = "EXCEL", [".avro"] = "AVRO"
        };

    internal static async Task<int> RunAsync(
        CliContext context,
        ILogger logger,
        IConnectorRegistry registry,
        CancellationToken cancellationToken)
    {
        if (!context.ScanPii)
        {
            logger.WriteLine("No scanner selected. Use 'etl-sql scan --pii [source]'.", ConsoleColor.Red);
            return 2;
        }

        try
        {
            var evaluator = Program.ServiceProvider.GetRequiredService<Evaluator>();
            var source = string.IsNullOrWhiteSpace(context.ScanSource)
                ? Environment.CurrentDirectory
                : context.ScanSource.Trim();
            var schemas = source.StartsWith("SHARED:", StringComparison.OrdinalIgnoreCase)
                ? [await ReadSharedSchemaAsync(source, context.ScanTable, evaluator, cancellationToken)]
                : await ReadLocalSchemasAsync(source, evaluator, registry, cancellationToken);
            var policy = LoadPolicy(source);
            var report = BuildReport(schemas, policy);
            Write(report, context.IsJsonMode, logger);
            return 0;
        }
        catch (Exception ex)
        {
            logger.WriteLine(SecretRedactor.Redact(ex.Message) ?? "PII schema scan failed.", ConsoleColor.Red);
            return 1;
        }
    }

    internal static PiiScanReport BuildReport(
        IReadOnlyList<(string Source, string Table, IReadOnlyList<string> Columns, int Line)> schemas,
        WorkspacePolicyDocument? policy,
        DateTimeOffset? evaluatedAtUtc = null)
    {
        var findings = new Dictionary<string, PiiScanFinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in schemas)
        {
            foreach (var column in schema.Columns.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var entry = new LineageHistoryEntry(
                    0, DateTime.UtcNow, null, null, schema.Table, column, [], "SCAN",
                    new Dictionary<string, string>(), schema.Source, schema.Line);
                foreach (var suggestion in LineageProtectedData.SuggestFromHistory([entry]))
                {
                    Add(new(schema.Source, schema.Table, column, suggestion.SuggestedTag,
                        suggestion.SuggestedValue, suggestion.Confidence, suggestion.EvidenceKind,
                        suggestion.Evidence, suggestion.Reason, schema.Line));
                }

                if (policy != null)
                {
                    var qualifiedName = $"{schema.Table}.{column}";
                    foreach (var pattern in policy.ProtectedDataPatterns.Where(p =>
                        p.Scopes.Contains("COLUMN", StringComparer.OrdinalIgnoreCase)
                        && !p.Exclude.Any(e => WildcardMatches(qualifiedName, e))
                        && Regex.IsMatch(qualifiedName, p.Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                            TimeSpan.FromMilliseconds(250))))
                    {
                        Add(new(schema.Source, schema.Table, column, "@classification", pattern.Classification,
                            1m, "WorkspacePolicy", pattern.Name,
                            $"Workspace policy pattern '{pattern.Name}' matched the schema name.", schema.Line));
                    }

                    foreach (var required in policy.RequiredTags.Where(r =>
                        r.Scopes.Contains("COLUMN", StringComparer.OrdinalIgnoreCase)
                        && !r.Exclude.Any(e => WildcardMatches(qualifiedName, e))))
                    {
                        Add(new(schema.Source, schema.Table, column, required.Tag, "<required>",
                            1m, "WorkspacePolicy", required.Tag,
                            $"Workspace policy requires {required.Tag} on column metadata.", schema.Line));
                    }
                }
            }
        }

        var evaluatedAt = evaluatedAtUtc ?? DateTimeOffset.UtcNow;
        var stewardship = StewardshipScoring.Evaluate(
            schemas.SelectMany(schema => schema.Columns.Select(column => new StewardshipAsset(
                null, schema.Table, column, new Dictionary<string, string>(), schema.Source, schema.Line))),
            policy, evaluatedAt);
        return new(DefinitionVersion, evaluatedAt, schemas.Count,
            schemas.Sum(s => s.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count()),
            findings.Values.OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Table, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Column, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(f => f.Confidence).ThenBy(f => f.SuggestedTag, StringComparer.OrdinalIgnoreCase).ToList(),
            stewardship.Scores, stewardship.Gaps);

        void Add(PiiScanFinding finding)
        {
            var key = $"{finding.Source}\u001f{finding.Table}\u001f{finding.Column}\u001f{finding.SuggestedTag}\u001f{finding.SuggestedValue}";
            if (!findings.TryGetValue(key, out var existing) || finding.Confidence > existing.Confidence)
                findings[key] = finding;
        }
    }

    private static async Task<List<(string Source, string Table, IReadOnlyList<string> Columns, int Line)>> ReadLocalSchemasAsync(
        string source, Evaluator evaluator, IConnectorRegistry registry, CancellationToken cancellationToken)
    {
        var resolved = evaluator.ResolvePath(source);
        evaluator.SecurityService.ValidatePath(resolved);
        var files = File.Exists(resolved) ? [resolved] : EnumerateSupportedFiles(resolved);
        if (files.Count == 0)
            throw new InvalidOperationException($"No supported schema files were found under '{resolved}'.");

        var schemas = new List<(string, string, IReadOnlyList<string>, int)>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(file);
            if (!FileConnectors.TryGetValue(extension, out var connectorName)) continue;
            var connector = registry.GetConnector(connectorName)
                ?? throw new InvalidOperationException($"Connector '{connectorName}' is not available for '{extension}' files.");
            await using var dataSource = connector.CreateDataSource(evaluator, file);
            var columns = (await dataSource.GetColumnsAsync(cancellationToken)).ToList();
            schemas.Add((file, Path.GetFileName(file), columns, 1));
        }
        return schemas;
    }

    private static async Task<(string Source, string Table, IReadOnlyList<string> Columns, int Line)> ReadSharedSchemaAsync(
        string source, string? table, Evaluator evaluator, CancellationToken cancellationToken)
    {
        var alias = source["SHARED:".Length..].Trim();
        if (!SharedAliasPattern.IsMatch(alias))
            throw new InvalidOperationException("The SHARED connection alias contains unsupported characters.");
        if (string.IsNullOrWhiteSpace(table))
            throw new InvalidOperationException("Database scans require --table so only the requested schema is accessed.");

        var configuration = Program.ServiceProvider.GetService<IConfiguration>();
        var catalog = ConnectionCatalogProviderFactory.Create(new ConnectionCatalogOptions
        {
            Provider = configuration?["Governance:ConnectionCatalog:Provider"],
            LocalRoot = configuration?["Governance:ConnectionCatalog:LocalRoot"]
        }) ?? throw new InvalidOperationException("No shared connection catalog is configured.");
        var definition = await catalog.ResolveAsync(alias, evaluator.ExecutionIdentity, cancellationToken);
        var connectionName = $"__pii_scan_{Guid.NewGuid():N}";
        var script = $"CREATE CONNECTION {connectionName} AS {definition.ConnectorType}('SHARED:{alias}');";
        await evaluator.Evaluate(new Parser(new Lexer(script).Tokenize(), script).Parse());
        if (!evaluator.Connections.TryGetValue(connectionName, out var connection))
            throw new InvalidOperationException("The cataloged database connection could not be opened for schema inspection.");

        IDataSource? scoped = null;
        try
        {
            scoped = connection.WithTable(table.Trim());
            var columns = (await scoped.GetColumnsAsync(cancellationToken)).ToList();
            return ($"SHARED:{alias}", table.Trim(), columns, 0);
        }
        finally
        {
            evaluator.Connections.Remove(connectionName);
            if (scoped != null && !ReferenceEquals(scoped, connection)) await scoped.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private static List<string> EnumerateSupportedFiles(string root)
    {
        if (!Directory.Exists(root)) throw new FileNotFoundException($"Scan source '{root}' does not exist.");
        var files = new List<string>();
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();
            foreach (var file in Directory.EnumerateFiles(directory).Where(f => FileConnectors.ContainsKey(Path.GetExtension(f))))
            {
                files.Add(file);
                if (files.Count > MaxFiles)
                    throw new InvalidOperationException($"Scan exceeds the {MaxFiles}-file safety limit; choose a narrower source directory.");
            }
            if (depth >= MaxDepth) continue;
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    queue.Enqueue((child, depth + 1));
            }
        }
        return files;
    }

    private static WorkspacePolicyDocument? LoadPolicy(string source)
    {
        if (source.StartsWith("SHARED:", StringComparison.OrdinalIgnoreCase))
            source = Environment.CurrentDirectory;
        var full = Path.GetFullPath(source);
        var start = Directory.Exists(full) ? full : Path.GetDirectoryName(full)!;
        var path = WorkspacePolicyLoader.Find(start);
        if (path == null) return null;
        var result = WorkspacePolicyLoader.Load(path);
        if (!result.IsValid)
            throw new InvalidOperationException($"Workspace policy is invalid: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
        return result.Policy;
    }

    private static bool WildcardMatches(string value, string pattern)
    {
        var expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250));
    }

    private static void Write(PiiScanReport report, bool json, ILogger logger)
    {
        if (json)
        {
            logger.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        logger.WriteLine($"PII schema scan {report.DefinitionVersion}: {report.SchemaCount} schema(s), {report.ColumnCount} column(s), {report.Findings.Count} suggestion(s).",
            report.Findings.Count == 0 ? ConsoleColor.Green : ConsoleColor.Yellow);
        foreach (var score in report.Scores.Where(s => s.ScopeType == "GLOBAL"))
            logger.WriteLine($"score {score.Component}: {score.Numerator}/{score.Denominator} ({score.Percentage:0.##}%), weight={score.Weight:0.##}, definition={score.DefinitionVersion}");
        foreach (var finding in report.Findings)
            logger.WriteLine($"{finding.Source}:{finding.Line}  {finding.Table}.{finding.Column}  {finding.SuggestedTag}={finding.SuggestedValue}  ({finding.Reason})");
    }
}
