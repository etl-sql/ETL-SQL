using System.Collections.Generic;

namespace ETL_SQL.Connectors.Sqlite
{
    public static class SqliteSyntax
    {
        public static readonly HashSet<string> Functions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "IFNULL", "INSTR", "HEX", "RANDOM", "GLOB", "SOUNDEX", "TYPEOF", "LAST_INSERT_ROWID"
        };

        public static readonly HashSet<string> Additions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "LIMIT", "OFFSET", "AUTOINCREMENT"
        };

        public static readonly HashSet<string> Exclusions = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "TOP",
            "CONVERT",
            "ROWNUM",
            "PERCENT",
            "GETDATE",
            "SYSDATE"
        };

        public static HashSet<string> GetSupportedKeywords() => Additions;
        public static HashSet<string> GetSupportedFunctions() => Functions;
    }
}
