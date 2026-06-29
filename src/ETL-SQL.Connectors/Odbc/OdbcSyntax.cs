using System;
using System.Collections.Generic;

namespace ETL_SQL.Connectors.Odbc
{
    /// <summary>
    /// Defines generic SQL syntax defaults for ODBC drivers.
    /// Many ODBC drivers follow standard SQL-92/99 quoting.
    /// </summary>
    public static class OdbcSyntax
    {
        /// <summary>
        /// A set of common functions usually supported via ODBC escapes or native implementation.
        /// </summary>
        public static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            "COUNT", "SUM", "AVG", "MIN", "MAX",
            "ABS", "ROUND", "TRUNCATE",
            "UPPER", "LOWER", "SUBSTRING", "TRIM",
            "NOW", "CURDATE", "CURTIME"
        };

        public static HashSet<string> GetSupportedFunctions() => Functions;

        public static HashSet<string> GetSupportedKeywords() => new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "OFFSET",
            "INSERT", "INTO", "UPDATE", "SET", "DELETE", "TRUNCATE", "DROP", "CREATE", "TABLE",
            "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "ON", "UNION", "ALL"
        };

        /// <summary>
        /// Attempts to escape an identifier based on common ODBC driver patterns.
        /// Defaults to double-quoting if unknown.
        /// </summary>
        public static string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return identifier;

            // Handle bracketed identifiers: e.g. [my_table]
            if (identifier.StartsWith('[') && identifier.EndsWith(']') && identifier.Length >= 2)
            {
                var unquoted = identifier.Substring(1, identifier.Length - 2).Replace("]", "]]");
                return $"[{unquoted}]";
            }

            // Handle double-quoted identifiers: e.g. "my_table"
            if (identifier.StartsWith('"') && identifier.EndsWith('"') && identifier.Length >= 2)
            {
                var unquoted = identifier.Substring(1, identifier.Length - 2).Replace("\"", "\"\"");
                return $"\"{unquoted}\"";
            }

            return $"\"{identifier.Replace("\"", "\"\"")}\"";
        }
    }
}
