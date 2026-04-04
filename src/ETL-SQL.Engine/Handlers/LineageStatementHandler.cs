using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
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
        public Type SupportedStatementType => typeof(LineageStatement);
        /// <summary>Executes the LINEAGE statement, rendering both a visual graph and a detailed audit log.</summary>
        public async Task Execute(Statement statement, IExecutionContext context)
        {
            var stmt = (LineageStatement)statement;
            
            string targetName = stmt.TargetTable.ConnectionName != null ? stmt.TargetTable.ConnectionName + "." + stmt.TargetTable.TableName : stmt.TargetTable.TableName;
            var entries = (stmt.ColumnName != null 
                ? context.LineageTracker.GetColumnLineage(targetName, stmt.ColumnName) 
                : context.LineageTracker.GetLineage(targetName)).ToList();

            if (!entries.Any())
            {
                Logger.WriteLine($"No lineage information found for '{targetName}'{(stmt.ColumnName != null ? "." + stmt.ColumnName : "")}.", ConsoleColor.Yellow);
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

                await File.WriteAllTextAsync(fullPath, sb.ToString());
                Logger.WriteLine($"Lineage report exported to: {fullPath}", ConsoleColor.Green);
            }

            // Visual Tree Representation (Console)
            Logger.WriteLine($"\nVisual Lineage for {targetName}{(stmt.ColumnName != null ? "." + stmt.ColumnName : "")}:", ConsoleColor.Cyan);
            string graph = renderer.Render(context.LineageTracker, targetName, stmt.ColumnName);
            Logger.WriteLine(graph, ConsoleColor.Gray);

            Logger.WriteLine($"\nDetailed Audit Log for {targetName}:", ConsoleColor.Cyan);
            Logger.WriteLine(new string('-', 80));
            Logger.WriteLine($"{"Timestamp",-20} | {"Operation",-15} | Sources");
            Logger.WriteLine(new string('-', 80));

            foreach (var entry in entries)
            {
                string sources = entry.SourceTables.Any() ? string.Join(", ", entry.SourceTables) : "(Direct Values)";
                Logger.WriteLine($"{entry.Timestamp:yyyy-MM-dd HH:mm:ss} | {entry.Operation,-15} | {sources}");
                
                if (entry.TargetColumn != null)
                {
                    string srcCols = entry.SourceColumns.Any() ? string.Join(", ", entry.SourceColumns) : "(None)";
                    Logger.WriteLine($"  -> Column: {entry.TargetColumn,-15} (Sources: {srcCols})", ConsoleColor.DarkGray);
                }
                
                foreach (var tag in entry.Metadata)
                {
                    Logger.WriteLine($"  -> @{tag.Key}: {tag.Value}", ConsoleColor.DarkMagenta);
                }

                if (!string.IsNullOrEmpty(entry.DerivedFromDescriptions))
                {
                    Logger.WriteLine($"  -> Derived From: {entry.DerivedFromDescriptions}", ConsoleColor.DarkCyan);
                }

                if (!string.IsNullOrEmpty(entry.SourceFile) || entry.Line > 0)
                {
                    string loc = $"[{entry.SourceFile ?? "Script"}:{entry.Line},{entry.Column}]";
                    Logger.WriteLine($"  -> Source Location: {loc}", ConsoleColor.DarkYellow);
                }
            }
            Logger.WriteLine(new string('-', 80) + "\n");
        }
    }
}
