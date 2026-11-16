using System;
using System.Collections.Generic;
using System.Linq;

namespace ETL_SQL.Core.Services;

/// <summary>
/// Infers output column names, data types, and lineage mappings for <see cref="TransformStatement"/>
/// from the source columns and algorithm options.
/// </summary>
public static class TransformSchemaProjection
{
    public sealed record ComputedColumnSpec(
        string TargetColumn,
        string DataType,
        IReadOnlyList<string> SourceColumns,
        bool ReplacesExisting);

    public static IReadOnlyList<ColumnMetadata> ProjectColumns(
        TransformStatement statement,
        IReadOnlyList<ColumnMetadata> sourceColumns)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(sourceColumns);

        var algorithm = statement.Algorithm.ToUpperInvariant();
        var sourceByName = BuildSourceTypeLookup(sourceColumns);

        if (algorithm == "PIVOT")
        {
            var rowFields = GetOptionColumnList(statement.Options, "ROW_FIELDS");
            return rowFields
                .Select(name => new ColumnMetadata(name, sourceByName.TryGetValue(name, out var t) ? t : "ANY"))
                .ToList();
        }

        var result = new List<ColumnMetadata>(sourceColumns.Count + 4);
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in sourceColumns)
        {
            if (indexByName.TryAdd(col.Name, result.Count))
                result.Add(col);
        }

        foreach (var computed in GetComputedColumns(statement, sourceByName))
        {
            if (indexByName.TryGetValue(computed.TargetColumn, out var existingIdx))
            {
                if (computed.ReplacesExisting)
                    result[existingIdx] = new ColumnMetadata(result[existingIdx].Name, computed.DataType);
            }
            else
            {
                indexByName[computed.TargetColumn] = result.Count;
                result.Add(new ColumnMetadata(computed.TargetColumn, computed.DataType));
            }
        }

        return result;
    }

    public static IReadOnlyList<ComputedColumnSpec> GetComputedColumns(
        TransformStatement statement,
        IReadOnlyDictionary<string, string>? sourceTypes = null)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var algo = statement.Algorithm.ToUpperInvariant();
        var options = statement.Options;
        var specs = new List<ComputedColumnSpec>();

        switch (algo)
        {
            case "INTERPOLATE":
                {
                    var method = TryGetOptionString(options, "METHOD") ?? "LINEAR";
                    var orderCol = TryGetOptionString(options, "ORDER_COL");
                    var valueCols = GetValueColumns(options);
                    foreach (var valCol in valueCols)
                    {
                        var srcType = sourceTypes != null && sourceTypes.TryGetValue(valCol, out var t) ? t : "DECIMAL";
                        var outType = string.Equals(method, "LINEAR", StringComparison.OrdinalIgnoreCase) && IsIntegralType(srcType)
                            ? "DECIMAL"
                            : srcType;
                        var deps = string.IsNullOrEmpty(orderCol) ? [valCol] : new[] { valCol, orderCol };
                        specs.Add(new ComputedColumnSpec(valCol, outType, deps, ReplacesExisting: true));
                    }
                    break;
                }
            case "TOP_N_OTHERS":
                {
                    var valCol = TryGetOptionString(options, "VALUE_COL");
                    var catCol = TryGetOptionString(options, "CATEGORY_COL");
                    var agg = (TryGetOptionString(options, "AGGREGATE") ?? "SUM").ToUpperInvariant();
                    if (!string.IsNullOrEmpty(valCol))
                    {
                        var srcType = sourceTypes != null && sourceTypes.TryGetValue(valCol, out var t) ? t : "DECIMAL";
                        var outType = InferAggregateType(agg, srcType);
                        var deps = string.IsNullOrEmpty(catCol) ? [valCol] : new[] { valCol, catCol };
                        specs.Add(new ComputedColumnSpec(valCol, outType, deps, ReplacesExisting: true));
                    }
                    break;
                }
            case "PERIOD_COMPARISON":
                {
                    var valCol = TryGetOptionString(options, "VALUE_COL");
                    var dateCol = TryGetOptionString(options, "DATE_COL");
                    if (!string.IsNullOrEmpty(valCol))
                    {
                        var diffCol = TryGetOptionString(options, "DIFF_COL") ?? $"{valCol}_Diff";
                        var pctCol = TryGetOptionString(options, "PCT_COL") ?? $"{valCol}_Pct";
                        var deps = string.IsNullOrEmpty(dateCol) ? [valCol] : new[] { valCol, dateCol };
                        specs.Add(new ComputedColumnSpec(diffCol, "DECIMAL", deps, ReplacesExisting: false));
                        specs.Add(new ComputedColumnSpec(pctCol, "DECIMAL", deps, ReplacesExisting: false));
                    }
                    break;
                }
            case "SHARE_OF_TOTAL":
                {
                    var valCol = TryGetOptionString(options, "VALUE_COL");
                    if (!string.IsNullOrEmpty(valCol))
                    {
                        var shareCol = TryGetOptionString(options, "SHARE_COL") ?? $"{valCol}_Share";
                        specs.Add(new ComputedColumnSpec(shareCol, "DECIMAL", [valCol], ReplacesExisting: false));
                    }
                    break;
                }
            case "NORMALIZE":
                {
                    var valueCols = GetValueColumns(options);
                    var normCols = GetOptionColumnList(options, "NORM_COL");
                    for (int i = 0; i < valueCols.Count; i++)
                    {
                        var valCol = valueCols[i];
                        var normCol = i < normCols.Count ? normCols[i] : $"{valCol}_Normalized";
                        specs.Add(new ComputedColumnSpec(normCol, "DECIMAL", [valCol], ReplacesExisting: false));
                    }
                    break;
                }
            case "ROLLING_AGGREGATE":
                {
                    var valCol = TryGetOptionString(options, "VALUE_COL");
                    var orderCol = TryGetOptionString(options, "ORDER_COL");
                    var agg = (TryGetOptionString(options, "AGGREGATE") ?? "AVG").ToUpperInvariant();
                    if (!string.IsNullOrEmpty(valCol))
                    {
                        var rollingCol = TryGetOptionString(options, "ROLLING_COL") ?? $"{valCol}_Rolling";
                        var srcType = sourceTypes != null && sourceTypes.TryGetValue(valCol, out var t) ? t : "DECIMAL";
                        var outType = InferAggregateType(agg, srcType);
                        var deps = string.IsNullOrEmpty(orderCol) ? [valCol] : new[] { valCol, orderCol };
                        specs.Add(new ComputedColumnSpec(rollingCol, outType, deps, ReplacesExisting: false));
                    }
                    break;
                }
        }

        return specs;
    }

    public static bool TryGetRejectIntoTable(TransformStatement statement, out string tableName)
    {
        ArgumentNullException.ThrowIfNull(statement);
        if (string.Equals(statement.Algorithm, "DEDUPLICATE", StringComparison.OrdinalIgnoreCase) &&
            TryGetOptionString(statement.Options, "REJECT_INTO") is { Length: > 0 } raw)
        {
            tableName = raw;
            return true;
        }
        tableName = string.Empty;
        return false;
    }

    public static IReadOnlyList<string> GetReferencedSourceColumns(TransformStatement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var cols = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRange(IEnumerable<string> names)
        {
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                    cols.Add(name);
            }
        }

        foreach (var key in new[]
                 {
                     "DATE_COL", "VALUE_COL", "VALUE_COLS", "ORDER_COL", "BY_GROUP",
                     "KEY_COLS", "ROW_FIELDS", "PIVOT_FIELD", "VALUE_FIELD", "CATEGORY_COL"
                 })
        {
            AddRange(GetOptionColumnList(statement.Options, key));
        }

        if (TryGetOptionString(statement.Options, "ORDER_BY") is { Length: > 0 } orderBy)
        {
            foreach (var part in orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var firstToken = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstToken) && seen.Add(firstToken))
                    cols.Add(firstToken);
            }
        }

        return cols;
    }

    public static string InferAggregateType(string aggregate, string sourceType)
    {
        return aggregate.ToUpperInvariant() switch
        {
            "COUNT" => "INT",
            "MIN" or "MAX" => string.IsNullOrWhiteSpace(sourceType) ? "DECIMAL" : sourceType,
            "AVG" => "DECIMAL",
            "SUM" => !string.IsNullOrWhiteSpace(sourceType)
                     && !sourceType.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
                     && !sourceType.Equals("ANY", StringComparison.OrdinalIgnoreCase)
                ? sourceType
                : "DECIMAL",
            _ => "DECIMAL"
        };
    }

    public static bool IsIntegralType(string? dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return false;
        var baseType = dataType.Split('(')[0].Trim().ToUpperInvariant();
        return baseType is "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT";
    }

    public static string? TryGetOptionString(IReadOnlyDictionary<string, Expression> options, string key)
    {
        if (!options.TryGetValue(key, out var expr))
            return null;

        return expr switch
        {
            LiteralExpression { Value: string s } => s,
            IdentifierExpression id => id.Name,
            _ => null
        };
    }

    public static List<string> GetOptionColumnList(IReadOnlyDictionary<string, Expression> options, string key)
    {
        var raw = TryGetOptionString(options, key);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static List<string> GetValueColumns(IReadOnlyDictionary<string, Expression> options)
    {
        var cols = GetOptionColumnList(options, "VALUE_COLS");
        if (cols.Count > 0) return cols;
        return GetOptionColumnList(options, "VALUE_COL");
    }

    private static Dictionary<string, string> BuildSourceTypeLookup(IReadOnlyList<ColumnMetadata> sourceColumns)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in sourceColumns)
        {
            lookup.TryAdd(col.Name, col.DataType);
        }
        return lookup;
    }
}
