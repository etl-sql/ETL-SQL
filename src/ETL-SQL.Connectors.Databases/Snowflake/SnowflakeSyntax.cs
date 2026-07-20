using System.Collections.Generic;

namespace ETL_SQL.Connectors.Snowflake
{
    /// <summary>
    /// Snowflake SQL dialect additions and exclusions relative to the ETL-SQL baseline vocabulary.
    /// </summary>
    public static class SnowflakeSyntax
    {
        public static readonly HashSet<string> Functions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "CURRENT_VERSION", "CURRENT_ACCOUNT", "CURRENT_WAREHOUSE", "CURRENT_DATABASE", "CURRENT_SCHEMA",
            "CURRENT_USER", "CURRENT_ROLE", "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME",
            "DATEADD", "DATEDIFF", "DATE_TRUNC", "DATE_PART", "DAYOFWEEK", "DAYOFYEAR",
            "YEAROFWEEK", "WEEKOFYEAR", "MONTHNAME", "DAYNAME",
            "ZEROIFNULL", "NULLIFZERO", "IFF", "IFNULL", "NVL", "NVL2",
            "GREATEST", "LEAST", "BOOLOR_AGG", "BOOLAND_AGG", "BOOLXOR_AGG",
            "ARRAY_AGG", "ARRAY_CONSTRUCT", "ARRAY_APPEND", "ARRAY_SIZE", "ARRAY_CONTAINS",
            "OBJECT_CONSTRUCT", "OBJECT_KEYS", "OBJECT_DELETE",
            "GET_PATH", "IS_NULL_VALUE", "STRIP_NULL_VALUE",
            "PARSE_JSON", "TRY_PARSE_JSON", "AS_REAL", "AS_INTEGER", "AS_DECIMAL", "AS_VARCHAR",
            "TO_VARIANT", "TO_ARRAY", "TO_OBJECT",
            "FLATTEN", "GET",
            "REGEXP_LIKE", "REGEXP_REPLACE", "REGEXP_SUBSTR", "REGEXP_COUNT", "REGEXP_INSTR",
            "CHARINDEX", "EDITDISTANCE", "SOUNDEX", "STRTOK", "STRTOK_TO_ARRAY",
            "UUID_STRING", "HASH",
            "APPROX_COUNT_DISTINCT", "APPROX_PERCENTILE",
            "RATIO_TO_REPORT",
            "GENERATOR",
            "SEQ1", "SEQ2", "SEQ4", "SEQ8",
            "UNIFORM", "RANDSTR", "RANDOM",
        };

        public static readonly HashSet<string> Additions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "QUALIFY", "SAMPLE", "TABLESAMPLE", "ILIKE", "RLIKE",
            "WITHIN", "GROUP", "OVER", "PARTITION",
            "TRY_CAST", "TRY_TO_DATE", "TRY_TO_TIMESTAMP", "TRY_TO_NUMBER",
            "VARIANT", "ARRAY", "OBJECT",
            "LATERAL", "FLATTEN",
            "COPY", "STAGE", "PIPE",
        };

        public static readonly HashSet<string> Exclusions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TOP",
            "NOLOCK",
            "WITH_ROLLUP",
        };
    }
}
