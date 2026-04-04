using System;
using System.Collections.Generic;

namespace ETL_SQL.Connectors.MockDb
{
    /// <summary>
    /// MockDB dialect additions and exclusions relative to the ETL-SQL baseline vocabulary.
    /// Mirrors T-SQL behavior for testing purposes.
    /// Only lists what is different — baseline keywords live in LanguageMetadata.
    /// </summary>
    public static class MockDbSyntax
    {
        /// <summary>Mock DB functions not in the ETL-SQL baseline (mirrors T-SQL).</summary>
        public static readonly HashSet<string> Functions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "GETDATE", "LEN", "ISNULL", "CONVERT", "CHARINDEX",
            "DATEADD", "DATENAME", "EOMONTH", "DATEFROMPARTS"
        };

        /// <summary>Mock DB keywords not in the ETL-SQL baseline (mirrors T-SQL additions).</summary>
        public static readonly HashSet<string> Additions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TOP", "NOLOCK", "OUTPUT", "INSERTED", "DELETED"
        };

        /// <summary>ETL-SQL baseline keywords not supported in MockDB pushdown queries.</summary>
        public static readonly HashSet<string> Exclusions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "LIMIT"   // MockDB uses TOP instead
        };

        /// <summary>Returns MockDB dialect keywords (additions only — baseline is in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedKeywords() => Additions;

        /// <summary>Returns MockDB dialect functions (additions only — baseline functions are in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedFunctions() => Functions;
    }
}
