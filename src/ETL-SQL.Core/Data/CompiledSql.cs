using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ETL_SQL.Core.Data
{
    /// <summary>
    /// Represents a compiled SQL statement with its associated parameters.
    /// </summary>
    public record CompiledSql(string Sql, Dictionary<string, object?> Parameters)
    {
        public static CompiledSql Empty => new CompiledSql(string.Empty, new Dictionary<string, object?>());

        /// <summary>
        /// Converts the parameterized SQL back to a raw string by escaping parameter values.
        /// Use only for data sources that do not support native parameters (e.g., FlatFiles).
        /// </summary>
        public string ToEscapedSql(string dialect = "MSSQL")
        {
            var result = Sql;
            // Sorting by length descending ensures that @p10 is replaced before @p1
            foreach (var kv in Parameters.OrderByDescending(p => p.Key.Length))
            {
                var escapedVal = EscapeValue(kv.Value, dialect);
                result = result.Replace(kv.Key, escapedVal);
            }
            return result;
        }

        private static string EscapeValue(object? val, string dialect)
        {
            if (val == null || val == DBNull.Value) return "NULL";
            if (val is string s)
            {
                var escaped = s.Replace("'", "''");
                return dialect.Equals("POSTGRES", StringComparison.OrdinalIgnoreCase) ? $"E'{escaped}'" : $"'{escaped}'";
            }
            if (val is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
            if (val is bool b)
            {
                if (dialect.Equals("POSTGRES", StringComparison.OrdinalIgnoreCase) || dialect.Equals("ORACLE", StringComparison.OrdinalIgnoreCase))
                    return b ? "TRUE" : "FALSE";
                return b ? "1" : "0";
            }
            if (val is decimal dec) return dec.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return val.ToString() ?? "NULL";
        }
    }
}
