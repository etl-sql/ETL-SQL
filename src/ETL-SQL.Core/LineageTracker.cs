using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Common;

namespace ETL_SQL.Core
{
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
        private readonly List<LineageEntry> _entries = new();
        private readonly object _lock = new object();
        private readonly ILogger _logger;
        private readonly Dictionary<(string table, string op, string? col, int l, int c, string? f), LineageEntry> _lookup = new();
        private readonly Dictionary<string, Dictionary<string, string>> _latestTableMetadata = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _latestColumnMetadata = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _detectedCycles = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> GlobalMetadata { get; } = new(StringComparer.OrdinalIgnoreCase);

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
                    return;
                }

                var entry = new LineageEntry(target, operation)
                {
                    TargetColumn = targetColumn,
                    SourceTables = sources.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
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

                _entries.Add(entry);
                _lookup[key] = entry;

                // Track latest metadata for inheritance
                if (entry.Metadata.Count > 0)
                {
                    if (string.IsNullOrEmpty(targetColumn) || operation.Equals("TABLE_TAGS", StringComparison.OrdinalIgnoreCase))
                    {
                        // Table-level or direct table tags
                        if (!_latestTableMetadata.ContainsKey(target))
                            _latestTableMetadata[target] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var kv in entry.Metadata) _latestTableMetadata[target][kv.Key] = kv.Value;
                    }

                    if (!string.IsNullOrEmpty(targetColumn))
                    {
                        // Column-level
                        if (!_latestColumnMetadata.ContainsKey(target))
                            _latestColumnMetadata[target] = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                        if (!_latestColumnMetadata[target].ContainsKey(targetColumn))
                            _latestColumnMetadata[target][targetColumn] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var kv in entry.Metadata) _latestColumnMetadata[target][targetColumn][kv.Key] = kv.Value;
                    }
                }
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
        /// safe to call repeatedly from the same source line (e.g. a CREATE TAG inside a FOR loop),
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

                _entries.Add(new LineageEntry(table, "TABLE_TAGS")
                {
                    TargetColumn = column,
                    Metadata = new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        public IEnumerable<LineageEntry> GetLineage(string tableName)
        {
            lock (_lock)
            {
                return _entries.Where(e => e.TargetTable.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                               .OrderByDescending(e => e.Timestamp)
                               .ToList();
            }
        }

        public IEnumerable<LineageEntry> GetColumnLineage(string tableName, string columnName)
        {
            lock (_lock)
            {
                return _entries.Where(e => e.TargetTable.Equals(tableName, StringComparison.OrdinalIgnoreCase) &&
                                          (e.TargetColumn == null || e.TargetColumn.Equals(columnName, StringComparison.OrdinalIgnoreCase)))
                               .OrderByDescending(e => e.Timestamp)
                               .ToList();
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
                    else
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
                            else
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

        public void LoadState(IEnumerable<LineageEntry> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries)
            {
                Record(entry.TargetTable, entry.SourceTables, entry.Operation, entry.TargetColumn, entry.SourceColumns, entry.Metadata, entry.DerivedFromDescriptions, entry.Line, entry.Column, entry.EndLine, entry.EndColumn, entry.SourceFile);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _lookup.Clear();
                _latestTableMetadata.Clear();
                _latestColumnMetadata.Clear();
            }
        }
    }
}
