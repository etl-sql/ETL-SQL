using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace ETL_SQL.Reporting.Builders;

/// <summary>Builds the renderer-neutral pivot payload consumed by the browser matrix table.</summary>
internal static class MatrixPivotBuilder
{
    private const string Separator = "\u001F";

    public static string Build(VisualManifest visual)
    {
        var rowColumns = Roles(visual, "row", "row1", "row2", "row3");
        if (rowColumns.Count == 0 && visual.Columns.Count > 0) rowColumns.Add(visual.Columns[0]);

        var columnColumns = Roles(visual, "col", "col1", "columns", "col2", "col3");
        if (columnColumns.Count == 0 && visual.Columns.Count > 1) columnColumns.Add(visual.Columns[1]);

        var valueColumns = Roles(visual, "value", "value2", "value3", "value4", "value5")
            .Select(column => (Column: column, Index: ColumnIndex(visual, column)))
            .ToList();
        if (valueColumns.Count == 0 && visual.Columns.Count > 2)
            valueColumns.Add((visual.Columns[2], 2));

        var valueCount = Math.Max(1, valueColumns.Count);
        var aggregate = (visual.Options.GetValueOrDefault("AGGREGATE") ?? "SUM").ToUpperInvariant();
        var rowIndices = rowColumns.Select(column => ColumnIndex(visual, column)).ToList();
        var columnIndices = columnColumns.Select(column => ColumnIndex(visual, column)).ToList();
        var groups = new Dictionary<string, Dictionary<string, List<double>[]>>(StringComparer.Ordinal);
        var rowOrder = new List<string>();
        var columnOrder = new List<string>();
        var seenColumns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in visual.Rows)
        {
            var rowKey = string.Join(Separator, rowIndices.Select(index => Cell(row, index)));
            var columnKey = string.Join(Separator, columnIndices.Select(index => Cell(row, index)));
            if (!groups.TryGetValue(rowKey, out var rowGroup))
            {
                rowGroup = new Dictionary<string, List<double>[]>(StringComparer.Ordinal);
                groups[rowKey] = rowGroup;
                rowOrder.Add(rowKey);
            }
            if (!rowGroup.TryGetValue(columnKey, out var measures))
            {
                measures = Enumerable.Range(0, valueCount).Select(_ => new List<double>()).ToArray();
                rowGroup[columnKey] = measures;
            }
            if (seenColumns.Add(columnKey)) columnOrder.Add(columnKey);
            for (var index = 0; index < valueColumns.Count; index++)
                measures[index].Add(Number(Cell(row, valueColumns[index].Index)));
        }

        var columnKeys = ((visual.Options.GetValueOrDefault("AXIS_SORT") ?? "ALPHA")
                .Equals("DESC", StringComparison.OrdinalIgnoreCase)
            ? columnOrder.OrderByDescending(key => groups.Values.Sum(group =>
                group.TryGetValue(key, out var values) ? values[0].Sum() : 0d))
            : columnOrder.OrderBy(key => key, StringComparer.OrdinalIgnoreCase)).ToList();
        var columnParts = columnKeys.Select(key => key.Split(Separator).ToList()).ToList();
        var rows = rowOrder.Select(rowKey => rowKey.Split(Separator).Cast<string?>()
            .Concat(columnKeys.SelectMany(key => Enumerable.Range(0, valueCount).Select(index =>
                Aggregate(groups[rowKey].GetValueOrDefault(key)?.ElementAtOrDefault(index), aggregate))))
            .ToList()).ToList();

        List<string?>? grandTotals = null;
        if (IsOn(visual.Options.GetValueOrDefault("GRAND_TOTAL")))
        {
            grandTotals = Enumerable.Repeat<string?>(null, rowColumns.Count)
                .Concat(columnKeys.SelectMany(key => Enumerable.Range(0, valueCount).Select(index =>
                    Aggregate(groups.Values.SelectMany(group =>
                        group.TryGetValue(key, out var values) && index < values.Length
                            ? values[index]
                            : []).ToList(), aggregate))))
                .ToList();
        }

        return JsonSerializer.Serialize(new
        {
            __matrix = true,
            rowHeaders = rowColumns,
            colHeaders = columnColumns,
            colParts = columnParts,
            aggregate,
            rows,
            grandTotals,
            valueHeaders = valueCount > 1 ? valueColumns.Select(value => value.Column).ToArray() : null,
            subtotalsEnabled = IsOn(visual.Options.GetValueOrDefault("SUBTOTALS"))
        });
    }

    private static List<string> Roles(VisualManifest visual, params string[] roles) => roles
        .Select(role => visual.Options.GetValueOrDefault("mapping:" + role))
        .Where(column => !string.IsNullOrWhiteSpace(column))
        .Select(column => column!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static int ColumnIndex(VisualManifest visual, string column) =>
        visual.Columns.FindIndex(candidate => candidate.Equals(column, StringComparison.OrdinalIgnoreCase));

    private static string Cell(IReadOnlyList<string?> row, int index) =>
        index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;

    private static double Number(string value) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0d;

    private static string? Aggregate(IReadOnlyCollection<double>? values, string aggregate)
    {
        if (values is null || values.Count == 0) return null;
        return aggregate switch
        {
            "COUNT" => values.Count.ToString(CultureInfo.InvariantCulture),
            "AVG" => (values.Sum() / values.Count).ToString("G6", CultureInfo.InvariantCulture),
            "MIN" => values.Min().ToString("G6", CultureInfo.InvariantCulture),
            "MAX" => values.Max().ToString("G6", CultureInfo.InvariantCulture),
            _ => values.Sum().ToString("G6", CultureInfo.InvariantCulture)
        };
    }

    private static bool IsOn(string? value) =>
        value?.Equals("ON", StringComparison.OrdinalIgnoreCase) == true ||
        value?.Equals("TRUE", StringComparison.OrdinalIgnoreCase) == true;
}
