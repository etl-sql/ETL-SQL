using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Handlers;

/// <summary>
/// Handles the <c>ASSERT TABLE &lt;actual&gt; MATCHES &lt;expected&gt; [WITH (...)]</c> statement.
/// Asserts that two tables or #temp datasets have identical structure and data, with support for
/// numeric tolerances, column exclusions, and order-insensitive multiset comparison.
/// </summary>
public class AssertTableStatementHandler(ILogger logger) : IStatementHandler
{
    private readonly ILogger _logger = logger;

    public Type SupportedStatementType => typeof(AssertTableStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (AssertTableStatement)statement;
        _logger.Debug($"Evaluating ASSERT TABLE {stmt.ActualTable} MATCHES {stmt.ExpectedTable}");

        var actualDs = await context.ResolveDataSourceAsync(new TableReference(stmt.ActualTable));
        if (actualDs == null)
            throw new ExecutionException($"ASSERT TABLE failed: actual table '{stmt.ActualTable}' does not exist or could not be resolved.");

        var expectedDs = await context.ResolveDataSourceAsync(new TableReference(stmt.ExpectedTable));
        if (expectedDs == null)
            throw new ExecutionException($"ASSERT TABLE failed: expected table '{stmt.ExpectedTable}' does not exist or could not be resolved.");

        var actualColumns = (await actualDs.GetColumnsAsync(context.CancellationToken)).ToList();
        var expectedColumns = (await expectedDs.GetColumnsAsync(context.CancellationToken)).ToList();

        var ignoreCols = new HashSet<string>(stmt.IgnoreColumns ?? [], StringComparer.OrdinalIgnoreCase);

        var filteredActualCols = actualColumns.Where(c => !ignoreCols.Contains(c)).ToList();
        var filteredExpectedCols = expectedColumns.Where(c => !ignoreCols.Contains(c)).ToList();

        // 1. Schema / Column comparison
        var extraColsInActual = filteredActualCols
            .Where(a => !filteredExpectedCols.Contains(a, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var missingColsInActual = filteredExpectedCols
            .Where(e => !filteredActualCols.Contains(e, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (extraColsInActual.Count > 0 || missingColsInActual.Count > 0)
        {
            var diff = new StringBuilder();
            diff.AppendLine($"ASSERT TABLE schema mismatch between '{stmt.ActualTable}' and '{stmt.ExpectedTable}':");
            if (missingColsInActual.Count > 0)
                diff.AppendLine($"  - Missing in actual: {string.Join(", ", missingColsInActual)}");
            if (extraColsInActual.Count > 0)
                diff.AppendLine($"  - Extra in actual:   {string.Join(", ", extraColsInActual)}");

            var message = await FormatErrorMessage(stmt, diff.ToString().TrimEnd(), context);
            _logger.Error($"[ASSERT TABLE FAIL] {message}");
            throw new ExecutionException(message);
        }

        // Columns to compare (using expected column order as standard)
        var compareColumns = filteredExpectedCols;

        // 2. Read rows from both data sources
        var actualRows = new List<Row>();
        await foreach (var batch in actualDs.ReadBatches(1000, context.CancellationToken))
        {
            actualRows.AddRange(batch.Rows);
        }

        var expectedRows = new List<Row>();
        await foreach (var batch in expectedDs.ReadBatches(1000, context.CancellationToken))
        {
            expectedRows.AddRange(batch.Rows);
        }

        // 3. Row count check & row comparison
        var mismatches = new List<string>();

        if (stmt.IgnoreOrder)
        {
            // Multiset matching: match each actual row to an unused matching expected row
            var matchedExpectedIndices = new HashSet<int>();
            var unmatchedActualRows = new List<(int Index, Row Row)>();

            for (int i = 0; i < actualRows.Count; i++)
            {
                var actualRow = actualRows[i];
                int matchedExpectedIdx = -1;

                for (int j = 0; j < expectedRows.Count; j++)
                {
                    if (matchedExpectedIndices.Contains(j)) continue;
                    if (RowsMatch(actualRow, expectedRows[j], compareColumns, stmt.Tolerance))
                    {
                        matchedExpectedIdx = j;
                        break;
                    }
                }

                if (matchedExpectedIdx >= 0)
                {
                    matchedExpectedIndices.Add(matchedExpectedIdx);
                }
                else
                {
                    unmatchedActualRows.Add((i, actualRow));
                }
            }

            var unmatchedExpectedRows = expectedRows
                .Select((r, idx) => (Index: idx, Row: r))
                .Where(x => !matchedExpectedIndices.Contains(x.Index))
                .ToList();

            if (unmatchedActualRows.Count > 0 || unmatchedExpectedRows.Count > 0 || actualRows.Count != expectedRows.Count)
            {
                var diff = new StringBuilder();
                diff.AppendLine($"ASSERT TABLE data mismatch between '{stmt.ActualTable}' ({actualRows.Count} rows) and '{stmt.ExpectedTable}' ({expectedRows.Count} rows) (IGNORE_ORDER=TRUE):");
                if (actualRows.Count != expectedRows.Count)
                    diff.AppendLine($"  - Row count mismatch: actual has {actualRows.Count}, expected has {expectedRows.Count}.");

                if (unmatchedActualRows.Count > 0)
                {
                    diff.AppendLine($"  - Unexpected rows in actual (showing up to 5):");
                    foreach (var (idx, row) in unmatchedActualRows.Take(5))
                        diff.AppendLine($"      [actual row {idx + 1}] {FormatRow(row, compareColumns)}");
                }

                if (unmatchedExpectedRows.Count > 0)
                {
                    diff.AppendLine($"  - Missing rows from actual (showing up to 5):");
                    foreach (var (idx, row) in unmatchedExpectedRows.Take(5))
                        diff.AppendLine($"      [expected row {idx + 1}] {FormatRow(row, compareColumns)}");
                }

                var message = await FormatErrorMessage(stmt, diff.ToString().TrimEnd(), context);
                _logger.Error($"[ASSERT TABLE FAIL] {message}");
                throw new ExecutionException(message);
            }
        }
        else
        {
            // Ordered comparison: compare row index by row index
            int minCount = Math.Min(actualRows.Count, expectedRows.Count);
            for (int i = 0; i < minCount; i++)
            {
                var actualRow = actualRows[i];
                var expectedRow = expectedRows[i];

                var cellDiffs = new List<string>();
                foreach (var col in compareColumns)
                {
                    var actualVal = actualRow[col];
                    var expectedVal = expectedRow[col];

                    if (!ValuesMatch(actualVal, expectedVal, stmt.Tolerance))
                    {
                        cellDiffs.Add($"{col}: actual={FormatValue(actualVal)}, expected={FormatValue(expectedVal)}");
                    }
                }

                if (cellDiffs.Count > 0)
                {
                    mismatches.Add($"  - Row {i + 1} mismatch: {string.Join("; ", cellDiffs)}");
                    if (mismatches.Count >= 10) break;
                }
            }

            if (mismatches.Count > 0 || actualRows.Count != expectedRows.Count)
            {
                var diff = new StringBuilder();
                diff.AppendLine($"ASSERT TABLE data mismatch between '{stmt.ActualTable}' ({actualRows.Count} rows) and '{stmt.ExpectedTable}' ({expectedRows.Count} rows):");
                if (actualRows.Count != expectedRows.Count)
                    diff.AppendLine($"  - Row count mismatch: actual has {actualRows.Count}, expected has {expectedRows.Count}.");

                foreach (var m in mismatches)
                    diff.AppendLine(m);

                if (mismatches.Count == 0 && actualRows.Count > expectedRows.Count)
                {
                    diff.AppendLine($"  - Actual has {actualRows.Count - expectedRows.Count} extra row(s) starting at row {expectedRows.Count + 1}:");
                    for (int i = expectedRows.Count; i < Math.Min(actualRows.Count, expectedRows.Count + 5); i++)
                        diff.AppendLine($"      [actual row {i + 1}] {FormatRow(actualRows[i], compareColumns)}");
                }
                else if (mismatches.Count == 0 && expectedRows.Count > actualRows.Count)
                {
                    diff.AppendLine($"  - Actual is missing {expectedRows.Count - actualRows.Count} row(s) starting at row {actualRows.Count + 1}:");
                    for (int i = actualRows.Count; i < Math.Min(expectedRows.Count, actualRows.Count + 5); i++)
                        diff.AppendLine($"      [expected row {i + 1}] {FormatRow(expectedRows[i], compareColumns)}");
                }

                var message = await FormatErrorMessage(stmt, diff.ToString().TrimEnd(), context);
                _logger.Error($"[ASSERT TABLE FAIL] {message}");
                throw new ExecutionException(message);
            }
        }

        _logger.Info($"[ASSERT TABLE PASS] '{stmt.ActualTable}' matches '{stmt.ExpectedTable}' ({actualRows.Count} rows verified).");
    }

    private static bool RowsMatch(Row r1, Row r2, IReadOnlyList<string> columns, decimal? tolerance)
    {
        foreach (var col in columns)
        {
            if (!ValuesMatch(r1[col], r2[col], tolerance))
                return false;
        }
        return true;
    }

    private static bool ValuesMatch(object? v1, object? v2, decimal? tolerance)
    {
        if (v1 == null && v2 == null) return true;
        if (v1 == null || v2 == null) return false;

        // Try numeric comparison if tolerance or numeric values
        if (TryGetDecimal(v1, out var d1) && TryGetDecimal(v2, out var d2))
        {
            if (tolerance.HasValue)
                return Math.Abs(d1 - d2) <= tolerance.Value;
            return d1 == d2;
        }

        // Try boolean comparison
        if (v1 is bool b1 && v2 is bool b2)
            return b1 == b2;

        // String comparison
        var s1 = v1.ToString() ?? "";
        var s2 = v2.ToString() ?? "";
        return string.Equals(s1, s2, StringComparison.Ordinal);
    }

    private static bool TryGetDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal d:
                result = d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case short s:
                result = s;
                return true;
            case byte b:
                result = b;
                return true;
            case double dbl:
                if (double.IsNaN(dbl) || double.IsInfinity(dbl)) { result = 0; return false; }
                result = (decimal)dbl;
                return true;
            case float flt:
                if (float.IsNaN(flt) || float.IsInfinity(flt)) { result = 0; return false; }
                result = (decimal)flt;
                return true;
            case string str when decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static string FormatRow(Row row, IReadOnlyList<string> columns)
    {
        var parts = columns.Select(c => $"{c}={FormatValue(row[c])}");
        return $"({string.Join(", ", parts)})";
    }

    private static string FormatValue(object? value)
    {
        if (value == null) return "NULL";
        if (value is string s) return $"'{s}'";
        return value.ToString() ?? "NULL";
    }

    private static async Task<string> FormatErrorMessage(AssertTableStatement stmt, string detailedDiff, IExecutionContext context)
    {
        if (stmt.Message != null)
        {
            var userMsgVal = await context.EvaluateValue(stmt.Message, Row.Empty);
            var userMsg = userMsgVal?.ToString();
            if (!string.IsNullOrWhiteSpace(userMsg))
                return $"{userMsg}\n{detailedDiff}";
        }
        return detailedDiff;
    }
}
