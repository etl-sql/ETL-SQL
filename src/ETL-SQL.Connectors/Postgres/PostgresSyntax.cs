using System.Collections.Generic;

namespace ETL_SQL.Connectors.Postgres
{
    /// <summary>
    /// PostgreSQL dialect additions and exclusions relative to the ETL-SQL baseline vocabulary.
    /// Only lists what is different — baseline keywords live in LanguageMetadata.
    /// </summary>
    public static class PostgresSyntax
    {
        /// <summary>PostgreSQL functions not in the ETL-SQL baseline.</summary>
        public static readonly HashSet<string> Functions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "NOW", "STRPOS", "STRING_AGG", "ARRAY_AGG", "ARRAY_TO_STRING",
            "GENERATE_SERIES", "REGEXP_MATCHES", "REGEXP_REPLACE", "REGEXP_SPLIT_TO_TABLE",
            "DATE_TRUNC", "DATE_PART", "AGE", "EXTRACT",
            "NULLIF", "GREATEST", "LEAST", "BOOL_AND", "BOOL_OR",
            "JSON_BUILD_OBJECT", "JSON_BUILD_ARRAY", "JSONB_SET", "JSON_ARRAY_ELEMENTS"
        };

        /// <summary>PostgreSQL keywords not in the ETL-SQL baseline (additions only).</summary>
        public static readonly HashSet<string> Additions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "LIMIT", "RETURNING", "ON_CONFLICT", "DO_NOTHING", "DO_UPDATE",
            "ILIKE", "SIMILAR", "TABLESAMPLE", "LATERAL", "WINDOW",
            "FILTER", "WITHIN_GROUP"
        };

        /// <summary>ETL-SQL baseline keywords not supported in PostgreSQL pushdown queries.</summary>
        public static readonly HashSet<string> Exclusions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TOP",     // Postgres uses LIMIT instead
            "ISNULL",  // Postgres uses COALESCE or IS NULL
            "CONVERT"  // Postgres uses CAST
        };

        /// <summary>Returns PostgreSQL dialect keywords (additions only — baseline is in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedKeywords() => Additions;

        /// <summary>Returns PostgreSQL dialect functions (additions only — baseline functions are in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedFunctions() => Functions;
    }
}
