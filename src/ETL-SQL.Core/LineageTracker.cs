using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;

namespace ETL_SQL.Core;

/// <summary>
/// Credential-free description of where a connection alias physically points. Carries only what is
/// safe to write into a lineage record, an OpenLineage export, or an IDE hover — never the
/// connection string, user, or password.
/// </summary>
/// <param name="ConnectorType">Connector name as written in the script (<c>MSSQL</c>, <c>FLATFILE</c>).</param>
/// <param name="Server">Host or instance, when known. Omitted from output under NO_SAVE_CONNECTION.</param>
/// <param name="Database">Catalog/database name, when known.</param>
/// <param name="FilePath">Resolved path, for file-backed connectors.</param>
public readonly record struct LineageSourceDescriptor(
    string? ConnectorType = null,
    string? Server = null,
    string? Database = null,
    string? FilePath = null)
{
    /// <summary>An alias that could not be resolved to anything physical.</summary>
    public static readonly LineageSourceDescriptor Unknown = new();

    public bool IsUnknown => string.IsNullOrEmpty(ConnectorType);
}

public class LineageEntry
{
    public string TargetTable { get; set; } = string.Empty;
    public string? TargetColumn { get; set; }
    public List<string> SourceTables { get; set; } = new();
    public List<string> SourceColumns { get; set; } = new();
    public string Operation { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? DerivedFromDescriptions { get; set; }
    public string? Description => Metadata.TryGetValue("d", out var d) ? d : null;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? SourceFile { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public TransformationKind TransformationKind { get; set; } = TransformationKind.Unknown;
    public string? TransformationExpression { get; set; }
    public IReadOnlyList<string>? FunctionsApplied { get; set; }

    /// <summary>
    /// Physical identifier for <see cref="TargetTable"/> — the connection alias resolved to
    /// something that still means something outside the script that produced it
    /// (<c>FLATFILE C:\tmp\patients.csv</c>, <c>localhost:EDW.dbo.Patient</c>). Null when the
    /// target is script-local (a <c>#temp</c> table, <c>RESULTSET</c>) or no connection is known.
    /// The logical <see cref="TargetTable"/> stays the lookup key so lineage survives export,
    /// import, and cross-script chaining, where the connection map is no longer in scope.
    /// </summary>
    public string? TargetTablePhysical { get; set; }

    /// <summary>Physical identifiers for <see cref="SourceTables"/>, positionally aligned. An entry is null when that source has no resolvable connection.</summary>
    public List<string?> SourceTablesPhysical { get; set; } = new();

    /// <summary>Display form of the target — physical when resolved, else the logical name.</summary>
    public string TargetTableDisplay => TargetTablePhysical ?? TargetTable;

    /// <summary>Display form of the source at <paramref name="index"/> — physical when resolved, else logical.</summary>
    public string SourceTableDisplay(int index) =>
        (index < SourceTablesPhysical.Count ? SourceTablesPhysical[index] : null) ?? SourceTables[index];

    public LineageEntry() { }

    public LineageEntry(string targetTable, string operation)
    {
        TargetTable = targetTable;
        Operation = operation;
    }

    public string GetTarget() => TargetTable;
    public string GetOperation() => Operation;

    public override string ToString() => $"{Timestamp:yyyy-MM-dd HH:mm:ss} | {Operation,-12} | {TargetTable}{(TargetColumn != null ? "." + TargetColumn : "")} <- {string.Join(", ", SourceTables)}";
}

public class LineageTracker : ILineageTracker
{
    public Func<string, LineageSourceDescriptor>? ConnectionResolver { get; set; }
    public bool NoSaveConnection { get; set; }

    private readonly List<LineageEntry> _entries = new();
    private readonly Dictionary<string, List<LineageEntry>> _entriesByTable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LineageEntry>> _tableWideEntriesByTable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string table, string column), List<LineageEntry>> _entriesByColumn = new(TableColumnKeyComparer.Instance);
    private readonly object _lock = new object();
    private readonly ILogger _logger;
    private readonly Dictionary<(string table, string op, string? col, int l, int c, string? f), LineageEntry> _lookup = new();
    private readonly Dictionary<string, Dictionary<string, string>> _latestTableMetadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _latestColumnMetadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _detectedCycles = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, string> GlobalMetadata { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Nodes (table or table.column) where a lineage cycle was encountered during ancestor
    /// traversal. Populated lazily by <see cref="GetAncestors"/>. Each cycle is also logged
    /// once as a warning when first detected.
    /// </summary>
    public IReadOnlyCollection<string> DetectedCycles
    {
        get { lock (_lock) { return _detectedCycles.ToList(); } }
    }

    public LineageTracker(ILogger logger)
    {
        _logger = logger;
    }

    private static readonly string[] FileConnectors =
        { "FLATFILE", "CSV", "EXCEL", "XLSX", "JSON", "XML", "PARQUET", "AVRO", "FIXEDWIDTH" };

    private static readonly string[] SyntheticConnectors =
        { "MOCK", "MOCKDB", "MOCK_DB", "TEST_COLUMNAR", "INMEMORY", "MEMORY" };

    /// <summary>True when the connector is file-backed, so its physical identity is a path rather than a database.</summary>
    public static bool IsFileConnector(string? connectorType) =>
        !string.IsNullOrEmpty(connectorType) && FileConnectors.Contains(connectorType.ToUpperInvariant());

    /// <summary>
    /// Splits a file descriptor ("FLATFILE C:\tmp\patients.csv") into its path, discarding the
    /// connector prefix. Returns false for database descriptors, which have no path.
    /// </summary>
    public static bool TryGetPhysicalFilePath(string? descriptor, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrEmpty(descriptor)) return false;

        int space = descriptor.IndexOf(' ');
        if (space <= 0) return false;
        if (!IsFileConnector(descriptor[..space])) return false;

        path = descriptor[(space + 1)..];
        return path.Length > 0;
    }

    /// <summary>
    /// Resolves a script-local table reference (<c>pats.FILE</c>, <c>hospital.dbo.Patient</c>) to a
    /// physical identifier that still means something once the script is out of view:
    /// <c>FLATFILE C:\tmp\patients.csv</c> for file connectors, <c>server:db.schema.table</c> for
    /// databases. When <paramref name="noSaveConnection"/> is set the server is omitted
    /// (<c>EDW.dbo.Patient</c>) so lineage can be shared without disclosing where it was read.
    /// Only credential-free fields are used — never the raw connection string.
    /// Returns null when the reference is script-local or the connection is unknown, in which case
    /// callers fall back to the logical name.
    /// </summary>
    public static string? ResolvePhysicalDescriptor(
        string rawTable,
        Func<string, LineageSourceDescriptor>? resolver,
        bool noSaveConnection)
    {
        if (string.IsNullOrEmpty(rawTable) || resolver == null) return null;

        // Script-local targets have no physical location to resolve.
        if (rawTable.StartsWith("#") || rawTable.StartsWith("&") ||
            rawTable.StartsWith("report:", StringComparison.OrdinalIgnoreCase) ||
            rawTable.StartsWith("dataset:", StringComparison.OrdinalIgnoreCase) ||
            rawTable.Equals("RESULTSET", StringComparison.OrdinalIgnoreCase) ||
            rawTable.Equals("VARIABLE", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = rawTable.Split('.', 2);
        string alias = parts[0];
        string rest = parts.Length > 1 ? parts[1] : "";

        var info = resolver(alias);
        if (info.IsUnknown) return null;

        string connector = info.ConnectorType!.ToUpperInvariant();
        if (SyntheticConnectors.Contains(connector)) return null;

        if (FileConnectors.Contains(connector) || !string.IsNullOrEmpty(info.FilePath))
        {
            if (string.IsNullOrEmpty(info.FilePath)) return null;
            // "pats.FILE" is the whole file; a named sheet/entity keeps its name appended.
            return string.IsNullOrEmpty(rest) || rest.Equals("FILE", StringComparison.OrdinalIgnoreCase)
                ? $"{connector} {info.FilePath}"
                : $"{connector} {info.FilePath}.{rest}";
        }

        // Without a database name there is nothing more identifying than the alias already was.
        if (string.IsNullOrEmpty(info.Database)) return null;

        string qualified = string.IsNullOrEmpty(rest) ? info.Database : $"{info.Database}.{rest}";
        return (!noSaveConnection && !string.IsNullOrEmpty(info.Server))
            ? $"{info.Server}:{qualified}"
            : qualified;
    }

    private string? Physical(string rawTable) =>
        ResolvePhysicalDescriptor(rawTable, ConnectionResolver, NoSaveConnection);

    public void Record(string target, IEnumerable<string> sources, string operation, string? targetColumn = null, IEnumerable<string>? sourceColumns = null, Dictionary<string, string>? metadata = null, string? derivedFromDescriptions = null, int line = 0, int column = 0, int endLine = 0, int endColumn = 0, string? sourceFile = null, TransformationKind transformationKind = TransformationKind.Unknown, string? transformationExpression = null, IReadOnlyList<string>? functionsApplied = null)
    {
        if (string.IsNullOrEmpty(target)) return;

        lock (_lock)
        {
            var key = (target.ToLowerInvariant(), operation.ToLowerInvariant(), targetColumn?.ToLowerInvariant(), line, column, sourceFile);
            if (_lookup.TryGetValue(key, out var existing))
            {
                if (metadata != null)
                {
                    foreach (var kv in metadata) existing.Metadata[kv.Key] = kv.Value;
                }
                if (derivedFromDescriptions != null) existing.DerivedFromDescriptions = derivedFromDescriptions;
                if (transformationKind != TransformationKind.Unknown) existing.TransformationKind = transformationKind;
                if (transformationExpression != null) existing.TransformationExpression = transformationExpression;
                if (functionsApplied != null) existing.FunctionsApplied = functionsApplied;

                // Tags merged into an existing entry still have to reach the metadata indexes, or
                // a column tagged by a second observation of the same statement would be
                // queryable on the entry but invisible to GetColumnMetadata and tag inheritance.
                ApplyMetadataFromEntry(existing);
                return;
            }

            var sourceList = sources.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var entry = new LineageEntry(target, operation)
            {
                TargetColumn = targetColumn,
                TargetTablePhysical = Physical(target),
                SourceTables = sourceList,
                SourceTablesPhysical = sourceList.Select(Physical).ToList(),
                SourceColumns = sourceColumns?.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
                Metadata = metadata ?? new(StringComparer.OrdinalIgnoreCase),
                DerivedFromDescriptions = derivedFromDescriptions,
                Line = line,
                Column = column,
                EndLine = endLine,
                EndColumn = endColumn,
                SourceFile = sourceFile,
                TransformationKind = transformationKind,
                TransformationExpression = transformationExpression,
                FunctionsApplied = functionsApplied
            };

            // Merge global metadata
            foreach (var kv in GlobalMetadata)
            {
                if (!entry.Metadata.ContainsKey(kv.Key))
                    entry.Metadata[kv.Key] = kv.Value;
            }

            AddEntry(entry);
            _lookup[key] = entry;
            ApplyMetadataFromEntry(entry);
        }
    }

    public Dictionary<string, string> GetTableMetadata(string tableName)
    {
        lock (_lock)
        {
            if (_latestTableMetadata.TryGetValue(tableName, out var metadata))
            {
                return new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
            }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public Dictionary<string, string> GetColumnMetadata(string tableName, string columnName)
    {
        lock (_lock)
        {
            if (_latestColumnMetadata.TryGetValue(tableName, out var tableMetadata) &&
                tableMetadata.TryGetValue(columnName, out var metadata))
            {
                return new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
            }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Explicitly upserts table- or column-level tags (last-writer-wins) and records a
    /// TABLE_TAGS audit entry so the tags round-trip through SHOW LINEAGE / OpenLineage export.
    /// Unlike <see cref="Record"/>, this writes the inheritance dictionaries directly and so is
    /// safe to call repeatedly from the same source line (e.g. INSERT TAG inside a FOR loop),
    /// where Record's location-based dedup would otherwise skip re-seeding the dictionaries.
    /// </summary>
    public void ApplyTags(string table, string? column, IReadOnlyDictionary<string, string> tags)
    {
        if (string.IsNullOrEmpty(table) || tags == null || tags.Count == 0) return;

        lock (_lock)
        {
            if (string.IsNullOrEmpty(column))
            {
                if (!_latestTableMetadata.TryGetValue(table, out var tm))
                {
                    tm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _latestTableMetadata[table] = tm;
                }
                foreach (var kv in tags) tm[kv.Key] = kv.Value;
            }
            else
            {
                if (!_latestColumnMetadata.TryGetValue(table, out var cols))
                {
                    cols = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    _latestColumnMetadata[table] = cols;
                }
                if (!cols.TryGetValue(column, out var cm))
                {
                    cm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    cols[column] = cm;
                }
                foreach (var kv in tags) cm[kv.Key] = kv.Value;
            }

            AddEntry(new LineageEntry(table, "TABLE_TAGS")
            {
                TargetColumn = column,
                Metadata = new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    public void RemoveTags(string table, string? column, IReadOnlyCollection<string> tagNames)
    {
        if (string.IsNullOrEmpty(table) || tagNames == null || tagNames.Count == 0) return;

        lock (_lock)
        {
            if (string.IsNullOrEmpty(column))
            {
                if (_latestTableMetadata.TryGetValue(table, out var tm))
                {
                    foreach (var tagName in tagNames) tm.Remove(tagName);
                }
            }
            else if (_latestColumnMetadata.TryGetValue(table, out var cols)
                     && cols.TryGetValue(column, out var cm))
            {
                foreach (var tagName in tagNames) cm.Remove(tagName);
            }

            AddEntry(new LineageEntry(table, "TABLE_TAG_DELETE")
            {
                TargetColumn = column,
                Metadata = tagNames.ToDictionary(tagName => tagName, _ => "", StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    public IEnumerable<LineageEntry> GetLineage(string tableName)
    {
        lock (_lock)
        {
            return _entriesByTable.TryGetValue(tableName, out var entries)
                ? CopyNewestFirst(entries)
                : Enumerable.Empty<LineageEntry>();
        }
    }

    public IEnumerable<LineageEntry> GetColumnLineage(string tableName, string columnName)
    {
        lock (_lock)
        {
            _tableWideEntriesByTable.TryGetValue(tableName, out var tableEntries);
            _entriesByColumn.TryGetValue((tableName, columnName), out var columnEntries);

            return CopyNewestFirst(tableEntries, columnEntries);
        }
    }

    public IEnumerable<LineageEntry> GetAncestors(string tableName, string? columnName = null)
    {
        lock (_lock)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ancestors = new List<LineageEntry>();
            WalkAncestors(tableName, columnName, visited, path, ancestors);
            return ancestors;
        }
    }

    private void AddEntry(LineageEntry entry)
    {
        _entries.Add(entry);
        IndexEntry(entry);
    }

    private void IndexEntry(LineageEntry entry)
    {
        AddIndexedEntry(_entriesByTable, entry.TargetTable, entry);

        if (string.IsNullOrEmpty(entry.TargetColumn))
        {
            AddIndexedEntry(_tableWideEntriesByTable, entry.TargetTable, entry);
        }
        else
        {
            var key = (entry.TargetTable, entry.TargetColumn);
            if (!_entriesByColumn.TryGetValue(key, out var columnEntries))
            {
                columnEntries = new List<LineageEntry>();
                _entriesByColumn[key] = columnEntries;
            }

            columnEntries.Add(entry);
        }
    }

    private void ApplyMetadataFromEntry(LineageEntry entry)
    {
        if (entry.Metadata.Count == 0) return;

        if (entry.Operation.Equals("TABLE_TAG_DELETE", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(entry.TargetColumn))
            {
                if (_latestTableMetadata.TryGetValue(entry.TargetTable, out var tm))
                {
                    foreach (var tagName in entry.Metadata.Keys) tm.Remove(tagName);
                }
            }
            else if (_latestColumnMetadata.TryGetValue(entry.TargetTable, out var cols)
                     && cols.TryGetValue(entry.TargetColumn, out var cm))
            {
                foreach (var tagName in entry.Metadata.Keys) cm.Remove(tagName);
            }
            return;
        }

        if (string.IsNullOrEmpty(entry.TargetColumn)
            || entry.Operation.Equals("TABLE_TAGS", StringComparison.OrdinalIgnoreCase)
            || entry.Operation.Equals("IMPORTED", StringComparison.OrdinalIgnoreCase))
        {
            if (!_latestTableMetadata.ContainsKey(entry.TargetTable))
                _latestTableMetadata[entry.TargetTable] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in entry.Metadata) _latestTableMetadata[entry.TargetTable][kv.Key] = kv.Value;
        }

        if (!string.IsNullOrEmpty(entry.TargetColumn))
        {
            if (!_latestColumnMetadata.ContainsKey(entry.TargetTable))
                _latestColumnMetadata[entry.TargetTable] = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            if (!_latestColumnMetadata[entry.TargetTable].ContainsKey(entry.TargetColumn))
                _latestColumnMetadata[entry.TargetTable][entry.TargetColumn] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in entry.Metadata) _latestColumnMetadata[entry.TargetTable][entry.TargetColumn][kv.Key] = kv.Value;
        }
    }

    private static void AddIndexedEntry(Dictionary<string, List<LineageEntry>> index, string key, LineageEntry entry)
    {
        if (!index.TryGetValue(key, out var entries))
        {
            entries = new List<LineageEntry>();
            index[key] = entries;
        }

        entries.Add(entry);
    }

    private static List<LineageEntry> CopyNewestFirst(List<LineageEntry>? entries)
    {
        if (entries == null || entries.Count == 0) return new List<LineageEntry>();

        var result = new List<LineageEntry>(entries.Count);
        for (var i = entries.Count - 1; i >= 0; i--)
            result.Add(entries[i]);

        return result;
    }

    private static List<LineageEntry> CopyNewestFirst(List<LineageEntry>? first, List<LineageEntry>? second)
    {
        if (first == null || first.Count == 0) return CopyNewestFirst(second);
        if (second == null || second.Count == 0) return CopyNewestFirst(first);

        var result = new List<LineageEntry>(first.Count + second.Count);
        var firstIndex = first.Count - 1;
        var secondIndex = second.Count - 1;

        while (firstIndex >= 0 || secondIndex >= 0)
        {
            if (secondIndex < 0 || (firstIndex >= 0 && first[firstIndex].Timestamp >= second[secondIndex].Timestamp))
            {
                result.Add(first[firstIndex--]);
            }
            else
            {
                result.Add(second[secondIndex--]);
            }
        }

        return result;
    }

    private void WalkAncestors(string table, string? column, HashSet<string> visited, HashSet<string> path, List<LineageEntry> collective)
    {
        string key = column != null ? $"{table}.{column}" : table;

        // A node already on the current DFS path is a back-edge — a genuine cycle. The
        // `visited` set alone can't distinguish this from harmless re-convergent (diamond)
        // lineage, so the active recursion stack (`path`) is tracked separately.
        if (path.Contains(key))
        {
            if (_detectedCycles.Add(key))
                _logger.Warning("Lineage cycle detected involving '{Node}'. The cycle is skipped during traversal; ancestor lineage for this node may be incomplete.", key);
            return;
        }

        if (visited.Contains(key)) return;
        visited.Add(key);
        path.Add(key);

        var entries = column != null ? GetColumnLineage(table, column) : GetLineage(table);
        foreach (var entry in entries)
        {
            collective.Add(entry);
            for (int i = 0; i < entry.SourceTables.Count; i++)
            {
                var srcTable = entry.SourceTables[i];
                var srcColumn = (entry.SourceColumns != null && i < entry.SourceColumns.Count) ? entry.SourceColumns[i] : null;
                WalkAncestors(srcTable, srcColumn, visited, path, collective);
            }
        }

        path.Remove(key);
    }

    public Dictionary<string, string> InheritMetadata(IEnumerable<string> sourceTables, IEnumerable<string> sourceColumns, out string? derivedFromDescriptions)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var derivedList = new List<string>();
        string? lastSeenDescription = null;
        derivedFromDescriptions = null;

        var sources = sourceTables.ToList();
        var columns = sourceColumns.ToList();

        // Iterate through sources in order. Since sources and columns are pairs from expressions,
        // we process them together to maintain correct inheritance priority.
        for (int i = 0; i < sources.Count; i++)
        {
            var sTable = sources[i];

            // 1. Table-level metadata (lower priority than columns)
            var tm = GetTableMetadata(sTable);
            foreach (var kv in tm)
            {
                if (kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
                {
                    lastSeenDescription = kv.Value;
                }
                else if (!Quality.ColumnRuleParser.IsRuleTagKey(kv.Key))
                {
                    result[kv.Key] = kv.Value;
                }
            }

            // 2. Column-level metadata (higher priority)
            // If we have a corresponding column in the list for this source table
            if (i < columns.Count)
            {
                var sc = columns[i];
                var m = GetColumnMetadata(sTable, sc);
                if (m != null && m.Count > 0)
                {
                    foreach (var kv in m)
                    {
                        if (kv.Key.Equals("d", StringComparison.OrdinalIgnoreCase))
                        {
                            lastSeenDescription = kv.Value;
                            derivedList.Add($"{sc}: {kv.Value}");
                        }
                        // @expect/@fail are enforcement directives scoped to the declaring
                        // statement, not descriptive metadata — they must not be inherited.
                        else if (!Quality.ColumnRuleParser.IsRuleTagKey(kv.Key))
                        {
                            result[kv.Key] = kv.Value;
                        }
                    }
                }
            }
        }

        if (derivedList.Any()) derivedFromDescriptions = string.Join("; ", derivedList.Distinct());

        // Apply the final winner for description if not explicitly set elsewhere
        if (lastSeenDescription != null && !result.ContainsKey("d"))
        {
            result["d"] = lastSeenDescription;
        }

        return result;
    }

    public IEnumerable<LineageEntry> GetFullLineage()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    /// <summary>
    /// Assigns each entry its distance from a raw source: a write whose inputs nothing else in the
    /// graph produces is step 1, a write that consumes that write's output is step 2, and so on.
    /// Ordering by step reads a flow origin-first, which is how someone tracing a column actually
    /// wants to walk it — timestamp order does not, because static analysis and execution record
    /// the same flow at different moments.
    /// Cycles are broken by leaving the entry at its lowest observed step.
    /// </summary>
    public static IReadOnlyDictionary<LineageEntry, int> ComputeChainSteps(IEnumerable<LineageEntry> entries)
    {
        var all = entries.ToList();

        // Which entries produce a given table, so an entry's inputs can be traced to their writers.
        var producers = new Dictionary<string, List<LineageEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in all)
        {
            if (string.IsNullOrEmpty(e.TargetTable)) continue;
            if (!producers.TryGetValue(e.TargetTable, out var list))
                producers[e.TargetTable] = list = new List<LineageEntry>();
            list.Add(e);
        }

        var steps = new Dictionary<LineageEntry, int>();
        var inProgress = new HashSet<LineageEntry>();

        int Depth(LineageEntry entry)
        {
            if (steps.TryGetValue(entry, out var known)) return known;
            if (!inProgress.Add(entry)) return 1;   // cycle — treat as an origin

            int depth = 1;
            foreach (var source in entry.SourceTables)
            {
                if (!producers.TryGetValue(source, out var upstream)) continue;
                foreach (var producer in upstream)
                {
                    // A self-referential write (UPDATE t FROM t) is not a further step.
                    if (ReferenceEquals(producer, entry)) continue;
                    if (string.Equals(producer.TargetTable, entry.TargetTable, StringComparison.OrdinalIgnoreCase)) continue;
                    depth = Math.Max(depth, Depth(producer) + 1);
                }
            }

            inProgress.Remove(entry);
            steps[entry] = depth;
            return depth;
        }

        foreach (var e in all) Depth(e);
        return steps;
    }

    public void LoadState(IEnumerable<LineageEntry> entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
        {
            // Transformation detail has to survive the round trip, or re-imported lineage loses
            // the very thing that makes a hop worth reading — that a CAST happened here.
            Record(entry.TargetTable, entry.SourceTables, entry.Operation, entry.TargetColumn,
                entry.SourceColumns, entry.Metadata, entry.DerivedFromDescriptions,
                entry.Line, entry.Column, entry.EndLine, entry.EndColumn, entry.SourceFile,
                entry.TransformationKind, entry.TransformationExpression, entry.FunctionsApplied);
        }
    }

    public int RemoveImportedLineage(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName)) return 0;

        lock (_lock)
        {
            var removed = _entries.RemoveAll(entry =>
                entry.Operation.Equals("IMPORTED", StringComparison.OrdinalIgnoreCase) &&
                entry.TargetTable.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
                RebuildIndexesAndMetadataLocked();
            return removed;
        }
    }

    private void RebuildIndexesAndMetadataLocked()
    {
        _entriesByTable.Clear();
        _tableWideEntriesByTable.Clear();
        _entriesByColumn.Clear();
        _lookup.Clear();
        _latestTableMetadata.Clear();
        _latestColumnMetadata.Clear();
        _detectedCycles.Clear();

        foreach (var entry in _entries)
        {
            IndexEntry(entry);
            _lookup[(entry.TargetTable.ToLowerInvariant(), entry.Operation.ToLowerInvariant(), entry.TargetColumn?.ToLowerInvariant(), entry.Line, entry.Column, entry.SourceFile)] = entry;
            ApplyMetadataFromEntry(entry);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _entriesByTable.Clear();
            _tableWideEntriesByTable.Clear();
            _entriesByColumn.Clear();
            _lookup.Clear();
            _latestTableMetadata.Clear();
            _latestColumnMetadata.Clear();
            _detectedCycles.Clear();
        }
    }

    private sealed class TableColumnKeyComparer : IEqualityComparer<(string table, string column)>
    {
        public static readonly TableColumnKeyComparer Instance = new();

        public bool Equals((string table, string column) x, (string table, string column) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.table, y.table) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.column, y.column);

        public int GetHashCode((string table, string column) obj)
        {
            var hash = new HashCode();
            hash.Add(obj.table, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.column, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
