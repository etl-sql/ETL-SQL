using System.Collections.Generic;

namespace ETL_SQL.Connectors.MySql
{
    /// <summary>
    /// MySQL and MariaDB dialect additions and exclusions relative to the ETL-SQL baseline vocabulary.
    /// Only lists what is different — baseline keywords live in LanguageMetadata.
    /// </summary>
    public static class MySqlSyntax
    {
        /// <summary>MySQL/MariaDB functions not in the ETL-SQL baseline.</summary>
        public static readonly HashSet<string> Functions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "IFNULL", "GROUP_CONCAT", "STR_TO_DATE", "DATE_FORMAT", "NOW", "UNIX_TIMESTAMP",
            "JSON_OBJECT", "JSON_ARRAY", "JSON_EXTRACT", "JSON_SET", "JSON_INSERT", "JSON_REPLACE", "JSON_REMOVE",
            "UTC_TIMESTAMP", "UUID", "MD5", "SHA1", "SHA2", "REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR",
            "SUBSTRING_INDEX", "CONCAT_WS", "DATEDIFF", "DATE_ADD", "DATE_SUB"
        };

        /// <summary>MySQL/MariaDB keywords not in the ETL-SQL baseline (additions only).</summary>
        public static readonly HashSet<string> Additions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "LIMIT", "OFFSET", "ON_DUPLICATE_KEY_UPDATE", "BINARY", "COLLATE",
            "REGEXP", "RLIKE", "SEPARATOR", "FORCE_INDEX", "USE_INDEX", "IGNORE_INDEX"
        };

        /// <summary>ETL-SQL baseline keywords not supported in MySQL/MariaDB pushdown queries.</summary>
        public static readonly HashSet<string> Exclusions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TOP",         // MySQL uses LIMIT instead
            "ROWNUM",      // Oracle-specific
            "PERCENT",     // T-SQL TOP PERCENT
            "GETDATE",     // T-SQL specific (MySQL uses NOW() or UTC_TIMESTAMP())
            "SYSDATE",     // Oracle specific
            "DATALENGTH",  // T-SQL specific (MySQL uses LENGTH() or OCTET_LENGTH())
            "ISNULL"       // T-SQL 2-argument ISNULL is not supported (MySQL's ISNULL(expr) returns 1 or 0; use IFNULL or COALESCE instead)
        };

        /// <summary>Returns MySQL/MariaDB dialect keywords (additions only — baseline is in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedKeywords() => Additions;

        /// <summary>Returns MySQL/MariaDB dialect functions (additions only — baseline functions are in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedFunctions() => Functions;
    }
}
