using System.Collections.Generic;

namespace ETL_SQL.Connectors.Oracle
{
    /// <summary>
    /// Oracle dialect additions and exclusions relative to the ETL-SQL baseline vocabulary.
    /// Only lists what is different — baseline keywords live in LanguageMetadata.
    /// </summary>
    public static class OracleSyntax
    {
        /// <summary>Oracle functions not in the ETL-SQL baseline.</summary>
        public static readonly HashSet<string> Functions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "SYSDATE", "TO_CHAR", "TO_NUMBER", "TO_DATE", "TO_TIMESTAMP",
            "NVL", "NVL2", "DECODE", "INSTR", "SUBSTR", "LPAD", "RPAD",
            "MONTHS_BETWEEN", "ADD_MONTHS", "LAST_DAY", "TRUNC", "BITAND",
            "SYS_GUID", "USERENV", "DUMP", "VSIZE"
        };

        /// <summary>Oracle keywords not in the ETL-SQL baseline (additions only).</summary>
        public static readonly HashSet<string> Additions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "ROWNUM", "ROWID", "CONNECT_BY", "PRIOR", "NOCYCLE", "LEVEL",
            "START_WITH", "SIBLINGS", "SAMPLE", "PIVOT_XML", "MODEL",
            "DIMENSION", "MEASURES", "RULES", "ITERATE", "KEEP"
        };

        /// <summary>ETL-SQL baseline keywords not supported in Oracle pushdown queries.</summary>
        public static readonly HashSet<string> Exclusions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TOP",    // Oracle uses ROWNUM / FETCH FIRST instead
            "LIMIT",  // Oracle uses FETCH FIRST instead
            "ISNULL"  // Oracle uses NVL
        };

        /// <summary>Returns Oracle dialect keywords (additions only — baseline is in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedKeywords() => Additions;

        /// <summary>Returns Oracle dialect functions (additions only — baseline functions are in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedFunctions() => Functions;
    }
}
