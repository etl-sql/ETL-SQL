using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;

namespace ETL_SQL.Engine.Engines;
/// <summary>
/// Handles the streaming execution of simple SELECT statements.
/// This engine is used when no heavy operations (joins, aggregates, window functions) are required.
/// </summary>
public class StreamingQueryEngine(IExecutionContext context, ILogger logger)
{
    private readonly IExecutionContext _context = context;
    private readonly ILogger _logger = logger;

    public async IAsyncEnumerable<DataTable> ExecuteStreamingSelect(
        SelectStatement stmt,
        IAsyncEnumerable<DataTable> batches,
        List<SelectColumn> finalColumns,
        List<string> colNames)
    {
        var resultBatch = new DataTable();
        resultBatch.SetColumns(colNames);

        bool yielded = false;
        int rowsYielded = 0;
        int rowsSkipped = 0;
        long streamingRowNumber = 0;
        int offset = 0;

        if (stmt.Offset != null)
        {
            var offVal = await _context.EvaluateValue(stmt.Offset, new Row());
            offset = Convert.ToInt32(offVal);
        }
        int? limit = null;
        if (stmt.LimitCount != null)
        {
            var limVal = await _context.EvaluateValue(stmt.LimitCount, new Row());
            limit = Convert.ToInt32(limVal);
        }

        var compiledWhere = stmt.WhereClause != null
            && RowExpressionCompiler.TryCompilePredicate(_context, stmt.WhereClause, out var wherePredicate)
                ? wherePredicate
                : null;
        var compiledColumns = new RowExpressionCompiler.RowValue?[finalColumns.Count];
        for (int i = 0; i < finalColumns.Count; i++)
        {
            if (RowExpressionCompiler.TryCompileValue(_context, finalColumns[i].Expression, out var value))
                compiledColumns[i] = value;
        }

        string fromName = stmt.FromTable.Alias ?? stmt.FromTable.TableName;
        // Qualified-name plan, rebuilt only when the incoming rows' schema instance changes (rows in
        // one stream share a schema): an expanded schema carrying the canonical columns plus real
        // "from.col" columns mirroring their canonical slots. Building the eval row as one values
        // array over that shared schema replaces the previous per-row form — a row.Columns
        // dictionary copy plus one interpolated "from.col" string and dynamic-dictionary entry per
        // column per row, ~40% of the Gate F round-trip's total allocation (see
        // certification-results/spill-alloc-profile). Real columns (not schema aliases) so every
        // consumer — enumeration, fallbacks, correlated-subquery capture — sees the qualified names.
        ETL_SQL.Data.TableSchema? qualifySource = null;
        ETL_SQL.Data.TableSchema? qualifiedSchema = null;
        int[] qualifiedSlots = System.Array.Empty<int>();
        await foreach (var batch in batches)
        {
            foreach (var row in batch.Rows)
            {
                // Qualify row for evaluation context (especially for correlated subqueries)
                var evalRow = row;
                if (!string.IsNullOrEmpty(fromName))
                {
                    if (row.Schema != null && !row.HasDynamicColumns)
                    {
                        if (!ReferenceEquals(row.Schema, qualifySource))
                        {
                            var source = row.Schema;
                            var names = new List<string>(source.ColumnCount * 2);
                            var slots = new List<int>(source.ColumnCount);
                            for (int i = 0; i < source.ColumnCount; i++) names.Add(source.GetName(i));
                            for (int i = 0; i < source.ColumnCount; i++)
                            {
                                var name = source.GetName(i);
                                if (name.Contains('.')) continue;
                                names.Add($"{fromName}.{name}");
                                slots.Add(i);
                            }
                            qualifiedSchema = new ETL_SQL.Data.TableSchema(names);
                            source.CopyAliasesTo(qualifiedSchema);
                            qualifiedSlots = slots.ToArray();
                            qualifySource = source;
                        }

                        int canonical = row.Schema.ColumnCount;
                        var values = new object?[canonical + qualifiedSlots.Length];
                        for (int i = 0; i < canonical; i++) values[i] = row[i];
                        for (int i = 0; i < qualifiedSlots.Length; i++)
                            values[canonical + i] = values[qualifiedSlots[i]];
                        evalRow = new ETL_SQL.Data.Row(qualifiedSchema!, values);
                    }
                    else
                    {
                        // Schemaless rows / dynamic extras: legacy per-column qualification.
                        evalRow = row.Clone();
                        foreach (var kv in row.Columns)
                        {
                            if (!kv.Key.Contains(".")) evalRow[$"{fromName}.{kv.Key}"] = kv.Value;
                        }
                    }
                }

                if (stmt.WhereClause != null)
                {
                    var passesWhere = compiledWhere != null
                        ? compiledWhere(evalRow)
                        : await _context.EvaluateCondition(stmt.WhereClause, evalRow);
                    if (!passesWhere) continue;
                }

                streamingRowNumber++;

                if (rowsSkipped < offset)
                {
                    rowsSkipped++;
                    continue;
                }

                if (limit.HasValue && rowsYielded >= limit.Value) goto done;

                var resRow = resultBatch.NewRow();
                for (int i = 0; i < finalColumns.Count; i++)
                {
                    resRow[i] = IsStreamingRowNumber(finalColumns[i].Expression)
                        ? (decimal)streamingRowNumber
                        : compiledColumns[i] != null
                        ? compiledColumns[i]!(evalRow)
                        : await _context.EvaluateValue(finalColumns[i].Expression, evalRow);
                }

                await resultBatch.AddRowAsync(resRow);
                rowsYielded++;

                if (resultBatch.Rows.Count >= _context.BatchSize)
                {
                    yield return resultBatch;
                    yielded = true;
                    resultBatch = new DataTable();
                    resultBatch.SetColumns(colNames);
                }
            }
            if (limit.HasValue && rowsYielded >= limit.Value) break;
        }
    done:
        if (resultBatch.Rows.Count > 0 || !yielded) yield return resultBatch;
    }

    internal static bool IsStreamingRowNumber(Expression expression)
        => expression is FunctionCallExpression
        {
            FunctionName: var name,
            Arguments.Count: 0,
            Window: { PartitionBy.Count: 0, OrderBy.Count: 0, Frame: null }
        } && name.Equals("ROW_NUMBER", StringComparison.OrdinalIgnoreCase);

    public async IAsyncEnumerable<DataTable> ReplayBatches(DataTable? first, IAsyncEnumerator<DataTable> e)
    {
        try
        {
            if (first != null) yield return first;
            while (await e.MoveNextAsync()) yield return e.Current;
        }
        finally { await e.DisposeAsync(); }
    }
}
