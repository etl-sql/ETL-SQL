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

        if (algorithm is "UNPIVOT" or "MELT")
        {
            var idCols = GetOptionColumnList(statement.Options, "ID_COLS");
            var valueCols = GetOptionColumnList(statement.Options, "VALUE_COLS");
            var nameCol = TryGetOptionString(statement.Options, "NAME_COL") ?? "Name";
            var valueCol = TryGetOptionString(statement.Options, "VALUE_COL") ?? "Value";
            var projected = idCols
                .Select(name => new ColumnMetadata(name, sourceByName.TryGetValue(name, out var t) ? t : "ANY"))
                .ToList();
            projected.Add(new ColumnMetadata(nameCol, "VARCHAR"));
            projected.Add(new ColumnMetadata(valueCol, InferUnpivotValueType(valueCols, sourceByName)));
            return projected;
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
            case "UNPIVOT":
            case "MELT":
                {
                    var valueCols = GetOptionColumnList(options, "VALUE_COLS");
                    var nameCol = TryGetOptionString(options, "NAME_COL") ?? "Name";
                    var valueCol = TryGetOptionString(options, "VALUE_COL") ?? "Value";
                    specs.Add(new ComputedColumnSpec(nameCol, "VARCHAR", valueCols, ReplacesExisting: false));
                    specs.Add(new ComputedColumnSpec(valueCol, InferUnpivotValueType(valueCols, sourceTypes), valueCols, ReplacesExisting: false));
                    break;
                }
            case "BIN":
            case "BUCKETIZE":
                {
                    var valCol = TryGetOptionString(options, "VALUE_COL");
                    if (!string.IsNullOrEmpty(valCol))
                    {
                        var startCol = TryGetOptionString(options, "BIN_START_COL") ?? "Bin_Start";
                        var endCol = TryGetOptionString(options, "BIN_END_COL") ?? "Bin_End";
                        var labelCol = TryGetOptionString(options, "BIN_LABEL_COL") ?? "Bin_Label";
                        specs.Add(new ComputedColumnSpec(startCol, "DECIMAL", [valCol], ReplacesExisting: false));
                        specs.Add(new ComputedColumnSpec(endCol, "DECIMAL", [valCol], ReplacesExisting: false));
                        specs.Add(new ComputedColumnSpec(labelCol, "VARCHAR", [valCol], ReplacesExisting: false));
                    }
                    break;
                }
            case "OUTLIERS":
                {
                    var valCol = TryGetOptionString(options, "VALUE_COL");
                    var action = (TryGetOptionString(options, "ACTION") ?? "CLIP").ToUpperInvariant();
                    if (!string.IsNullOrEmpty(valCol))
                    {
                        if (action == "FLAG")
                        {
                            var flagCol = TryGetOptionString(options, "FLAG_COL") ?? $"{valCol}_IsOutlier";
                            specs.Add(new ComputedColumnSpec(flagCol, "INT", [valCol], ReplacesExisting: false));
                        }
                        else if (action is "CLIP" or "NULL")
                        {
                            var srcType = sourceTypes != null && sourceTypes.TryGetValue(valCol, out var t) ? t : "DECIMAL";
                            var outType = action == "CLIP" && IsIntegralType(srcType) ? "DECIMAL" : srcType;
                            specs.Add(new ComputedColumnSpec(valCol, outType, [valCol], ReplacesExisting: true));
                        }
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
                     "KEY_COLS", "ID_COLS", "ROW_FIELDS", "PIVOT_FIELD", "VALUE_FIELD", "CATEGORY_COL"
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

    public static string InferUnpivotValueType(
        IReadOnlyList<string> valueCols,
        IReadOnlyDictionary<string, string>? sourceTypes)
    {
        if (sourceTypes == null || valueCols.Count == 0)
            return "ANY";

        var resolvedTypes = new List<string>();
        foreach (var col in valueCols)
        {
            if (sourceTypes.TryGetValue(col, out var t)
                && !string.IsNullOrWhiteSpace(t)
                && !t.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
                && !t.Equals("ANY", StringComparison.OrdinalIgnoreCase))
            {
                resolvedTypes.Add(t);
            }
        }

        if (resolvedTypes.Count == 0)
            return "ANY";

        if (resolvedTypes.All(t => t.Equals(resolvedTypes[0], StringComparison.OrdinalIgnoreCase)))
            return resolvedTypes[0];

        var baseTypes = resolvedTypes.Select(t => t.Split('(')[0].Trim().ToUpperInvariant()).ToList();
        if (baseTypes.All(b => b == baseTypes[0]))
            return baseTypes[0];

        static bool IsNumeric(string b) =>
            b is "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT" or "DECIMAL" or "NUMERIC" or "MONEY" or "FLOAT" or "DOUBLE" or "REAL";

        if (baseTypes.All(IsNumeric))
        {
            if (baseTypes.Any(b => b is "FLOAT" or "DOUBLE" or "REAL"))
                return "DOUBLE";
            return "DECIMAL";
        }

        return "VARCHAR";
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
