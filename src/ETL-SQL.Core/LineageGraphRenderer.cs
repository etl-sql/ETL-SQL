using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ETL_SQL.Core
{
    public class LineageGraphRenderer
    {
        public string Render(ILineageTracker tracker, string? targetTable = null, string? targetColumn = null)
        {
            var sb = new StringBuilder();
            var allEntries = targetTable != null 
                ? tracker.GetAncestors(targetTable, targetColumn).ToList()
                : tracker.GetFullLineage().ToList();

            if (!allEntries.Any()) return "No lineage data found.";

            // Find roots (terminal nodes in the graph we are looking at)
            var sourceTables = allEntries.SelectMany(e => e.SourceTables).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var roots = allEntries.Where(e => !sourceTables.Contains(e.TargetTable) || (targetColumn != null && e.TargetColumn == targetColumn)).ToList();

            if (targetTable != null)
            {
                roots = roots.Where(e => e.TargetTable.Equals(targetTable, StringComparison.OrdinalIgnoreCase)).ToList();
                if (targetColumn != null)
                {
                    roots = roots.Where(e => e.TargetColumn == null || e.TargetColumn.Equals(targetColumn, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }

            var groupedByTable = roots.GroupBy(e => e.TargetTable, StringComparer.OrdinalIgnoreCase);

            foreach (var tableGroup in groupedByTable)
            {
                sb.AppendLine($"[Table: {tableGroup.Key}]");
                
                var columns = tableGroup.Where(e => e.TargetColumn != null).GroupBy(e => e.TargetColumn!, StringComparer.OrdinalIgnoreCase);
                if (targetColumn != null) columns = columns.Where(g => g.Key.Equals(targetColumn, StringComparison.OrdinalIgnoreCase));

                int colCount = columns.Count();
                int colIdx = 0;
                foreach (var colGroup in columns)
                {
                    bool isLastCol = ++colIdx == colCount;
                    string prefix = isLastCol ? "└── " : "├── ";
                    sb.AppendLine($"{prefix}{colGroup.Key}");
                    
                    var entry = colGroup.First();
                    RenderSources(sb, tracker, entry, isLastCol ? "    " : "│   ", 1, new HashSet<string>());
                }
                
                // Also handle table-level lineage if no columns or specifically asked for table
                if (!columns.Any() || targetColumn == null)
                {
                    var tableEntry = tableGroup.FirstOrDefault(e => e.TargetColumn == null);
                    if (tableEntry != null)
                    {
                        RenderSources(sb, tracker, tableEntry, "    ", 1, new HashSet<string>());
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string RenderMermaid(ILineageTracker tracker, string? targetTable = null, string? targetColumn = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("graph TD");
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = targetTable != null 
                ? tracker.GetAncestors(targetTable, targetColumn).ToList()
                : tracker.GetFullLineage().ToList();

            foreach (var entry in entries)
            {
                string rawNodeId = entry.TargetColumn != null ? $"{entry.TargetTable}_{entry.TargetColumn}" : entry.TargetTable;
                string nodeId = CleanId(rawNodeId);
                string label = entry.TargetColumn != null ? $"{entry.TargetTable}.{entry.TargetColumn}" : entry.TargetTable;

                if (visited.Add(nodeId))
                {
                    sb.AppendLine($"    {nodeId}[\"{label}\"]");
                }

                for (int i = 0; i < entry.SourceTables.Count; i++)
                {
                    string srcTable = entry.SourceTables[i];
                    string? srcCol = entry.SourceColumns.Count > i ? entry.SourceColumns[i] : null;
                    string rawSrcNodeId = srcCol != null ? $"{srcTable}_{srcCol}" : srcTable;
                    string srcNodeId = CleanId(rawSrcNodeId);
                    string srcLabel = srcCol != null ? $"{srcTable}.{srcCol}" : srcTable;

                    if (visited.Add(srcNodeId))
                    {
                        sb.AppendLine($"    {srcNodeId}[\"{srcLabel}\"]");
                    }
                    sb.AppendLine($"    {srcNodeId} --> {nodeId}");
                }
            }

            return sb.ToString();
        }

        private string CleanId(string id) => new string(id.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

        private void RenderSources(StringBuilder sb, ILineageTracker tracker, LineageEntry entry, string indent, int depth, HashSet<string> visited)
        {
            string key = entry.TargetColumn != null ? $"{entry.TargetTable}.{entry.TargetColumn}.{entry.Operation}" : $"{entry.TargetTable}.{entry.Operation}";
            if (depth > 20 || visited.Contains(key)) return; 
            visited.Add(key);

            int sourceCount = entry.SourceTables.Count;
            for (int i = 0; i < sourceCount; i++)
            {
                string sourceTable = entry.SourceTables[i];
                string? sourceCol = entry.SourceColumns.Count > i ? entry.SourceColumns[i] : null;
                
                bool isLastSource = i == sourceCount - 1;
                string prefix = isLastSource ? "└── " : "├── ";
                string label = sourceCol != null ? $"{sourceTable}.{sourceCol}" : sourceTable;
                
                sb.AppendLine($"{indent}{prefix}{label}");

                var deeperEntries = sourceCol != null 
                    ? tracker.GetColumnLineage(sourceTable, sourceCol).ToList()
                    : tracker.GetLineage(sourceTable).ToList();

                if (deeperEntries.Any())
                {
                    RenderSources(sb, tracker, deeperEntries.First(), indent + (isLastSource ? "    " : "│   "), depth + 1, visited);
                }
            }
        }
    }
}
