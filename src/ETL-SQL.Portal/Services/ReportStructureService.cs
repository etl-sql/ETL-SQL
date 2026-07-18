using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Portal.Data;
using ETL_SQL.Portal.Models;
using Microsoft.EntityFrameworkCore;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Portal.Services;

/// <summary>
/// Thrown by <see cref="ReportStructureService.BuildAsync"/> when the report script cannot be
/// parsed. Controllers map this to an HTTP 422 (unprocessable entity) response; the message is the
/// underlying parser error.
/// </summary>
public sealed class ReportStructureParseException(string message, Exception inner)
    : Exception(message, inner);

/// <summary>
/// Builds the report structure/lineage DAG (nodes + edges) from a report script, including
/// best-effort cross-script enrichment from the dataset registry and the persisted lineage catalog.
/// Extracted from <c>ReportsController.GetStructure</c> so the controller only handles report
/// lookup, authorization, script resolution, and HTTP mapping.
/// </summary>
public sealed class ReportStructureService(PortalDbContext db, ILineageCatalogStore lineageCatalog)
{
    /// <summary>
    /// Parse <paramref name="scriptText"/> and build the visual/dataset/table DAG for report
    /// <paramref name="reportId"/>. Throws <see cref="ReportStructureParseException"/> if the script
    /// cannot be parsed. Catalog/dataset lineage enrichment is best-effort and never fails the build.
    /// </summary>
    public async Task<DagDto> BuildAsync(string scriptText, int reportId)
    {
        List<DagNodeDto> nodes = [];
        List<DagEdgeDto> edges = [];

        try
        {
            var tokens = new Lexer(scriptText).Tokenize();
            var script = new CoreParser(tokens, scriptText).Parse();

            // Pass 1 — collect all tables produced by SELECT INTO and CREATE DATASET
            var producers = new Dictionary<string, (List<string> Sources, bool HasGroupBy)>(StringComparer.OrdinalIgnoreCase);

            foreach (var stmt in script.Statements)
            {
                if (stmt is SelectStatement sel && sel.IntoTable is not null)
                {
                    producers[sel.IntoTable.TableName] = (
                        sel.GetSourceTables().ToList(),
                        sel.GroupBy?.Count > 0 || sel.GroupingSet is not null);
                }
                else if (stmt is CreateDatasetStatement ds)
                {
                    var selQuery = ds.SourceQuery as SelectStatement;
                    producers[ds.TempTableName] = (
                        ds.SourceQuery.GetSourceTables().ToList(),
                        selQuery?.GroupBy?.Count > 0 || selQuery?.GroupingSet is not null);
                }
            }

            // Pass 2 — walk backwards from each visual to find only relevant ancestors
            var relevant = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void WalkAncestors(string table)
            {
                if (!relevant.Add(table)) return;
                if (producers.TryGetValue(table, out var info))
                    foreach (var src in info.Sources) WalkAncestors(src);
            }

            var visuals = script.Statements.OfType<CreateVisualStatement>().ToList();
            var visualPages = BuildVisualPageMap(script);

            foreach (var vis in visuals)
            {
                if (vis.Source.TempTableName is string t)
                    WalkAncestors(t);
                else if (vis.Source.InlineSelect is Statement inl)
                    foreach (var src in inl.GetSourceTables()) WalkAncestors(src);
            }

            // Build nodes — datasets (green), temp/source tables (gray)
            var datasetNames = new HashSet<string>(
                script.Statements.OfType<CreateDatasetStatement>().Select(d => d.TempTableName),
                StringComparer.OrdinalIgnoreCase);

            var addedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string EnsureTableNode(string name)
            {
                var isDataset = datasetNames.Contains(name);
                var nodeId = isDataset ? $"ds:{name}" : $"table:{name}";
                if (addedNodes.Add(nodeId))
                    nodes.Add(new DagNodeDto(nodeId, name, isDataset ? "dataset" : "table", null));
                return nodeId;
            }

            // Add edges for all producer relationships (restricted to relevant ancestors)
            foreach (var kvp in producers)
            {
                var target = kvp.Key;
                if (!relevant.Contains(target)) continue;
                var (srcs, hasGroupBy) = kvp.Value;
                var edgeLabel = hasGroupBy ? "GROUP BY" : "SELECT";
                var targetId = EnsureTableNode(target);
                foreach (var src in srcs)
                {
                    var srcId = EnsureTableNode(src);
                    edges.Add(new DagEdgeDto(srcId, targetId, edgeLabel));
                }
            }

            // Add visual and page nodes, plus dataset→visual edges with axis labels
            foreach (var stmt in script.Statements)
            {
                if (stmt is CreatePageStatement page)
                {
                    var pageId = $"page:{page.Name}";
                    if (addedNodes.Add(pageId))
                        nodes.Add(new DagNodeDto(pageId, page.Name, "page", null));

                    foreach (var visualName in page.SlotMap.Values)
                    {
                        if (!visualPages.ContainsKey(visualName)) continue;
                        var visId = $"vis:{visualName}";
                        edges.Add(new DagEdgeDto(pageId, visId, null));
                    }
                }
                else if (stmt is CreateVisualStatement vis)
                {
                    var visId = $"vis:{vis.Name}";
                    var label = $"{vis.VisualType} · {vis.Name}";
                    visualPages.TryGetValue(vis.Name, out var pages);
                    if (addedNodes.Add(visId))
                        nodes.Add(new DagNodeDto(visId, label, "visual",
                            new
                            {
                                page = pages?.FirstOrDefault(),
                                pages = pages ?? [],
                                visualType = vis.VisualType.ToString(),
                                mappings = vis.Mappings
                                    .Select(m => new { role = m.Role, column = m.Column, display = m.DisplayName })
                                    .ToList(),
                            }));

                    var axisLabel = BuildMappingLabel(vis.Mappings);

                    if (vis.Source.TempTableName is string srcTable)
                    {
                        var srcId = EnsureTableNode(srcTable);
                        edges.Add(new DagEdgeDto(srcId, visId, axisLabel));
                    }
                    else if (vis.Source.InlineSelect is Statement inl)
                    {
                        foreach (var src in inl.GetSourceTables())
                        {
                            var srcId = EnsureTableNode(src);
                            edges.Add(new DagEdgeDto(srcId, visId, axisLabel));
                        }
                    }
                }
            }

            // Enrich table and dataset nodes with column-level lineage for DAG expansion
            var colTracker = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
            new LineageAnalyzer(colTracker).Analyze(script);
            var allLineage = colTracker.GetFullLineage().ToList();

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (node.Type != "table" && node.Type != "dataset") continue;

                var nodeEntries = allLineage
                    .Where(e => e.TargetColumn != null && e.TargetColumn != "*" &&
                                e.TargetTable.Equals(node.Label, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // A bare SELECT * yields only a "*" column with no real lineage —
                // leave the node empty so the dataset bridge can fill it with the
                // upstream dataset's actual columns (pass-through).
                if (nodeEntries.Count == 0) continue;

                var columns = nodeEntries
                    .Select(e => e.TargetColumn!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();

                // Rich per-column lineage: source columns, the transform that
                // produced them, and any inherited description / tags (e.g. pii).
                // Lets the detail panel walk a column back to its origin and show
                // "total = SUM(Amount) ← EDW.Sales.Amount · <description>".
                var columnLineage = nodeEntries
                    .GroupBy(e => e.TargetColumn!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g =>
                    {
                        var e = g.FirstOrDefault(x => x.SourceTables.Count > 0) ?? g.First();
                        var sources = e.SourceTables
                            .Select((t, k) => new { table = t, column = k < e.SourceColumns.Count ? e.SourceColumns[k] : null })
                            .ToList();
                        var tags = e.Metadata
                            .Where(kv => !kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
                            .ToDictionary(kv => kv.Key, kv => kv.Value);
                        return (object)new
                        {
                            sources,
                            transform = e.TransformationExpression,
                            functions = e.FunctionsApplied,
                            kind = e.TransformationKind == TransformationKind.Unknown ? null : e.TransformationKind.ToString(),
                            description = e.DerivedFromDescriptions ?? e.Description,
                            tags = tags.Count > 0 ? tags : null,
                        };
                    }, StringComparer.OrdinalIgnoreCase);

                nodes[i] = node with { Meta = new { columns, columnLineage } };
            }
        }
        catch (Exception ex)
        {
            throw new ReportStructureParseException(ex.Message, ex);
        }

        // Best-effort cross-script enrichment: resolve dataset references (built by
        // a separate script) to their column lineage so the detail panel can trace a
        // visual's field back through the dataset to its origin + description.
        try { await BridgeCatalogLineageAsync(nodes, edges); }
        catch { /* never let enrichment fail the structure render */ }
        try { await BridgeDatasetLineageAsync(reportId, nodes, edges); }
        catch { /* never let enrichment fail the structure render */ }

        return new DagDto(nodes, edges);

        static string? BuildMappingLabel(List<VisualMapping> mappings)
        {
            var x = mappings.FirstOrDefault(m => m.Role.Equals("XAXIS", StringComparison.OrdinalIgnoreCase))?.Column;
            var y = mappings.FirstOrDefault(m => m.Role.Equals("YAXIS", StringComparison.OrdinalIgnoreCase))?.Column;
            var parts = new List<string>();
            if (x is not null) parts.Add($"X: {x}");
            if (y is not null) parts.Add($"Y: {y}");
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        static Dictionary<string, List<string>> BuildVisualPageMap(Script script)
        {
            var visualNames = script.Statements
                .OfType<CreateVisualStatement>()
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var page in script.Statements.OfType<CreatePageStatement>())
            {
                foreach (var target in page.SlotMap.Values)
                {
                    if (!visualNames.Contains(target)) continue;
                    if (!map.TryGetValue(target, out var pages))
                    {
                        pages = [];
                        map[target] = pages;
                    }
                    if (!pages.Contains(page.Name, StringComparer.OrdinalIgnoreCase))
                        pages.Add(page.Name);
                }
            }

            return map;
        }
    }

    private static string NormalizeName(string? s) => (s ?? string.Empty).TrimStart('&', '#');

    // Resolve a registered dataset's column lineage by stitching two sources:
    //  - parsing its stored SourceQuery (column transform + source columns), and
    //  - the persisted lineage catalog from its own build run (inherited
    //    description / tags such as pii — which the SQL text alone cannot supply).
    private (List<string> Columns, Dictionary<string, object> Lineage) ResolveDatasetColumnLineage(
        Dataset ds, IEnumerable<LineageHistoryEntry> persistedEntries)
    {
        var parsed = new Dictionary<string, LineageEntry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!string.IsNullOrWhiteSpace(ds.SourceQuery))
            {
                var tokens = new Lexer(ds.SourceQuery).Tokenize();
                var script = new CoreParser(tokens, ds.SourceQuery).Parse();
                var tr = new LineageTracker(ETL_SQL.Common.NullLogger.Instance);
                new LineageAnalyzer(tr).Analyze(script);
                foreach (var e in tr.GetFullLineage())
                    if (e.TargetColumn != null && !parsed.ContainsKey(e.TargetColumn))
                        parsed[e.TargetColumn] = e;
            }
        }
        catch { /* unparseable source query — fall back to persisted lineage only */ }

        // persistedEntries are pre-fetched by the caller in one batch query, ordered so the
        // "dataset:&name" variant precedes "dataset:name"; first occurrence per column wins.
        var persisted = new Dictionary<string, LineageHistoryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in persistedEntries)
            if (e.TargetColumn != null && !persisted.ContainsKey(e.TargetColumn))
                persisted[e.TargetColumn] = e;

        var columns = new List<string>();
        var lineage = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in parsed.Keys.Concat(persisted.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c))
        {
            parsed.TryGetValue(col, out var p);
            persisted.TryGetValue(col, out var h);

            var srcTables = (p?.SourceTables ?? (IReadOnlyList<string>?)h?.SourceTables) ?? new List<string>();
            var srcCols = (p?.SourceColumns ?? (IReadOnlyList<string>?)h?.SourceColumns) ?? new List<string>();
            var sources = srcTables
                .Select((t, k) => new { table = t, column = k < srcCols.Count ? srcCols[k] : null })
                .ToList();

            string? description = p?.DerivedFromDescriptions
                ?? (h?.Tags != null && h.Tags.TryGetValue("d", out var hd) ? hd : null)
                ?? h?.DerivedFromDescriptions
                ?? (p?.Metadata != null && p.Metadata.TryGetValue("d", out var pd) ? pd : null);

            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (h?.Tags != null)
                foreach (var kv in h.Tags)
                    if (!kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase)) tags[kv.Key] = kv.Value;
            if (p?.Metadata != null)
                foreach (var kv in p.Metadata)
                    if (!kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase) && !tags.ContainsKey(kv.Key)) tags[kv.Key] = kv.Value;

            columns.Add(col);
            lineage[col] = new
            {
                sources,
                transform = p?.TransformationExpression ?? h?.TransformationExpression,
                functions = (object?)p?.FunctionsApplied ?? h?.FunctionsApplied,
                kind = (p != null && p.TransformationKind != TransformationKind.Unknown) ? p.TransformationKind.ToString() : h?.TransformationKind,
                description,
                tags = tags.Count > 0 ? tags : null,
            };
        }

        return (columns, lineage);
    }

    // Replace dataset-reference nodes' (and their SELECT * consumers') column
    // lineage with the resolved cross-script lineage.
    private async Task BridgeDatasetLineageAsync(int reportId, List<DagNodeDto> nodes, List<DagEdgeDto> edges)
    {
        var reportDatasets = await db.Datasets.Where(d => d.OwningReportId == reportId).ToListAsync();
        if (reportDatasets.Count == 0) return;

        var dsByNorm = reportDatasets
            .GroupBy(d => NormalizeName(d.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Batch all datasets' persisted lineage in one round-trip (was 2 queries per dataset).
        var persistedTargets = dsByNorm.Keys
            .SelectMany(norm => new[] { $"dataset:&{norm}", $"dataset:{norm}" })
            .ToList();
        var persistedByTarget = (await lineageCatalog.GetHistoryForTablesAsync(persistedTargets, 500))
            .GroupBy(e => e.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LineageHistoryEntry>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        var resolved = new Dictionary<string, (List<string> Columns, Dictionary<string, object> Lineage)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in dsByNorm)
        {
            var persistedEntries = new[] { $"dataset:&{kvp.Key}", $"dataset:{kvp.Key}" }
                .SelectMany(t => persistedByTarget.TryGetValue(t, out var l) ? l : Array.Empty<LineageHistoryEntry>());
            var r = ResolveDatasetColumnLineage(kvp.Value, persistedEntries);
            if (r.Columns.Count > 0) resolved[kvp.Key] = r;
        }
        if (resolved.Count == 0) return;

        // 1. Enrich the dataset-reference nodes themselves.
        var datasetRefCols = new Dictionary<string, (List<string> Columns, string Label)>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Type != "table" && node.Type != "dataset") continue;
            if (!resolved.TryGetValue(NormalizeName(node.Label), out var r)) continue;
            nodes[i] = node with { Type = "dataset", Meta = new { columns = r.Columns, columnLineage = r.Lineage } };
            datasetRefCols[node.Id] = (r.Columns, node.Label);
        }
        if (datasetRefCols.Count == 0) return;

        // 2. Propagate to temp tables that SELECT * from a single dataset ref
        //    (e.g. SELECT * INTO #sales FROM &sales_snap) — pass-through columns
        //    pointing back at the dataset so the chain stays connected.
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Type != "table" && node.Type != "dataset") continue;
            if (node.Meta != null) continue;   // already has column lineage from the report script
            var inbound = edges.Where(e => e.Target == node.Id && datasetRefCols.ContainsKey(e.Source)).ToList();
            if (inbound.Count != 1) continue;  // only the unambiguous single-source case
            var (cols, label) = datasetRefCols[inbound[0].Source];
            var passthrough = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cols)
                passthrough[c] = new
                {
                    sources = new[] { new { table = label, column = (string?)c } },
                    transform = (string?)null,
                    functions = (object?)null,
                    kind = "PassThrough",
                    description = (string?)null,
                    tags = (object?)null,
                };
            nodes[i] = node with { Meta = new { columns = cols, columnLineage = passthrough } };
        }
    }

    // Enrich raw source-table nodes from persisted runtime DB_CATALOG lineage.
    // This avoids portal-time DB round-trips while still making SELECT * consumers
    // inspectable after a report has run with catalog import enabled.
    private async Task BridgeCatalogLineageAsync(List<DagNodeDto> nodes, List<DagEdgeDto> edges)
    {
        var resolved = new Dictionary<string, (List<string> Columns, Dictionary<string, object> Lineage, string Label)>(StringComparer.OrdinalIgnoreCase);

        // Fetch persisted catalog lineage for every unresolved table node in one round-trip
        // (was an N+1: one SQLite query per node).
        var tableLabels = nodes
            .Where(n => n.Type == "table" && n.Meta == null)
            .Select(n => n.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tableLabels.Count == 0) return;

        var historyByTable = (await lineageCatalog.GetHistoryForTablesAsync(tableLabels, 500))
            .GroupBy(e => e.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Type != "table" || node.Meta != null) continue;
            if (!historyByTable.TryGetValue(node.Label, out var nodeHistory)) continue;

            var history = nodeHistory
                .Where(e => e.Operation.Equals("DB_CATALOG", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(e.TargetColumn))
                .GroupBy(e => e.TargetColumn!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(e => e.RunAt).First())
                .OrderBy(e => e.TargetColumn, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (history.Count == 0) continue;

            var columns = history.Select(e => e.TargetColumn!).ToList();
            var lineage = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in history)
            {
                var tags = e.Tags
                    .Where(kv => !kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                var description = e.Tags.TryGetValue("d", out var d)
                    ? d
                    : e.DerivedFromDescriptions;

                lineage[e.TargetColumn!] = new
                {
                    sources = Array.Empty<object>(),
                    transform = (string?)null,
                    functions = (object?)null,
                    kind = "Catalog",
                    description,
                    tags = tags.Count > 0 ? tags : null,
                };
            }

            nodes[i] = node with { Meta = new { columns, columnLineage = lineage } };
            resolved[node.Id] = (columns, lineage, node.Label);
        }

        if (resolved.Count == 0) return;

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.Type != "table" || node.Meta != null) continue;

            var inbound = edges.Where(e => e.Target == node.Id && resolved.ContainsKey(e.Source)).ToList();
            if (inbound.Count != 1) continue;

            var (cols, _, label) = resolved[inbound[0].Source];
            var passthrough = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cols)
            {
                passthrough[c] = new
                {
                    sources = new[] { new { table = label, column = (string?)c } },
                    transform = (string?)null,
                    functions = (object?)null,
                    kind = "PassThrough",
                    description = (string?)null,
                    tags = (object?)null,
                };
            }

            nodes[i] = node with { Meta = new { columns = cols, columnLineage = passthrough } };
        }
    }
}
