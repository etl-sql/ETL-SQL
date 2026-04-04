using System.Collections.Generic;

namespace ETL_SQL.Connectors.SqlServer
{
    /// <summary>
    /// SQL Server dialect additions and exclusions relative to the ETL-SQL baseline vocabulary.
    /// Only lists what is different — baseline keywords live in LanguageMetadata.
    /// </summary>
    public static class SqlServerSyntax
    {
        /// <summary>T-SQL functions not in the ETL-SQL baseline.</summary>
        public static readonly HashSet<string> Functions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "GETDATE", "CHARINDEX", "ISNULL", "CONVERT", "LEN",
            "DATEADD", "DATENAME", "EOMONTH", "DATEFROMPARTS", "SYSDATETIME",
            "NEWSEQUENTIALID", "CHECKSUM", "BINARY_CHECKSUM", "HASHBYTES"
        };

        /// <summary>T-SQL keywords not in the ETL-SQL baseline (additions only).</summary>
        public static readonly HashSet<string> Additions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TOP", "NOLOCK", "READUNCOMMITTED", "ROWLOCK", "TABLOCK", "UPDLOCK",
            "INSERTED", "DELETED", "OUTPUT", "OPTION", "RECOMPILE", "MAXDOP",
            "CROSS", "APPLY", "TABLESAMPLE"
        };

        /// <summary>ETL-SQL baseline keywords not supported in T-SQL pushdown queries.</summary>
        public static readonly HashSet<string> Exclusions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "LIMIT"   // SQL Server uses TOP instead
        };

        /// <summary>Returns all T-SQL keywords (additions only — baseline is in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedKeywords() => Additions;

        /// <summary>Returns all T-SQL functions (additions only — baseline functions are in LanguageMetadata).</summary>
        public static HashSet<string> GetSupportedFunctions() => Functions;
    }
}
