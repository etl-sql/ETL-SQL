using System;
using System.Collections.Generic;
using System.Linq;


namespace ETL_SQL.Common
{
    public static class LanguageMetadata
    {
        public static readonly HashSet<string> DmlKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "GROUP", "BY", "HAVING", "ORDER", "ASC", "DESC",
            "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "TRUNCATE", "PIVOT", "UNPIVOT", "MERGE", "USING", "MATCHED", "SOURCE", "TARGET",
            "DISTINCT", "TOP", "LIMIT", "OFFSET", "ROWS", "ROW", "FETCH", "NEXT", "ONLY", "AS"
        };

        public static readonly HashSet<string> DdlKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "CREATE", "TABLE", "CONNECTION", "DROP", "DECLARE", "ADD", "COLUMN", "INDEX", "UNIQUE",
            "PRIMARY", "KEY", "FOREIGN", "CHECK", "REFERENCES", "CONSTRAINT", "PROCEDURE", "FUNCTION", "RETURNS",
            "DATABASE", "DIRECTORY", "ALTER", "VIEW", "SCHEMA", "TRANSACTION", "TRAN", "COMMIT", "ROLLBACK", "CLEAR", "SSH_KEY_PAIR",
            "RENAME", "ENCRYPT", "DECRYPT", "DIRECTORY_CONTENTS"
        };

        public static readonly HashSet<string> ControlFlowKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "IF", "ELSE", "WHILE", "FOR", "FOREACH", "EACH", "BEGIN", "END", "TRY", "CATCH", "THROW",
            "RAISEERROR", "EXEC", "EXECUTE", "RETURN", "BREAK", "CONTINUE", "CASE", "WHEN", "THEN", "SEND_EMAIL"
        };

        public static readonly HashSet<string> JoinKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "FULL", "CROSS", "APPLY",
            "UNION", "ALL", "EXCEPT", "INTERSECT"
        };

        public static readonly HashSet<string> OperatorKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "OR", "NOT", "LIKE", "ESCAPE", "IN", "EXISTS", "BETWEEN", "IS", "NULL"
        };

        /// <summary>
        /// Names of built-in connector types. FILE is intentionally excluded — it is the reserved
        /// default table name inside file-based connector blocks, not a connector type keyword.
        /// </summary>
        public static readonly HashSet<string> ConnectorTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "FLATFILE", "CSV", "EXCEL", "JSON", "XML", "AVRO", "PARQUET",
            "MSSQL", "ORACLE", "POSTGRES", "MOCKDB", "ODBC",
            "SFTP", "FTP", "FTP_CONN", "AZURE_BLOB", "SMTP", "DOCKER", "DIRECTORY"
        };

        public static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "PRINT", "STEP", "IN", "TO", "SHOW", "PROFILING", "PROFILE", "OFF", "ON", "WHAT_IF", "SYSDATE",
            "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME",
            "WAITFOR", "DELAY",
            "WITH", "RECURSIVE", "HASH", "LOOP",
            "IDENTITY", "DEFAULT", "RANGE", "PRECEDING", "FOLLOWING", "UNBOUNDED", "CURRENT",
            "OVER", "PARTITION", "PATH", "ROOT", "AUTO", "RAW", "EXPLICIT", "ELEMENTS",
            "EXPLAIN", "SEMI", "ANTI", "WITHIN", "AT", "TIME", "ZONE", "COPY", "MOVE", "DELETE", "COMPRESS",
            "RENAME", "COPY_FILE", "MOVE_FILE", "RENAME_FILE", "DELETE_FILE", "COMPRESS_FILE", "ENCRYPT_FILE", "DECRYPT_FILE", "CLOSE",
            "CREATE_DIRECTORY", "DELETE_DIRECTORY", "RENAME_DIRECTORY", "MOVE_DIRECTORY", "COPY_DIRECTORY", "DELETE_DIRECTORY_CONTENTS",
            "COMPRESS_DIRECTORY", "ENCRYPT_DIRECTORY", "DECRYPT_DIRECTORY",
            "SEND", "RECEIVE", "EMAIL", "SEND_FILE", "RECEIVE_FILE", "FILE_SEND", "FILE_RECEIVE", "HELP", "TYPE", "TARGET", "TRUE", "FALSE",
            "SINGLEQUOTE", "DOUBLEQUOTE", "SINGLEQUOTES", "DOUBLEQUOTES", "LF", "CR", "CRLF", "TILDE", "SEMICOLON", "COLON", "COMMA", "TAB", "PIPE",
            "ESCAPE_CHAR", "NULL_AS", "DATE_FORMAT", "STRICT_SCHEMA", "UTF16", "LATIN1", "UNICODE", "BACKSLASH_N", "EMPTY",
            "PASSWORD", "SHOW_PASSWORD", "OUTPUT", "INPUT", "PARALLEL", "RUN", "SCRIPT", "USE", "START", "STOP", "PAUSE",
            "START_DOCKER", "STOP_DOCKER", "PAUSE_DOCKER", "CLOSE_DOCKER",
            "BULK", "LOAD", "BATCHSIZE", "MAXERRORS", "FIELDTERMINATOR", "ROWTERMINATOR", "FIRSTROW", "DATA_SOURCE",
            "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "INCLUDE_NULL_VALUES", "WITHOUT_ARRAY_WRAPPER",
            "JOB", "SCHEDULE", "EVERY", "HISTORY", "JOBS", "CRON", "LINT",
            "SETS", "SESSION"
        };

        public static readonly HashSet<string> DataTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT", "BIT", "BOOLEAN", "BOOL",
            "DECIMAL", "NUMERIC", "MONEY", "SMALLMONEY", "FLOAT", "REAL", "DOUBLE",
            "DATE", "DATETIME", "DATETIME2", "SMALLDATETIME", "DATETIMEOFFSET", "TIMESTAMP", "TIME",
            "CHAR", "VARCHAR", "NCHAR", "NVARCHAR", "TEXT", "NTEXT", "BINARY", "VARBINARY", "IMAGE",
            "XML", "JSON", "UNIQUEIDENTIFIER", "UUID", "GUID", "GEOMETRY", "GEOGRAPHY", "HIERARCHYID",
            "VARIANT", "SQL_VARIANT", "TABLE", "CURSOR", "ANY"
        };

        public static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            "CAST", "CONCAT", "UPPER", "LOWER", "SUBSTR", "SUBSTRING", "LEN", "GETDATE", "SYSDATE", "ISNULL", "COALESCE",
            "FORMAT", "COUNT", "SUM", "AVG", "MIN", "MAX", "DENSE_RANK", "ROW_NUMBER", "RANK",
            "CHARINDEX", "TRIM", "LTRIM", "RTRIM", "DATEPART", "DATEDIFF", "INSTR", "REPLACE",
            "ABS", "ROUND", "CEILING", "FLOOR", "LPAD", "RPAD", "INITCAP", "POSITION", "STRPOS",
            "NVL", "NVL2", "NULLIF", "DECODE", "LEAST", "GREATEST", "POWER", "SQRT", "EXP", "LOG", "LN", "MOD", "TRUNC",
            "APPEND_TO_LIST", "REMOVE_FROM_LIST", "LENGTH", "SORT_LIST",
            "FILE_EXISTS", "DIRECTORY_EXISTS", "FILE_LIST",
            "DATETIMEFROMPARTS", "TIMEFROMPARTS", "DATETIMEOFFSETSFROMPARTS", "HASHBYTES", "NEWID", "NEWSEQUENTIALID", "CHECKSUM", "BINARY_CHECKSUM",
            "STUFF", "STRING_ESCAPE", "STRING_SPLIT", "ASCII", "UNICODE", "CHAR", "PATINDEX", "STR", "QUOTENAME", "TRANSLATE", "DATALENGTH", "TO_STR", "REPLICATE", "TRY_CAST",
            "SIN", "COS", "TAN", "ASIN", "ACOS", "ATAN", "ATAN2", "SIGN",
            "LAG", "LEAD", "NTILE", "FIRST_VALUE", "LAST_VALUE", "STRING_AGG",
            "CUME_DIST", "PERCENT_RANK", "NTH_VALUE", "PERCENTILE_CONT", "PERCENTILE_DISC",
            "REGEXP_LIKE", "REGEXP_SUBSTR", "REGEXP_REPLACE", "REGEXP_INSTR", "REGEXP_COUNT", "REGEXP_MATCHES", "REGEXP_SPLIT_TO_TABLE",
            // JSON functions
            "JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "ISJSON", "JSON_EXISTS", "JSON_OBJECT", "JSON_ARRAY", "JSON_TABLE", "JSON_EXTRACT", "OPENJSON",
            // XML functions
            "XMLVALUE", "XMLEXISTS", "XMLQUERY", "XMLTABLE", "XMLELEMENT", "XMLATTRIBUTES", "XMLFOREST", "EXTRACTVALUE"
        };

        public static bool IsKeyword(string word) => DmlKeywords.Contains(word) || DdlKeywords.Contains(word) || ControlFlowKeywords.Contains(word) || JoinKeywords.Contains(word) || OperatorKeywords.Contains(word) || Keywords.Contains(word) || ConnectorTypes.Contains(word) || Functions.Contains(word);
        public static bool IsFunction(string word) => Functions.Contains(word);
        public static bool IsDataType(string word) => DataTypes.Contains(word);
        public static bool IsConnectorType(string word) => ConnectorTypes.Contains(word);

        public static IEnumerable<string> GetAllKeywords()
        {
            return DmlKeywords.Concat(DdlKeywords).Concat(ControlFlowKeywords).Concat(JoinKeywords).Concat(OperatorKeywords).Concat(Keywords).Concat(ConnectorTypes).Concat(DataTypes);
        }
    }
}
