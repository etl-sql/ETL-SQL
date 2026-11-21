using System;
using System.Collections.Generic;
using ETL_SQL.Data;
using ETL_SQL.Engine.Planning;

namespace ETL_SQL.Engine.Engines;

/// <summary>
/// Qualifies rows from one source as <c>from.col</c> via <see cref="TableSchema.AddAlias"/>
/// without duplicating physical columns, and optionally prunes columns not referenced by the
/// statement. Each row becomes a single values array over the cached schema.
/// Not thread-safe: use one instance per row stream.
/// </summary>
internal sealed class QualifiedRowLayout(string fromName, HashSet<string>? requiredColumns = null)
{
    private TableSchema? _source;
    private TableSchema? _qualified;
    private int[] _keptIndices = Array.Empty<int>();

    /// <summary>
    /// Returns the qualified (and optionally pruned) values row, or null when the row has no schema
    /// or carries dynamic columns (callers keep their per-column path for those). Only the values
    /// are set; the caller copies row-level state such as <see cref="Row.MayLackColumns"/> when
    /// the row replaces the original.
    /// </summary>
    public Row? TryQualify(Row row)
    {
        var schema = row.Schema;
        if (schema == null || row.HasDynamicColumns) return null;
        if (!ReferenceEquals(schema, _source))
        {
            var keptIndices = new List<int>(schema.ColumnCount);
            var names = new List<string>(schema.ColumnCount);
            for (int i = 0; i < schema.ColumnCount; i++)
            {
                var name = schema.GetName(i);
                if (requiredColumns != null && !IsColumnOrAliasRequired(schema, i, name, requiredColumns))
                    continue;
                keptIndices.Add(i);
                names.Add(name);
            }

            // Never prune a non-empty row down to zero columns (e.g. SELECT COUNT(*) FROM t).
            if (keptIndices.Count == 0 && schema.ColumnCount > 0)
            {
                keptIndices.Add(0);
                names.Add(schema.GetName(0));
            }

            var qualified = new TableSchema(names);
            schema.CopyAliasesTo(qualified);
            for (int outIdx = 0; outIdx < names.Count; outIdx++)
            {
                var name = names[outIdx];
                if (!name.Contains('.'))
                    qualified.AddAlias($"{fromName}.{name}", outIdx);
            }

            _qualified = qualified;
            _keptIndices = keptIndices.ToArray();
            _source = schema;
        }

        var values = new object?[_keptIndices.Length];
        for (int i = 0; i < _keptIndices.Length; i++) values[i] = row[_keptIndices[i]];
        return new Row(_qualified!, values);
    }

    internal static bool IsColumnOrAliasRequired(
        TableSchema schema,
        int index,
        string canonicalName,
        HashSet<string> required)
    {
        if (RequiredColumnAnalyzer.IsRequired(canonicalName, required))
            return true;
        foreach (var alias in schema.EnumerateAliasesOf(canonicalName))
        {
            if (RequiredColumnAnalyzer.IsRequired(alias, required))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Projects a row stream down to only columns referenced by downstream clauses (for example,
/// dropping WHERE-only columns before external sort or window spilling).
/// Not thread-safe: use one instance per row stream.
/// </summary>
internal sealed class ProjectedRowLayout(HashSet<string> requiredColumns)
{
    private TableSchema? _source;
    private TableSchema? _projected;
    private int[] _keptIndices = Array.Empty<int>();
    private bool _passThrough;

    public Row Project(Row row)
    {
        var schema = row.Schema;
        if (schema == null || row.HasDynamicColumns) return row;

        if (!ReferenceEquals(schema, _source))
        {
            var keptIndices = new List<int>(schema.ColumnCount);
            var names = new List<string>(schema.ColumnCount);
            for (int i = 0; i < schema.ColumnCount; i++)
            {
                var name = schema.GetName(i);
                if (!QualifiedRowLayout.IsColumnOrAliasRequired(schema, i, name, requiredColumns))
                    continue;
                keptIndices.Add(i);
                names.Add(name);
            }

            if (keptIndices.Count == 0 && schema.ColumnCount > 0)
            {
                keptIndices.Add(0);
                names.Add(schema.GetName(0));
            }

            _passThrough = keptIndices.Count == schema.ColumnCount;
            if (!_passThrough)
            {
                var projected = new TableSchema(names, schema.Constraints);
                schema.CopyAliasesTo(projected);
                _projected = projected;
                _keptIndices = keptIndices.ToArray();
            }
            else
            {
                _projected = null;
                _keptIndices = Array.Empty<int>();
            }
            _source = schema;
        }

        if (_passThrough) return row;

        var values = new object?[_keptIndices.Length];
        for (int i = 0; i < _keptIndices.Length; i++) values[i] = row[_keptIndices[i]];
        return new Row(_projected!, values)
        {
            MayLackColumns = row.MayLackColumns,
            DataQualityReplayProvenance = row.DataQualityReplayProvenance,
        };
    }
}
