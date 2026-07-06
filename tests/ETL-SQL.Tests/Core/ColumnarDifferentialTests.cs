using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class ColumnarDifferentialTests
{
    [Fact]
    public void FixedWidthColumnarKernelsMatchRowReferencePath()
    {
        var definitions = new[]
        {
            new ColumnDefinition("Key", "INT", false),
            new ColumnDefinition("Value", "INT", false)
        };
        var logicalSchema = definitions.ToDictionary(column => column.ColumnName, StringComparer.OrdinalIgnoreCase);
        var rows = new DataTable();
        rows.SetColumns(new[] { "Key", "Value" });
        for (var i = 0; i < 257; i++)
        {
            var row = rows.NewRow();
            row["Key"] = i % 13 == 0 ? null : (decimal)(i % 7);
            row["Value"] = i % 11 == 0 ? DBNull.Value : (decimal)(i - 100);
            rows.Rows.Add(row);
        }

        using var batch = ColumnBatchAdapter.FromDataTable(rows, logicalSchema);
        using var selected = ColumnBatchKernels.SelectArithmeticComparison(
            batch, "Value", ColumnArithmetic.Multiply, 2, ColumnComparison.GreaterThanOrEqual, 40);
        var expectedSelection = rows.Rows
            .Select((row, ordinal) => (row, ordinal))
            .Where(item => item.row["Value"] is not null and not DBNull && Convert.ToInt32(item.row["Value"]) * 2 >= 40)
            .Select(item => item.ordinal)
            .ToArray();
        Assert.Equal(expectedSelection, selected.Indices.ToArray());

        var expectedValues = rows.Rows
            .Select(row => row["Value"])
            .Where(value => value is not null and not DBNull)
            .Select(Convert.ToInt32)
            .ToArray();
        Assert.Equal(expectedValues.LongLength, ColumnBatchKernels.Count(batch, "Value"));
        Assert.Equal(expectedValues.Sum(), ColumnBatchKernels.Sum<int>(batch, "Value"));
        Assert.Equal(expectedValues.Sum(value => (decimal)value), ColumnBatchKernels.SumDecimal<int>(batch, "Value"));
        Assert.Equal(expectedValues.Average(), ColumnBatchKernels.Average<int>(batch, "Value"));
        var range = ColumnBatchKernels.MinMax<int>(batch, "Value");
        Assert.Equal(expectedValues.Min(), range.Min);
        Assert.Equal(expectedValues.Max(), range.Max);

        using var run = ColumnBatchSortKernels.CreateRun<int>(batch, "Key", nullsFirst: false);
        var expectedSort = rows.Rows
            .Select((row, ordinal) => new
            {
                Ordinal = ordinal,
                Key = row["Key"] is null or DBNull ? (int?)null : Convert.ToInt32(row["Key"])
            })
            .OrderBy(item => item.Key == null)
            .ThenBy(item => item.Key)
            .ThenBy(item => item.Ordinal)
            .Select(item => item.Ordinal)
            .ToArray();
        Assert.Equal(expectedSort, run.Ordinals.ToArray());

        using var grouped = ColumnBatchGroupKernels.GroupAggregate<int, int>(batch, "Key", "Value");
        var expectedGroupCounts = rows.Rows
            .GroupBy(row => row["Key"] is null or DBNull ? "NULL" : Convert.ToInt32(row["Key"]).ToString())
            .ToDictionary(group => group.Key, group => group.LongCount());
        foreach (var pair in grouped.Groups)
        {
            var key = pair.Key.IsNull ? "NULL" : pair.Key.Value.ToString();
            Assert.Equal(expectedGroupCounts[key], pair.Value.RowCount);
        }
    }
}
