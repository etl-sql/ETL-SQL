using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ETL_SQL.Reporting.Semantics;

namespace ETL_SQL.Reporting.Semantics.Runtime;

public sealed class VisualChartDataBuilder
{
    public ChartDataSet Build(ChartSpec spec, VisualManifest manifest)
    {
        var semanticByField = spec.Bindings.Concat(spec.Layers.SelectMany(layer => layer.Bindings))
            .Where(binding => binding.SourceKind == BindingSourceKind.Field && binding.Field is not null)
            .GroupBy(binding => binding.Field!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().SemanticKind, StringComparer.OrdinalIgnoreCase);
        var formatter = new ChartValueFormatter(spec.Formatting);
        var columns = new List<ChartColumn>();
        for (var columnIndex = 0; columnIndex < manifest.Columns.Count; columnIndex++)
        {
            var name = manifest.Columns[columnIndex];
            var rawValues = Enumerable.Range(0, manifest.Rows.Count)
                .Select(rowIndex => RawValue(manifest, rowIndex, columnIndex))
                .ToList();
            var kind = InferKind(rawValues, semanticByField.GetValueOrDefault(name));
            var values = rawValues.Select(value => ConvertValue(value, kind)).ToImmutableArray();
            // COMPAT_BREAK: 0.19
            // Date and time columns are rendered by the chart formatter rather than reused from the
            // engine's row strings: only the formatter knows the report's time zone and locale, and an
            // instant printed in the server's zone is exactly the browser-dependent output the
            // deterministic-formatting contract forbids. Every other column keeps its engine rendering,
            // which already honours the author's MAPPINGS format.
            var display = IsTemporal(kind)
                ? values.Select(value => (string?)formatter.Format(value, name)).ToImmutableArray()
                : manifest.Rows.Select(row => columnIndex < row.Count ? row[columnIndex] : null).ToImmutableArray();
            columns.Add(new ChartColumn(name, kind, semanticByField.GetValueOrDefault(name, DataSemanticKind.Nominal), values, display));
        }

        if (!columns.Any(c => c.Name.Equals("__row_id", StringComparison.OrdinalIgnoreCase)))
        {
            var rowIndices = Enumerable.Range(0, manifest.Rows.Count).Select(i => ChartValue.From((decimal)i)).ToImmutableArray();
            var rowDisplays = Enumerable.Range(0, manifest.Rows.Count).Select(i => (string?)i.ToString()).ToImmutableArray();
            columns.Add(new ChartColumn("__row_id", ChartValueKind.Decimal, DataSemanticKind.Nominal, rowIndices, rowDisplays));
        }

        var data = ChartDataSet.Create(spec.DataReference, manifest.Rows.Count, columns.ToImmutableArray());
        data.Validate();
        return data;
    }

    private static bool IsTemporal(ChartValueKind kind) =>
        kind is ChartValueKind.Date or ChartValueKind.Time or ChartValueKind.LocalDateTime or ChartValueKind.OffsetDateTime;

    private static object? RawValue(VisualManifest manifest, int rowIndex, int columnIndex)
    {
        if (rowIndex < manifest.RawRows.Count && manifest.RawRows[rowIndex].TryGetValue(manifest.Columns[columnIndex], out var raw))
            return raw;
        return columnIndex < manifest.Rows[rowIndex].Count ? manifest.Rows[rowIndex][columnIndex] : null;
    }

    private static ChartValueKind InferKind(IReadOnlyList<object?> values, DataSemanticKind? semantic)
    {
        var first = values.FirstOrDefault(value => value is not null && value is not DBNull);
        if (first is null) return semantic == DataSemanticKind.Quantitative ? ChartValueKind.Decimal : ChartValueKind.Text;
        return first switch
        {
            byte or sbyte or short or ushort or int or uint or long or ulong => ChartValueKind.Integer,
            float or double => ChartValueKind.FloatingPoint,
            decimal => ChartValueKind.Decimal,
            DateOnly => ChartValueKind.Date,
            TimeOnly or TimeSpan => ChartValueKind.Time,
            DateTimeOffset => ChartValueKind.OffsetDateTime,
            DateTime => ChartValueKind.LocalDateTime,
            bool => ChartValueKind.Boolean,
            _ when semantic == DataSemanticKind.Quantitative && decimal.TryParse(first.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out _) => ChartValueKind.Decimal,
            _ when semantic == DataSemanticKind.Temporal && DateTimeOffset.TryParse(first.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) => ChartValueKind.OffsetDateTime,
            _ => ChartValueKind.Text
        };
    }

    private static ChartValue ConvertValue(object? value, ChartValueKind kind)
    {
        if (value is null || value is DBNull) return ChartValue.Null();
        var text = value.ToString() ?? string.Empty;
        return kind switch
        {
            ChartValueKind.Integer => ChartValue.From(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            ChartValueKind.FloatingPoint => ChartValue.From(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            ChartValueKind.Decimal => ChartValue.From(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
            ChartValueKind.Date => ChartValue.From(value is DateOnly date ? date : DateOnly.Parse(text, CultureInfo.InvariantCulture)),
            ChartValueKind.Time => ChartValue.From(value switch
            {
                TimeOnly time => time,
                TimeSpan span => TimeOnly.FromTimeSpan(span),
                _ => TimeOnly.Parse(text, CultureInfo.InvariantCulture)
            }),
            // COMPAT_BREAK: 0.19
            // AssumeUniversal: a string with no offset must not pick up the server's local offset, or the
            // same report renders different instants on two hosts.
            ChartValueKind.OffsetDateTime => ChartValue.From(value is DateTimeOffset offset
                ? offset
                : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal)),
            ChartValueKind.LocalDateTime => ChartValue.FromLocal(DateTime.SpecifyKind((DateTime)value, DateTimeKind.Unspecified)),
            ChartValueKind.Boolean => ChartValue.From(Convert.ToBoolean(value, CultureInfo.InvariantCulture)),
            _ => ChartValue.From(text)
        };
    }
}
