using System;
using System.Collections.Generic;
using ETL_SQL.Data;

namespace ETL_SQL.Engine.Engines;

/// <summary>
/// Qualifies rows from one source as <c>from.col</c> without per-row name work. For a row held
/// positionally by its schema, the qualified schema (canonical names, then a real
/// <c>from.col</c> column mirroring each unqualified canonical slot) is built once per source
/// schema and each row becomes one values array over it. The per-row alternative, a clone plus one
/// interpolated name and dynamic-dictionary entry per column, was most of the allocation of the
/// Gate F round trip and of the external join and window pipelines.
/// Not thread-safe: use one instance per row stream.
/// </summary>
internal sealed class QualifiedRowLayout(string fromName)
{
    private TableSchema? _source;
    private TableSchema? _qualified;
    private int[] _slots = Array.Empty<int>();

    /// <summary>
    /// Returns the qualified values row, or null when the row has no schema or carries dynamic
    /// columns (callers keep their per-column path for those). Only the values are set; the caller
    /// copies row-level state such as <see cref="Row.MayLackColumns"/> when the row replaces the
    /// original.
    /// </summary>
    public Row? TryQualify(Row row)
    {
        var schema = row.Schema;
        if (schema == null || row.HasDynamicColumns) return null;
        if (!ReferenceEquals(schema, _source))
        {
            var names = new List<string>(schema.ColumnCount * 2);
            var slots = new List<int>(schema.ColumnCount);
            for (int i = 0; i < schema.ColumnCount; i++) names.Add(schema.GetName(i));
            for (int i = 0; i < schema.ColumnCount; i++)
            {
                var name = schema.GetName(i);
                if (name.Contains('.')) continue;
                names.Add($"{fromName}.{name}");
                slots.Add(i);
            }
            _qualified = new TableSchema(names);
            schema.CopyAliasesTo(_qualified);
            _slots = slots.ToArray();
            _source = schema;
        }

        int canonical = schema.ColumnCount;
        var values = new object?[canonical + _slots.Length];
        for (int i = 0; i < canonical; i++) values[i] = row[i];
        for (int i = 0; i < _slots.Length; i++) values[canonical + i] = values[_slots[i]];
        return new Row(_qualified!, values);
    }
}
