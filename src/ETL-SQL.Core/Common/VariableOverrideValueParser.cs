using System;

namespace ETL_SQL.Core.Common;

/// <summary>Canonical value coercion for CLI and remotely triggered <c>--var</c> overrides.</summary>
public static class VariableOverrideValueParser
{
    public static object? Parse(string raw)
    {
        if (int.TryParse(raw, out var integer)) return integer;
        if (double.TryParse(raw, out var number)) return number;
        if (bool.TryParse(raw, out var boolean)) return boolean;
        if (DateTime.TryParse(raw, out var dateTime)) return dateTime;
        return raw.Trim('\'', '"');
    }
}
