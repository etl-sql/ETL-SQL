using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Lineage;

namespace ETL_SQL.Engine.Handlers;
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
            var fullPath = new FileSystemPolicyAuthorizer(context.SecurityService)
                .Authorize(context, context.ResolvePath(stmt.ExportPath), FileSystemAccessKind.Write, validateFileType: false)
                .CanonicalPath;
            var scriptName = context.LineageTracker.GlobalMetadata.TryGetValue("author", out var a) ? a : null;
            var jobNamespace = context.LineageNamespace ?? "etl-sql";

            var connectionNamespaces = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in context.Connections)
            {
                if (kv.Value != null)
                {
                    connectionNamespaces[kv.Key] = OpenLineageExporter.ResolveConnectionNamespace(kv.Key, kv.Value);
                }
            }

            await OpenLineageExporter.ExportToFileAsync(
                context.LineageTracker,
                context.SessionId ?? "session",
                scriptName,
                fullPath,
                jobNamespace,
                connectionNamespaces,
                _logger);
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

        if (!string.IsNullOrEmpty(stmt.IntoTable))
        {
            var table = await BuildLineageTable(entries);
            if (!context.Connections.ContainsKey(stmt.IntoTable))
            {
                context.Connections[stmt.IntoTable] = new InMemoryDataSource();
            }
            var destination = await context.ResolveDataSourceAsync(new TableReference(stmt.IntoTable));
            await destination.WriteBatches(new[] { table }.ToAsyncEnumerable());
            context.LastResult = table;
            context.LastResultSets.Add(table);
            context.OnResultSet?.Invoke(table);
            return;
        }

        if (!entries.Any())
        {
            _logger.WriteLine($"No lineage information found for '{targetName}'{(stmt.ColumnName != null ? "." + stmt.ColumnName : "")}.", ConsoleColor.Yellow);
            return;
        }

        var renderer = new LineageGraphRenderer();

        // Handle Export
        if (!string.IsNullOrEmpty(stmt.ExportPath))
        {
            var fullPath = new FileSystemPolicyAuthorizer(context.SecurityService)
                .Authorize(context, context.ResolvePath(stmt.ExportPath), FileSystemAccessKind.Write, validateFileType: false)
                .CanonicalPath;
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

    private static async Task<DataTable> BuildLineageTable(IEnumerable<LineageEntry> entries)
    {
        var table = new DataTable();
        table.SetColumns(new[]
        {
            "Timestamp", "Operation", "TargetTable", "TargetColumn",
            "SourceTables", "SourceColumns", "Description", "Metadata",
            "DerivedFromDescriptions", "SourceFile", "Line", "Column",
            "TransformationKind", "TransformationExpression", "FunctionsApplied"
        });

        foreach (var entry in entries)
        {
            var row = new Row();
            row["Timestamp"] = entry.Timestamp;
            row["Operation"] = entry.Operation;
            row["TargetTable"] = entry.TargetTable;
            row["TargetColumn"] = entry.TargetColumn;
            row["SourceTables"] = string.Join(", ", entry.SourceTables);
            row["SourceColumns"] = string.Join(", ", entry.SourceColumns);
            row["Description"] = entry.Description;
            row["Metadata"] = System.Text.Json.JsonSerializer.Serialize(entry.Metadata);
            row["DerivedFromDescriptions"] = entry.DerivedFromDescriptions;
            row["SourceFile"] = entry.SourceFile;
            row["Line"] = entry.Line;
            row["Column"] = entry.Column;
            row["TransformationKind"] = entry.TransformationKind == TransformationKind.Unknown ? null : entry.TransformationKind.ToString();
            row["TransformationExpression"] = entry.TransformationExpression;
            row["FunctionsApplied"] = entry.FunctionsApplied != null ? string.Join(", ", entry.FunctionsApplied) : null;
            await table.AddRowAsync(row);
        }

        return table;
    }
}
