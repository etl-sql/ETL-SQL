using System.Text.Json;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Models;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Computes the cross-job dependency chain for Portal's Orchestrator API, resolving normalized
/// <c>RUN SCRIPT '&lt;targetPath&gt;';</c> wrappers, <c>orch://</c> bundle entry scripts, nested
/// <c>RUN SCRIPT</c> calls, table read/write overlap, triggers, and explicit job dependencies.
/// </summary>
internal sealed class JobDependencyGraphBuilder(OrchestratorProxyService proxy)
{
    private const int MaxScriptDepth = 16;

    private readonly Dictionary<string, string?> _scriptCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _latestBundleVersionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Bundle, int Version), IReadOnlyList<BundleFileInfo>> _bundleFilesCache = new();
    private readonly Dictionary<(string Bundle, int Version), IReadOnlyList<BundleDependencyInfo>> _bundleDepsCache = new();

    public async Task<JobDependencyChainDto?> BuildAsync(string jobName, CancellationToken ct = default)
    {
        var allJobs = await proxy.GetJobsAsync();
        var currentJob = allJobs.FirstOrDefault(j => j.Name.Equals(jobName, StringComparison.OrdinalIgnoreCase));
        if (currentJob == null) return null;

        var analyses = new Dictionary<string, JobAnalysis>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in allJobs)
            analyses[job.Name] = await AnalyzeJobAsync(job, ct);

        var upstreamJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var downstreamJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var edgeSet = new HashSet<(string From, string To, string Type)>();
        var edges = new List<JobDependencyEdgeDto>();

        void AddEdge(string from, string to, string type, string detail)
        {
            if (edgeSet.Add((from.ToUpperInvariant(), to.ToUpperInvariant(), type.ToUpperInvariant())))
                edges.Add(new JobDependencyEdgeDto(from, to, type, detail));
        }

        var current = analyses[currentJob.Name];
        foreach (var other in allJobs)
        {
            if (other.Name.Equals(currentJob.Name, StringComparison.OrdinalIgnoreCase)) continue;
            var otherAnalysis = analyses[other.Name];

            // Upstream providers: other writes a table current reads, triggers current, or current declares dependency on other.
            var matchingUp = otherAnalysis.Outputs.Intersect(current.Inputs, StringComparer.OrdinalIgnoreCase).ToList();
            if (matchingUp.Count > 0)
            {
                upstreamJobs.Add(other.Name);
                AddEdge(other.Name, currentJob.Name, "Data", $"Writes {string.Join(", ", matchingUp)}");
            }
            if (otherAnalysis.Triggers.Contains(currentJob.Name)
                || (!string.IsNullOrWhiteSpace(currentJob.TargetPath)
                    && otherAnalysis.ReferencedTargets.Contains(NormalizePathKey(currentJob.TargetPath))))
            {
                upstreamJobs.Add(other.Name);
                AddEdge(other.Name, currentJob.Name, "Trigger", "Direct execution trigger");
            }
            if (current.ExplicitDependencies.Contains(other.Name))
            {
                upstreamJobs.Add(other.Name);
                AddEdge(other.Name, currentJob.Name, "Dependency", "Explicit job dependency");
            }

            // Downstream consumers: other reads a table current writes, current triggers other, or other declares dependency on current.
            var matchingDown = current.Outputs.Intersect(otherAnalysis.Inputs, StringComparer.OrdinalIgnoreCase).ToList();
            if (matchingDown.Count > 0)
            {
                downstreamJobs.Add(other.Name);
                AddEdge(currentJob.Name, other.Name, "Data", $"Reads {string.Join(", ", matchingDown)}");
            }
            if (current.Triggers.Contains(other.Name)
                || (!string.IsNullOrWhiteSpace(other.TargetPath)
                    && current.ReferencedTargets.Contains(NormalizePathKey(other.TargetPath))))
            {
                downstreamJobs.Add(other.Name);
                AddEdge(currentJob.Name, other.Name, "Trigger", "Direct execution trigger");
            }
            if (otherAnalysis.ExplicitDependencies.Contains(currentJob.Name))
            {
                downstreamJobs.Add(other.Name);
                AddEdge(currentJob.Name, other.Name, "Dependency", "Explicit job dependency");
            }
        }

        var neighborNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentJob.Name };
        foreach (var u in upstreamJobs) neighborNames.Add(u);
        foreach (var d in downstreamJobs) neighborNames.Add(d);

        var nodes = allJobs
            .Where(j => neighborNames.Contains(j.Name))
            .Select(j => new JobDependencyNodeDto(
                j.Name,
                j.Name,
                j.DisplayName,
                j.IsEnabled,
                j.LastRun,
                j.NextRun,
                j.Name.Equals(currentJob.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new JobDependencyChainDto(
            currentJob.Name,
            nodes,
            edges,
            upstreamJobs.ToList(),
            downstreamJobs.ToList(),
            []);
    }

    private async Task<JobAnalysis> AnalyzeJobAsync(JobDefinitionDto job, CancellationToken ct)
    {
        var analysis = new JobAnalysis();
        ParseExplicitOptionsDependencies(job.Options, analysis.ExplicitDependencies);

        var visitedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(job.Script))
            await AnalyzeScriptTextAsync(job.Script, currentBundleUri: null, analysis, visitedTargets, depth: 0, ct);

        if (job.JobType == JobTargetKind.Script && !string.IsNullOrWhiteSpace(job.TargetPath))
            await ResolveAndAnalyzeTargetAsync(job.TargetPath, currentBundleUri: null, analysis, visitedTargets, depth: 0, ct);

        return analysis;
    }

    private async Task ResolveAndAnalyzeTargetAsync(
        string targetPath,
        BundleUri? currentBundleUri,
        JobAnalysis analysis,
        HashSet<string> visitedTargets,
        int depth,
        CancellationToken ct,
        string? currentScriptPath = null)
    {
        if (depth >= MaxScriptDepth || string.IsNullOrWhiteSpace(targetPath))
            return;

        var effectiveTarget = targetPath.Trim();
        if (currentBundleUri != null && !effectiveTarget.StartsWith("orch://", StringComparison.OrdinalIgnoreCase))
            effectiveTarget = BundleUri.CombineRelative(currentBundleUri, effectiveTarget);
        else if (currentScriptPath != null && !effectiveTarget.StartsWith("orch://", StringComparison.OrdinalIgnoreCase))
            effectiveTarget = ResolveRelativeScriptPath(currentScriptPath, effectiveTarget);

        analysis.ReferencedTargets.Add(effectiveTarget);
        analysis.ReferencedTargets.Add(NormalizePathKey(effectiveTarget));

        if (BundleUri.TryParse(effectiveTarget, out var bundleUri) && bundleUri != null)
        {
            await ResolveBundleTargetAsync(bundleUri, analysis, visitedTargets, depth, ct);
            return;
        }

        var normalizedKey = $"file:{NormalizePathKey(effectiveTarget)}";
        if (!visitedTargets.Add(normalizedKey))
            return;

        var content = await GetScriptCachedAsync(effectiveTarget, ct);
        if (!string.IsNullOrWhiteSpace(content))
            await AnalyzeScriptTextAsync(content, currentBundleUri: null, analysis, visitedTargets, depth + 1, ct,
                currentScriptPath: effectiveTarget);
    }

    private async Task ResolveBundleTargetAsync(
        BundleUri bundleUri,
        JobAnalysis analysis,
        HashSet<string> visitedTargets,
        int depth,
        CancellationToken ct)
    {
        var version = bundleUri.Version ?? await GetLatestBundleVersionAsync(bundleUri.BundleName, ct);
        if (!version.HasValue)
            return;

        var files = await GetBundleFilesCachedAsync(bundleUri.BundleName, version.Value, ct);
        if (files.Count == 0)
            return;

        var normalizedEntry = NormalizePathKey(bundleUri.Path);
        var visitKey = $"bundle:{bundleUri.BundleName}@{version.Value}/{normalizedEntry}";
        if (!visitedTargets.Add(visitKey))
            return;

        var entryFile = files.FirstOrDefault(f => NormalizePathKey(f.VirtualPath).Equals(normalizedEntry, StringComparison.OrdinalIgnoreCase));
        if (entryFile != null && !string.IsNullOrWhiteSpace(entryFile.Content))
        {
            var resolvedUri = new BundleUri(bundleUri.BundleName, version.Value, normalizedEntry);
            await AnalyzeScriptTextAsync(entryFile.Content, resolvedUri, analysis, visitedTargets, depth + 1, ct);
        }

        // Follow declared bundle dependencies (FromPath -> ToPath) in case targets are invoked dynamically.
        var bundleDeps = await GetBundleDependenciesCachedAsync(bundleUri.BundleName, version.Value, ct);
        foreach (var dep in bundleDeps)
        {
            if (!NormalizePathKey(dep.FromPath).Equals(normalizedEntry, StringComparison.OrdinalIgnoreCase))
                continue;
            var depPath = NormalizePathKey(dep.ToPath);
            var depUri = new BundleUri(bundleUri.BundleName, version.Value, depPath);
            await ResolveBundleTargetAsync(depUri, analysis, visitedTargets, depth + 1, ct);
        }
    }

    private async Task AnalyzeScriptTextAsync(
        string scriptText,
        BundleUri? currentBundleUri,
        JobAnalysis analysis,
        HashSet<string> visitedTargets,
        int depth,
        CancellationToken ct,
        string? currentScriptPath = null)
    {
        Script? script;
        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            script = new CoreParser(tokens, scriptText).Parse();
        }
        catch
        {
            return;
        }

        var nestedTargets = new List<string>();
        WalkStatements(script.Statements, analysis, nestedTargets);

        foreach (var nested in nestedTargets)
            await ResolveAndAnalyzeTargetAsync(nested, currentBundleUri, analysis, visitedTargets, depth + 1, ct,
                currentScriptPath);
    }

    private static void WalkStatements(
        IReadOnlyList<Statement> statements,
        JobAnalysis analysis,
        List<string> nestedTargets)
    {
        foreach (var stmt in statements)
        {
            var cteNames = stmt.Ctes != null && stmt.Ctes.Count > 0
                ? new HashSet<string>(stmt.Ctes.Select(c => c.Name), StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var src in stmt.GetSourceTables())
                AddTableRef(analysis.Inputs, src, cteNames);

            switch (stmt)
            {
                case SelectStatement sel when sel.IntoTable != null:
                    AddTableRef(analysis.Outputs, sel.IntoTable.FullyQualifiedName, cteNames);
                    break;
                case SetOperationStatement setOp when setOp.GetCreatedTable() is { } created:
                    AddTableRef(analysis.Outputs, created, cteNames);
                    break;
                case InsertStatement ins:
                    AddTableRef(analysis.Outputs, ins.TargetTable.FullyQualifiedName, cteNames);
                    break;
                case UpdateStatement upd:
                    AddTableRef(analysis.Outputs, upd.TargetTable.FullyQualifiedName, cteNames);
                    break;
                case MergeStatement mrg:
                    AddTableRef(analysis.Outputs, mrg.TargetTable.FullyQualifiedName, cteNames);
                    AddTableRef(analysis.Inputs, mrg.SourceTable.FullyQualifiedName, cteNames);
                    break;
                case CreateTableStatement ctStmt:
                    AddTableRef(analysis.Outputs, ctStmt.TargetTable.FullyQualifiedName, cteNames);
                    break;
                case BulkInsertStatement biStmt:
                    AddTableRef(analysis.Outputs, biStmt.TargetTable.FullyQualifiedName, cteNames);
                    break;
                case TransformStatement trStmt:
                    AddTableRef(analysis.Outputs, (trStmt.TargetTable ?? trStmt.SourceTable)?.FullyQualifiedName, cteNames);
                    break;
                case TriggerJobStatement trigStmt:
                    if (!string.IsNullOrWhiteSpace(trigStmt.Name))
                        analysis.Triggers.Add(trigStmt.Name);
                    break;
                case AssertJobStatement assertJob:
                    if (!string.IsNullOrWhiteSpace(assertJob.JobName))
                        analysis.ExplicitDependencies.Add(assertJob.JobName);
                    break;
                case RunScriptStatement runStmt when runStmt.PathExpression is LiteralExpression { Value: string target }
                    && !string.IsNullOrWhiteSpace(target):
                    nestedTargets.Add(target);
                    break;
            }

            foreach (var child in NestedStatementLists(stmt))
                WalkStatements(child, analysis, nestedTargets);
        }
    }

    private static IEnumerable<IReadOnlyList<Statement>> NestedStatementLists(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement b:
                yield return b.Statements;
                break;
            case IfStatement i:
                yield return AsList(i.IfBody);
                if (i.ElseIfClauses != null)
                    foreach (var elif in i.ElseIfClauses) yield return AsList(elif.Body);
                if (i.ElseBody != null) yield return AsList(i.ElseBody);
                break;
            case WhileStatement w:
                yield return AsList(w.Body);
                break;
            case ForStatement f:
                yield return AsList(f.Body);
                break;
            case ForeachStatement fe:
                yield return AsList(fe.Body);
                break;
            case ParallelStatement p:
                yield return p.Body.Statements;
                break;
            case ParallelForStatement pf:
                yield return AsList(pf.Body);
                break;
            case TryCatchStatement tc:
                yield return AsList(tc.TryBody);
                yield return AsList(tc.CatchBody);
                break;
        }
    }

    private static IReadOnlyList<Statement> AsList(Statement statement) =>
        statement is BlockStatement b ? b.Statements : [statement];

    private static void AddTableRef(HashSet<string> target, string? rawName, HashSet<string>? cteNames)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return;
        var trimmed = rawName.Trim();
        if (trimmed.StartsWith('#') || trimmed.StartsWith('@')
            || trimmed.Equals("SUBQUERY", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("DUAL", StringComparison.OrdinalIgnoreCase)
            || (cteNames != null && cteNames.Contains(trimmed)))
            return;
        target.Add(trimmed);
    }

    private static void ParseExplicitOptionsDependencies(string? optionsJson, HashSet<string> target)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Name.Equals("DependsOn", StringComparison.OrdinalIgnoreCase)
                    && !prop.Name.Equals("Dependencies", StringComparison.OrdinalIgnoreCase)
                    && !prop.Name.Equals("UpstreamJobs", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var raw = prop.Value.GetString();
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    foreach (var part in raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (!string.IsNullOrWhiteSpace(part)) target.Add(part);
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in prop.Value.EnumerateArray())
                    {
                        if (elem.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(elem.GetString()))
                            target.Add(elem.GetString()!.Trim());
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore non-JSON or malformed legacy option strings.
        }
    }

    private static string NormalizePathKey(string path) => path.Trim().Replace('\\', '/').TrimStart('/');

    private static string ResolveRelativeScriptPath(string parentScript, string target)
    {
        // These paths belong to the remote host. Recognize both Windows and Unix roots
        // without resolving them against the Portal's own operating system or directory.
        var path = target.Replace('\\', '/');
        if (!path.StartsWith('/') && !(path.Length > 1 && path[1] == ':'))
        {
            var parent = parentScript.Replace('\\', '/');
            var separator = parent.LastIndexOf('/');
            if (separator >= 0) path = parent[..(separator + 1)] + path;
        }

        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == ".." && segments.Count > 0 && segments[^1] != ".." && !segments[^1].EndsWith(':'))
                segments.RemoveAt(segments.Count - 1);
            else
                segments.Add(segment);
        }
        var root = path.StartsWith("//", StringComparison.Ordinal) ? "//" : path.StartsWith('/') ? "/" : "";
        return root + string.Join('/', segments);
    }

    private async Task<string?> GetScriptCachedAsync(string path, CancellationToken ct)
    {
        var key = NormalizePathKey(path);
        if (_scriptCache.TryGetValue(key, out var cached)) return cached;
        var content = await proxy.GetScriptContentAsync(path, ct);
        _scriptCache[key] = content;
        return content;
    }

    private async Task<int?> GetLatestBundleVersionAsync(string bundleName, CancellationToken ct)
    {
        if (_latestBundleVersionCache.TryGetValue(bundleName, out var cached)) return cached;
        var versions = await proxy.GetBundleVersionsAsync(bundleName, ct);
        var latest = versions.Count > 0 ? versions.Max(v => v.Version) : (int?)null;
        _latestBundleVersionCache[bundleName] = latest;
        return latest;
    }

    private async Task<IReadOnlyList<BundleFileInfo>> GetBundleFilesCachedAsync(string bundleName, int version, CancellationToken ct)
    {
        var key = (bundleName.ToUpperInvariant(), version);
        if (_bundleFilesCache.TryGetValue(key, out var cached)) return cached;
        var files = await proxy.GetBundleFilesAsync(bundleName, version, ct);
        _bundleFilesCache[key] = files;
        return files;
    }

    private async Task<IReadOnlyList<BundleDependencyInfo>> GetBundleDependenciesCachedAsync(string bundleName, int version, CancellationToken ct)
    {
        var key = (bundleName.ToUpperInvariant(), version);
        if (_bundleDepsCache.TryGetValue(key, out var cached)) return cached;
        var deps = await proxy.GetBundleDependenciesAsync(bundleName, version, ct);
        _bundleDepsCache[key] = deps;
        return deps;
    }

    private sealed class JobAnalysis
    {
        public HashSet<string> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Outputs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Triggers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExplicitDependencies { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReferencedTargets { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
