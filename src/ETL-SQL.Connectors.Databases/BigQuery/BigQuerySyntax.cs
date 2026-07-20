using System.Collections.Generic;

namespace ETL_SQL.Connectors.BigQuery
{
    /// <summary>
    /// BigQuery Standard SQL dialect additions and exclusions relative to the ETL-SQL baseline vocabulary.
    /// </summary>
    public static class BigQuerySyntax
    {
        public static readonly HashSet<string> Functions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            // Conditional
            "IF", "IFNULL", "NULLIF", "COALESCE", "IIF",
            "GREATEST", "LEAST", "SAFE_DIVIDE",

            // Date / time
            "DATE", "DATETIME", "TIMESTAMP", "TIME",
            "DATE_ADD", "DATE_SUB", "DATE_DIFF", "DATE_TRUNC",
            "DATETIME_ADD", "DATETIME_SUB", "DATETIME_DIFF", "DATETIME_TRUNC",
            "TIMESTAMP_ADD", "TIMESTAMP_SUB", "TIMESTAMP_DIFF", "TIMESTAMP_TRUNC",
            "FORMAT_DATE", "FORMAT_DATETIME", "FORMAT_TIMESTAMP",
            "PARSE_DATE", "PARSE_DATETIME", "PARSE_TIMESTAMP",
            "EXTRACT", "CURRENT_DATE", "CURRENT_DATETIME", "CURRENT_TIME", "CURRENT_TIMESTAMP",
            "UNIX_DATE", "UNIX_SECONDS", "UNIX_MILLIS", "UNIX_MICROS",
            "TIMESTAMP_SECONDS", "TIMESTAMP_MILLIS", "TIMESTAMP_MICROS",

            // String
            "REGEXP_CONTAINS", "REGEXP_EXTRACT", "REGEXP_EXTRACT_ALL", "REGEXP_REPLACE",
            "STARTS_WITH", "ENDS_WITH", "SPLIT", "STRPOS", "FORMAT",
            "TO_BASE64", "FROM_BASE64", "TO_HEX", "FROM_HEX", "CODE_POINTS_TO_STRING",
            "NORMALIZE", "NORMALIZE_AND_CASEFOLD",
            "SOUNDEX",

            // Aggregate
            "STRING_AGG", "ARRAY_AGG", "ARRAY_CONCAT_AGG",
            "COUNTIF", "LOGICAL_AND", "LOGICAL_OR",
            "APPROX_COUNT_DISTINCT", "APPROX_QUANTILES", "APPROX_TOP_COUNT", "APPROX_TOP_SUM",
            "HLL_COUNT.INIT", "HLL_COUNT.MERGE", "HLL_COUNT.MERGE_PARTIAL", "HLL_COUNT.EXTRACT",

            // JSON
            "TO_JSON_STRING", "JSON_VALUE", "JSON_QUERY", "JSON_VALUE_ARRAY", "JSON_QUERY_ARRAY",
            "JSON_EXTRACT", "JSON_EXTRACT_SCALAR", "PARSE_JSON",

            // Array / struct
            "ARRAY", "ARRAY_LENGTH", "ARRAY_CONCAT", "ARRAY_REVERSE",
            "GENERATE_ARRAY", "GENERATE_DATE_ARRAY", "GENERATE_TIMESTAMP_ARRAY",
            "FLATTEN", "UNNEST", "STRUCT",

            // Math
            "DIV", "MOD", "SAFE_ADD", "SAFE_SUBTRACT", "SAFE_MULTIPLY", "SAFE_NEGATE",
            "FARM_FINGERPRINT", "MD5", "SHA1", "SHA256", "SHA512",

            // Conversion / type
            "SAFE_CAST", "PARSE_NUMERIC", "PARSE_BIGNUMERIC",

            // Geography / ML (common)
            "ST_GEOGPOINT", "ST_DISTANCE", "ST_CONTAINS", "ST_WITHIN",

            // Misc
            "GENERATE_UUID", "SESSION_USER", "CURRENT_USER",
        };

        public static readonly HashSet<string> Additions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "QUALIFY", "LIMIT", "EXCEPT", "TABLESAMPLE",
            "ARRAY", "STRUCT", "UNNEST",
            "SAFE_CAST",
            "LATERAL", "CROSS_JOIN",
            "WINDOW",
            "ANY_VALUE",
            "PIVOT", "UNPIVOT",
            "GROUPING_SETS", "ROLLUP", "CUBE",
        };

        public static readonly HashSet<string> Exclusions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TOP",         // BigQuery uses LIMIT
            "NOLOCK",
            "ISNULL",      // BigQuery uses IFNULL
            "GETDATE",     // BigQuery uses CURRENT_DATETIME()
            "SYSDATE",     // BigQuery uses CURRENT_TIMESTAMP()
            "WITH_ROLLUP", // BigQuery uses ROLLUP() without WITH
            "ROWNUM",      // Oracle-specific
        };
    }
}
