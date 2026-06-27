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

        var unpivotCols = ResolveUnpivotColumns(rows[0], unpivot);
        var resultRows = new List<Row>();
        var allCols = rows[0].Columns.Keys.ToList();
        var colsToKeep = allCols.Where(c => !unpivotCols.Any(uc => IsMatch(c, uc))).ToList();

        foreach (var row in rows)
        {
            foreach (var unpivotCol in unpivotCols)
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

    /// <summary>
    /// Resolves the effective set of columns to unpivot. For DuckDB <c>ON COLUMNS(* EXCLUDE (...))</c>
    /// this is every source column except the excluded ones and the generated name/value columns;
    /// otherwise it is the explicit list.
    /// </summary>
    private List<string> ResolveUnpivotColumns(Row firstRow, UnpivotClause unpivot)
    {
        if (!unpivot.AllColumnsExcept) return unpivot.UnpivotColumns;
        var excludes = unpivot.ExcludeColumns ?? new List<string>();
        var result = new List<string>();
        var seenBase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Prefer unprefixed names; dedupe prefixed/unprefixed pairs (e.g. "t.q1" vs "q1") by base name.
        foreach (var c in firstRow.Columns.Keys.OrderBy(k => k.Contains('.') ? 1 : 0))
        {
            if (c.Equals(unpivot.NameColumn, StringComparison.OrdinalIgnoreCase)
                || c.Equals(unpivot.ValueColumn, StringComparison.OrdinalIgnoreCase)) continue;
            if (excludes.Any(e => IsMatch(c, e) || c.Equals(e, StringComparison.OrdinalIgnoreCase))) continue;
            var baseName = c.Contains('.') ? c.Split('.').Last() : c;
            if (seenBase.Add(baseName)) result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// DuckDB-style PIVOT: supports multiple ON columns, multiple aggregates, dynamic value discovery,
    /// and an explicit or implicit row grouping. Implemented by synthesizing one FILTER-ed aggregate per
    /// (value-combination × aggregate) and delegating to the standard aggregate engine.
    /// </summary>
    public async Task<List<Row>> ApplyDuckPivot(List<Row> rows, DuckPivotClause pivot)
    {
        if (rows.Count == 0) return rows;

        var aggCols = pivot.Aggregates
            .Where(a => a.Column != null && a.Column != "*")
            .Select(a => a.Column!)
            .ToList();

        // Row (grouping) columns: explicit GROUP BY, else all columns not consumed by ON or the aggregates.
        List<string> rawGroupingCols = pivot.GroupByColumns ?? rows[0].Columns.Keys
            .Where(c => !pivot.OnColumns.Any(on => IsMatch(c, on) || c.Equals(on, StringComparison.OrdinalIgnoreCase)))
            .Where(c => !aggCols.Any(ac => IsMatch(c, ac) || c.Equals(ac, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Dedupe prefixed/unprefixed pairs (e.g. "t.region" vs "region"), preferring unprefixed.
        var groupingCols = new List<string>();
        var seenBase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in rawGroupingCols.OrderBy(k => k.Contains('.') ? 1 : 0))
        {
            var bn = c.Contains('.') ? c.Split('.').Last() : c;
            if (seenBase.Add(bn)) groupingCols.Add(c);
        }
        static string CleanName(string c) => c.Contains('.') ? c.Split('.').Last() : c;

        // Pivot value combinations: explicit IN list (single ON column) or distinct combinations from data.
        List<List<object?>> combos;
        if (pivot.InValues != null)
        {
            combos = new List<List<object?>>();
            foreach (var v in pivot.InValues)
                combos.Add(new List<object?> { await _context.EvaluateValue(v, new Row()) });
        }
        else
        {
            var seen = new Dictionary<string, List<object?>>();
            foreach (var row in rows)
            {
                var combo = pivot.OnColumns.Select(on => row[on]).ToList();
                var key = string.Join("", combo.Select(x => x?.ToString() ?? " "));
                if (!seen.ContainsKey(key)) seen[key] = combo;
            }
            combos = seen.Values.OrderBy(c => c, Comparer<List<object?>>.Create(CompareCombo)).ToList();
        }

        bool suffixAgg = pivot.Aggregates.Count > 1;
        var groupByExprs = groupingCols.Select(c => (Expression)new IdentifierExpression(c)).ToList();
        var finalColumns = groupingCols.Select(c => new SelectColumn(new IdentifierExpression(c), c)).ToList();
        var colNames = new List<string>(groupingCols);

        foreach (var combo in combos)
        {
            Expression? comboFilter = null;
            for (int i = 0; i < pivot.OnColumns.Count; i++)
            {
                var eq = new BinaryExpression(new IdentifierExpression(pivot.OnColumns[i]), TokenType.EQUALS, LiteralFor(combo[i]));
                comboFilter = comboFilter == null ? eq : new BinaryExpression(comboFilter, TokenType.AND, eq);
            }

            var comboName = string.Join("_", combo.Select(v => v?.ToString() ?? "NULL"));
            foreach (var agg in pivot.Aggregates)
            {
                var args = (agg.Column == null || agg.Column == "*")
                    ? new List<Expression>()
                    : new List<Expression> { new IdentifierExpression(agg.Column) };
                var aggExpr = new FunctionCallExpression(agg.Function, args) { Filter = comboFilter };
                var outName = suffixAgg ? $"{comboName}_{agg.Alias ?? agg.Function.ToLowerInvariant()}" : comboName;
                finalColumns.Add(new SelectColumn(aggExpr, outName));
                colNames.Add(outName);
            }
        }

        var aggregated = await _aggregateEngine.ApplyAggregation(rows.ToAsyncEnumerable(), groupByExprs, finalColumns, colNames);

        // Final projection: expose only the declared output columns with clean (unprefixed) grouping
        // names, dropping the aggregate engine's AGG_* intermediate columns.
        var pivotOutNames = colNames.Skip(groupingCols.Count).ToList();
        var projected = new List<Row>(aggregated.Count);
        foreach (var r in aggregated)
        {
            var nr = new Row();
            foreach (var g in groupingCols) nr[CleanName(g)] = r[g];
            foreach (var pn in pivotOutNames) nr[pn] = r[pn];
            projected.Add(nr);
        }
        return projected;
    }

    private static int CompareCombo(List<object?>? a, List<object?>? b)
    {
        if (a == null || b == null) return 0;
        for (int i = 0; i < a.Count && i < b.Count; i++)
        {
            var (x, y) = (a[i], b[i]);
            if (x == null && y == null) continue;
            if (x == null) return -1;
            if (y == null) return 1;
            int cmp = decimal.TryParse(x.ToString(), out var dx) && decimal.TryParse(y.ToString(), out var dy)
                ? dx.CompareTo(dy)
                : string.Compare(x.ToString(), y.ToString(), StringComparison.Ordinal);
            if (cmp != 0) return cmp;
        }
        return a.Count.CompareTo(b.Count);
    }

    private static Expression LiteralFor(object? value) => value switch
    {
        null => new LiteralExpression(null, TokenType.NULL),
        bool b => new LiteralExpression(b, TokenType.NUMBER),
        decimal or int or long or double or float or short or byte => new LiteralExpression(Convert.ToDecimal(value), TokenType.NUMBER),
        _ => new LiteralExpression(value.ToString(), TokenType.STRING)
    };

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
        var unpivotCols = ResolveUnpivotColumns(first, unpivot);
        var allCols = first.Columns.Keys.ToList();
        var colsToKeep = allCols.Where(c => !unpivotCols.Any(uc => IsMatch(c, uc))).ToList();

        foreach (var row in UnpivotRow(first, colsToKeep, unpivotCols, unpivot))
            yield return row;

        while (await enumerator.MoveNextAsync())
        {
            foreach (var row in UnpivotRow(enumerator.Current, colsToKeep, unpivotCols, unpivot))
                yield return row;
        }
    }

    private IEnumerable<Row> UnpivotRow(Row row, List<string> colsToKeep, List<string> unpivotCols, UnpivotClause unpivot)
    {
        foreach (var unpivotCol in unpivotCols)
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

