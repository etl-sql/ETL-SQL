using ETL_SQL.Common;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using ETL_SQL.Engine.Lineage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace ETL_SQL.Engine.Handlers
{
    /// <summary>
    /// Handles the LINEAGE statement, providing a visual and detailed trace of data origins and transformations.
    /// </summary>
    public class LineageStatementHandler : IStatementHandler
    {
        private readonly ILogger _logger;
        public Type SupportedStatementType => typeof(LineageStatement);

        public LineageStatementHandler(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>Executes the LINEAGE statement, rendering both a visual graph and a detailed audit log.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (LineageStatement)statement;

            // OpenLineage export mode
            if (stmt.ExportAsOpenLineage && !string.IsNullOrEmpty(stmt.ExportPath))
            {
                var fullPath = context.ResolvePath(stmt.ExportPath);
                var scriptName = context.LineageTracker.GlobalMetadata.TryGetValue("author", out var a) ? a : null;
                await OpenLineageExporter.ExportToFileAsync(
                    context.LineageTracker, context.SessionId ?? "session", scriptName, fullPath, _logger);
                _logger.WriteLine($"OpenLineage export written to: {fullPath}", ConsoleColor.Green);
                return;
            }

            string? targetName = stmt.TargetTable == null ? null :
                stmt.TargetTable.ConnectionName != null
                    ? stmt.TargetTable.ConnectionName + "." + stmt.TargetTable.TableName
                    : stmt.TargetTable.TableName;

            var entries = (targetName != null
                ? (stmt.ColumnName != null
                    ? context.LineageTracker.GetColumnLineage(targetName, stmt.ColumnName)
                    : context.LineageTracker.GetLineage(targetName))
                : context.LineageTracker.GetFullLineage()).ToList();

            if (!entries.Any())
            {
                _logger.WriteLine($"No lineage information found for '{targetName}'{(stmt.ColumnName != null ? "." + stmt.ColumnName : "")}.", ConsoleColor.Yellow);
                return;
            }

            var renderer = new LineageGraphRenderer();

            // Handle Export
            if (!string.IsNullOrEmpty(stmt.ExportPath))
            {
                var fullPath = context.ResolvePath(stmt.ExportPath);
                var sb = new StringBuilder();
                sb.AppendLine($"# Data Lineage Report: {targetName}");
                if (stmt.ColumnName != null) sb.AppendLine($"## Column: {stmt.ColumnName}");
                sb.AppendLine();
                sb.AppendLine("## Visual Graph");
                sb.AppendLine("```mermaid");
                sb.AppendLine(renderer.RenderMermaid(context.LineageTracker, targetName, stmt.ColumnName));
                sb.AppendLine("```");
                sb.AppendLine();
                sb.AppendLine("## Detailed Audit Log");
                sb.AppendLine("| Timestamp | Operation | Sources | Metadata |");
                sb.AppendLine("| :--- | :--- | :--- | :--- |");

                foreach (var entry in entries)
                {
                    string sources = entry.SourceTables.Any() ? string.Join(", ", entry.SourceTables) : "(Direct Values)";
                    if (entry.SourceColumns.Any()) sources += $" ({string.Join(", ", entry.SourceColumns)})";
                    
                    var metaParts = entry.Metadata.Select(kv => $"**{kv.Key}**: {kv.Value}").ToList();
                    if (!string.IsNullOrEmpty(entry.DerivedFromDescriptions)) metaParts.Add($"*Derived From*: {entry.DerivedFromDescriptions}");
                    string metadataStr = string.Join("<br/>", metaParts);

                    sb.AppendLine($"| {entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.Operation} | {sources} | {metadataStr} |");
                }

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(fullPath, sb.ToString());
                _logger.WriteLine($"Lineage report exported to: {fullPath}", ConsoleColor.Green);
            }

            // Visual Tree Representation (Console)
            _logger.WriteLine($"\nVisual Lineage for {targetName}{(stmt.ColumnName != null ? "." + stmt.ColumnName : "")}:", ConsoleColor.Cyan);
            string graph = renderer.Render(context.LineageTracker, targetName, stmt.ColumnName);
            _logger.WriteLine(graph, ConsoleColor.Gray);

            _logger.WriteLine($"\nDetailed Audit Log for {targetName}:", ConsoleColor.Cyan);
            _logger.WriteLine(new string('-', 80));
            _logger.WriteLine($"{"Timestamp",-20} | {"Operation",-15} | Sources");
            _logger.WriteLine(new string('-', 80));

            foreach (var entry in entries)
            {
                string sources = entry.SourceTables.Any() ? string.Join(", ", entry.SourceTables) : "(Direct Values)";
                _logger.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.Operation,-15} | {sources}");
                
                if (entry.TargetColumn != null)
                {
                    string srcCols = entry.SourceColumns.Any() ? string.Join(", ", entry.SourceColumns) : "(None)";
                    _logger.WriteLine($"  -> Column: {entry.TargetColumn,-15} (Sources: {srcCols})", ConsoleColor.DarkGray);
                }
                
                foreach (var tag in entry.Metadata)
                {
                    _logger.WriteLine($"  -> @{tag.Key}: {tag.Value}", ConsoleColor.DarkMagenta);
                }

                if (!string.IsNullOrEmpty(entry.DerivedFromDescriptions))
                {
                    _logger.WriteLine($"  -> Derived From: {entry.DerivedFromDescriptions}", ConsoleColor.DarkCyan);
                }

                if (!string.IsNullOrEmpty(entry.SourceFile) || entry.Line > 0)
                {
                    string loc = $"[{entry.SourceFile ?? "Script"}:{entry.Line},{entry.Column}]";
                    _logger.WriteLine($"  -> Source Location: {loc}", ConsoleColor.DarkYellow);
                }
            }
            _logger.WriteLine(new string('-', 80) + "\n");
        }
    }
}
