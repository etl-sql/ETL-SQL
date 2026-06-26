using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;
/// <summary>
/// Handles PIVOT and UNPIVOT operations, transforming data between long and wide formats.
/// </summary>
public class PivotEngine
{
    private readonly IExecutionContext _context;
    private readonly AggregateEngine _aggregateEngine;
    private readonly ILogger _logger;

    public PivotEngine(IExecutionContext context, ILogger logger)
    {
        _context = context;
        _logger = logger;
        _aggregateEngine = new AggregateEngine(context, logger);
    }

    /// <summary>Transforms a list of rows into a pivoted format based on the specified pivot clause.</summary>
    public async Task<List<Row>> ApplyPivot(List<Row> rows, PivotClause pivot)
    {
        if (rows.Count == 0) return rows;



        // 1. Identify grouping columns (all columns except AggregateColumn and PivotColumn)
        var rawGroupingCols = rows[0].Columns.Keys.Where(c =>
            !c.Equals(pivot.PivotColumn, StringComparison.OrdinalIgnoreCase) &&
            !c.Equals(pivot.AggregateColumn, StringComparison.OrdinalIgnoreCase) &&
            !IsMatch(c, pivot.PivotColumn) &&
            !IsMatch(c, pivot.AggregateColumn)
        ).ToList();

        // Deduplicate: if we have both "Col" and "Table.Col", only keep one (preferring the unprefixed short name for the final result set if possible, or just consistency)
        var groupingCols = new List<string>();
        var seenBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Sort by length so we see short names first or long names first? 
        // If we keep short names, it looks cleaner.
        foreach (var col in rawGroupingCols.OrderBy(c => c.Contains(".") ? 1 : 0))
        {
            var baseName = col.Contains(".") ? col.Split('.').Last() : col;
            if (seenBaseNames.Add(baseName))
            {
                groupingCols.Add(col);
            }
        }

        // 2. Group by grouping columns
        var groups = new Dictionary<string, List<Row>>();
        foreach (var row in rows)
        {
            var key = string.Join("|", groupingCols.Select(c => row[c]?.ToString() ?? "NULL"));
            if (!groups.TryGetValue(key, out var list)) { list = new List<Row>(); groups[key] = list; }
            list.Add(row);
        }

        // 3. Transform groups
        var resultRows = new List<Row>();
        var pivotValueNames = new List<string>();
        var pivotValues = new List<object?>();

        foreach (var valExpr in pivot.PivotValues)
        {
            var val = await _context.EvaluateValue(valExpr, new Row());
            pivotValues.Add(val);
            pivotValueNames.Add(val?.ToString() ?? "NULL");
        }

        foreach (var groupRows in groups.Values)
        {
            var baseRow = new Row();
            foreach (var col in groupingCols) baseRow[col] = groupRows[0][col];

            for (int i = 0; i < pivot.PivotValues.Count; i++)
            {
                var pivotVal = pivotValues[i];
                var targetColName = pivotValueNames[i];

                var filteredRows = groupRows.Where(r =>
                {
                    var rVal = FindValue(r, pivot.PivotColumn);
                    return _context.CompareConstants(rVal, pivotVal) == 0;
                }).ToList();

                // Apply aggregation
                var aggArgs = new List<Expression>();
                if (pivot.AggregateColumn == "*") { /* COUNT(*) case */ }
                else aggArgs.Add(new IdentifierExpression(pivot.AggregateColumn));

                var aggExpr = new FunctionCallExpression(pivot.AggregateFunction, aggArgs);
                baseRow[targetColName] = await _aggregateEngine.EvaluateAggregate(aggExpr, filteredRows);
            }
            resultRows.Add(baseRow);
        }

        return resultRows;
    }

    public async Task<List<Row>> ApplyUnpivot(List<Row> rows, UnpivotClause unpivot)
    {
        if (rows.Count == 0) return rows;

        var resultRows = new List<Row>();
        var allCols = rows[0].Columns.Keys.ToList();
        var colsToKeep = allCols.Where(c => !unpivot.UnpivotColumns.Any(uc => IsMatch(c, uc))).ToList();

        foreach (var row in rows)
        {
            foreach (var unpivotCol in unpivot.UnpivotColumns)
            {
                var newRow = new Row();
                foreach (var col in colsToKeep) newRow[col] = row[col];

                newRow[unpivot.NameColumn] = unpivotCol;
                newRow[unpivot.ValueColumn] = FindValue(row, unpivotCol);
                resultRows.Add(newRow);
            }
        }

        return resultRows;
    }

    public async IAsyncEnumerable<Row> ApplyPivotStream(IAsyncEnumerable<Row> rows, PivotClause pivot)
    {
        await using var enumerator = rows.GetAsyncEnumerator();
        var buffered = new List<Row>();
        var threshold = Math.Max(1, _context.JoinSpillThreshold);
        while (buffered.Count <= threshold && await enumerator.MoveNextAsync())
            buffered.Add(enumerator.Current);

        if (buffered.Count == 0)
            yield break;

        if (buffered.Count <= threshold)
        {
            foreach (var row in await ApplyPivot(buffered, pivot))
                yield return row;
            yield break;
        }

        _logger.WriteLine($"[yellow]HYPER-SCALE: PIVOT input exceeded {threshold:N0} rows. Switching to spill-backed filtered aggregation.[/]");
        var groupingCols = GetGroupingColumns(buffered[0], pivot);
        var groupBy = groupingCols.Select(c => (Expression)new IdentifierExpression(c)).ToList();
        var finalColumns = groupingCols
            .Select(c => new SelectColumn(new IdentifierExpression(c), c))
            .ToList();
        var colNames = groupingCols.ToList();

        foreach (var valueExpression in pivot.PivotValues)
        {
            var pivotValue = await _context.EvaluateValue(valueExpression, new Row());
            var targetName = pivotValue?.ToString() ?? "NULL";
            var arguments = pivot.AggregateColumn == "*"
                ? new List<Expression>()
                : new List<Expression> { new IdentifierExpression(pivot.AggregateColumn) };
            var aggregate = new FunctionCallExpression(pivot.AggregateFunction, arguments)
            {
                Filter = new BinaryExpression(
                    new IdentifierExpression(pivot.PivotColumn),
                    TokenType.EQUALS,
                    valueExpression)
            };
            finalColumns.Add(new SelectColumn(aggregate, targetName));
            colNames.Add(targetName);
        }

        async IAsyncEnumerable<Row> ReplayRows()
        {
            foreach (var row in buffered)
                yield return row;
            while (await enumerator.MoveNextAsync())
                yield return enumerator.Current;
        }

        var externalAggregate = new ExternalAggregateEngine(_context, _logger);
        await foreach (var row in externalAggregate.ApplyAggregationExternal(
            ReplayRows(), groupBy, finalColumns, colNames))
            yield return row;
    }

    public async IAsyncEnumerable<Row> ApplyUnpivotStream(IAsyncEnumerable<Row> rows, UnpivotClause unpivot)
    {
        await using var enumerator = rows.GetAsyncEnumerator();
        if (!await enumerator.MoveNextAsync())
            yield break;

        var first = enumerator.Current;
        var allCols = first.Columns.Keys.ToList();
        var colsToKeep = allCols.Where(c => !unpivot.UnpivotColumns.Any(uc => IsMatch(c, uc))).ToList();

        foreach (var row in UnpivotRow(first, colsToKeep, unpivot))
            yield return row;

        while (await enumerator.MoveNextAsync())
        {
            foreach (var row in UnpivotRow(enumerator.Current, colsToKeep, unpivot))
                yield return row;
        }
    }

    private IEnumerable<Row> UnpivotRow(Row row, List<string> colsToKeep, UnpivotClause unpivot)
    {
        foreach (var unpivotCol in unpivot.UnpivotColumns)
        {
            var newRow = new Row();
            foreach (var col in colsToKeep) newRow[col] = row[col];

            newRow[unpivot.NameColumn] = unpivotCol;
            newRow[unpivot.ValueColumn] = FindValue(row, unpivotCol);
            yield return newRow;
        }
    }

    private static List<string> GetGroupingColumns(Row row, PivotClause pivot)
    {
        var rawGroupingCols = row.Columns.Keys.Where(c =>
            !c.Equals(pivot.PivotColumn, StringComparison.OrdinalIgnoreCase) &&
            !c.Equals(pivot.AggregateColumn, StringComparison.OrdinalIgnoreCase) &&
            !IsColumnMatch(c, pivot.PivotColumn) &&
            !IsColumnMatch(c, pivot.AggregateColumn));
        var groupingCols = new List<string>();
        var seenBaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in rawGroupingCols.OrderBy(c => c.Contains('.') ? 1 : 0))
        {
            var baseName = col.Contains('.') ? col.Split('.').Last() : col;
            if (seenBaseNames.Add(baseName))
                groupingCols.Add(col);
        }
        return groupingCols;
    }

    private static bool IsColumnMatch(string fullColName, string targetColName)
    {
        return fullColName.Equals(targetColName, StringComparison.OrdinalIgnoreCase)
            || fullColName.EndsWith("." + targetColName, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsMatch(string fullColName, string targetColName)
    {
        if (fullColName.Equals(targetColName, StringComparison.OrdinalIgnoreCase)) return true;
        if (fullColName.EndsWith("." + targetColName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private object? FindValue(Row row, string colName)
    {
        if (row.Columns.TryGetValue(colName, out var val)) return val;
        var match = row.Columns.Keys.FirstOrDefault(k => k.EndsWith("." + colName, StringComparison.OrdinalIgnoreCase));
        if (match != null) return row[match];
        return null;
    }
}

