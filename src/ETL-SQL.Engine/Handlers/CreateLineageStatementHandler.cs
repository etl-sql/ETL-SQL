using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Governance;
using ETL_SQL.Data;
using ETL_SQL.Engine.Lineage;

namespace ETL_SQL.Engine.Handlers;
/// <summary>
/// Handles CREATE LINEAGE FOR TABLE &lt;table&gt; FROM &lt;source&gt; — imports lineage from an
/// OpenLineage JSON document (a file path, or inline JSON). The import is intended as an up-front
/// seed: any lineage the script subsequently produces accrues on top (last-writer-wins). The
/// FOR TABLE clause names the focus table for error context; the whole document is imported.
/// </summary>
public class CreateLineageStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(CreateLineageStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (CreateLineageStatement)statement;
        var row = new Row();

        var source = (await context.EvaluateValue(stmt.Source, row))?.ToString();
        if (string.IsNullOrWhiteSpace(source))
            throw new ExecutionException("CREATE LINEAGE: the FROM source evaluated to null or empty.");

        string content;
        var trimmed = source.TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            content = source; // inline JSON
        }
        else
        {
            var path = new FileSystemPolicyAuthorizer(context.SecurityService)
                .Authorize(context, context.ResolvePath(source), FileSystemAccessKind.Read, validateFileType: false)
                .CanonicalPath;
            if (!File.Exists(path))
                throw new ExecutionException($"CREATE LINEAGE: lineage source file not found: {source}");
            try
            {
                content = await File.ReadAllTextAsync(path);
            }
            catch (IOException ex)
            {
                throw new ExecutionException($"CREATE LINEAGE: could not read lineage source: {ex.Message}");
            }
        }

        List<LineageEntry> entries;
        try
        {
            entries = OpenLineageImporter.Import(content);
        }
        catch (ExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ExecutionException($"CREATE LINEAGE: failed to parse OpenLineage document: {ex.Message}");
        }

        if (entries.Count == 0)
        {
            context.Log("CREATE LINEAGE: no lineage edges found in the source document.");
            return;
        }

        context.LineageTracker.LoadState(entries);

        var tableCount = entries.Select(e => e.TargetTable).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        context.Log($"Imported lineage for {tableCount} table(s), {entries.Count} entry(ies).");

        var focus = (await context.EvaluateValue(stmt.TableName, row))?.ToString();
        if (!string.IsNullOrWhiteSpace(focus) &&
            !entries.Any(e => string.Equals(e.TargetTable, focus, StringComparison.OrdinalIgnoreCase)))
        {
            context.Log($"Note: CREATE LINEAGE focus table '{focus}' was not present in the imported document.");
        }
    }
}
