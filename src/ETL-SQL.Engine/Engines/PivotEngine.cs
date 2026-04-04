using ETL_SQL.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.Engine.Engines
{
    /// <summary>
    /// Handles PIVOT and UNPIVOT operations, transforming data between long and wide formats.
    /// </summary>
    public class PivotEngine
    {
        private readonly IExecutionContext _context;
        private readonly AggregateEngine _aggregateEngine;

        public PivotEngine(IExecutionContext context)
        {
            _context = context;
            _aggregateEngine = new AggregateEngine(context);
        }

        /// <summary>Transforms a list of rows into a pivoted format based on the specified pivot clause.</summary>
        public async Task<List<Row>> ApplyPivot(List<Row> rows, PivotClause pivot)
        {
            if (rows.Count == 0) return rows;

            
            
            // 1. Identify grouping columns (all columns except AggregateColumn and PivotColumn)
            var allCols = rows[0].Columns.Keys.ToList();
            var groupingCols = allCols.Where(c => 
                !c.Equals(pivot.PivotColumn, StringComparison.OrdinalIgnoreCase) && 
                !c.Equals(pivot.AggregateColumn, StringComparison.OrdinalIgnoreCase) &&
                !IsMatch(c, pivot.PivotColumn) &&
                !IsMatch(c, pivot.AggregateColumn)
            ).ToList();

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
                    
                    var filteredRows = groupRows.Where(r => {
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
}

