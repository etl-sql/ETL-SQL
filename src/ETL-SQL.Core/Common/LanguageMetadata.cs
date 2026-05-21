using System;
using System.Collections.Generic;
using System.Linq;


namespace ETL_SQL.Common
{
    public static class LanguageMetadata
    {
        public static readonly HashSet<string> DmlKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "GROUP", "BY", "HAVING", "QUALIFY", "ORDER", "ASC", "DESC",
            "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "TRUNCATE", "PIVOT", "UNPIVOT", "MERGE", "USING", "MATCHED", "SOURCE", "TARGET",
            "DISTINCT", "TOP", "PERCENT", "TIES", "LIMIT", "OFFSET", "ROWS", "ROW", "FETCH", "FIRST", "NEXT", "ONLY", "AS",
            "ROLLUP", "CUBE", "GROUPING"
        };

        public static readonly HashSet<string> DdlKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "CREATE", "TABLE", "CONNECTION", "DROP", "DECLARE", "ADD", "COLUMN", "INDEX", "UNIQUE",
            "PRIMARY", "KEY", "FOREIGN", "CHECK", "REFERENCES", "CONSTRAINT", "PROCEDURE", "FUNCTION", "RETURNS",
            "DATABASE", "DIRECTORY", "ALTER", "VIEW", "SCHEMA", "TRANSACTION", "TRAN", "COMMIT", "ROLLBACK", "CLEAR", "SSH_KEY_PAIR", "PGP_KEY_PAIR",
            "RENAME", "ENCRYPT", "DECRYPT", "DIRECTORY_CONTENTS", "TEMPLATE", "VISUAL", "PAGE", "DATASET", "CONTAINER", "NAVIGATION", "STYLE"
        };

        public static readonly HashSet<string> ControlFlowKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "IF", "ELSE", "WHILE", "FOR", "FOREACH", "EACH", "BEGIN", "END", "TRY", "CATCH", "THROW",
            "RAISEERROR", "RAISERROR", "ASSERT", "EXEC", "EXECUTE", "RETURN", "BREAK", "CONTINUE", "GO", "CASE", "WHEN", "THEN", "SEND_EMAIL"
        };

        public static readonly HashSet<string> JoinKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "FULL", "CROSS", "APPLY",
            "UNION", "ALL", "EXCEPT", "INTERSECT",
            "FUZZY", "KEEP"
        };

        public static readonly HashSet<string> OperatorKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "AND", "OR", "NOT", "LIKE", "ILIKE", "ESCAPE", "IN", "EXISTS", "BETWEEN", "IS", "NULL"
        };

        /// <summary>
        /// Names of built-in connector types. FILE is intentionally excluded — it is the reserved
        /// default table name inside file-based connector blocks, not a connector type keyword.
        /// </summary>
        public static readonly HashSet<string> ConnectorTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "FLATFILE", "CSV", "EXCEL", "JSON", "XML", "AVRO", "PARQUET",
            "MSSQL", "ORACLE", "POSTGRES", "MOCKDB", "ODBC",
            "SFTP", "FTP", "FTP_CONN", "AZURE_BLOB", "SMTP", "DOCKER", "DIRECTORY",
            "REPORTPORTAL", "REPORT_PORTAL", "ORCHESTRATOR", "ORCH"
        };

        public static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "RELDATE", "WEEK_START_DAY", "SCRIPT_HASH_POLICY", "CASE_SENSITIVE",
            "SUBSCRIPTION", "ENABLE", "DISABLE",
            "PRINT", "STEP", "IN", "TO", "SHOW", "PROFILING", "PROFILE", "OFF", "ON", "WHAT_IF", "SYSDATE", "PERCENT", "TIES", "FILTER", "VISIBLE", "INTERACTIVE_MODE",
            "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_TIME",
            "WAIT", "WAITFOR", "DELAY", "UNTIL",
            "WITH", "RECURSIVE", "HASH", "LOOP",
            "IDENTITY", "DEFAULT", "RANGE", "GROUPS", "PRECEDING", "FOLLOWING", "UNBOUNDED", "CURRENT", "EXCLUDE", "NO", "OTHERS",
            "OVER", "PARTITION", "PATH", "ROOT", "AUTO", "RAW", "EXPLICIT", "ELEMENTS",
            "EXPLAIN", "SEMI", "ANTI", "WITHIN", "AT", "TIME", "ZONE", "COPY", "MOVE", "DELETE", "COMPRESS", "DECOMPRESS",
            "RENAME", "COPY_FILE", "MOVE_FILE", "RENAME_FILE", "DELETE_FILE", "COMPRESS_FILE", "DECOMPRESS_FILE", "ENCRYPT_FILE", "DECRYPT_FILE", "CLOSE",
            "CREATE_DIRECTORY", "DELETE_DIRECTORY", "RENAME_DIRECTORY", "MOVE_DIRECTORY", "COPY_DIRECTORY", "DELETE_DIRECTORY_CONTENTS",
            "COMPRESS_DIRECTORY", "DECOMPRESS_DIRECTORY", "ENCRYPT_DIRECTORY", "DECRYPT_DIRECTORY",
            "SEND", "RECEIVE", "EMAIL", "SEND_FILE", "RECEIVE_FILE", "FILE_SEND", "FILE_RECEIVE", "HELP", "TYPE", "TARGET", "TRUE", "FALSE",
            "SUBJECT", "BODY", "ATTACH", "CC", "BCC", "LINEAGE",
            "SINGLEQUOTE", "DOUBLEQUOTE", "SINGLEQUOTES", "DOUBLEQUOTES", "LF", "CR", "CRLF", "TILDE", "SEMICOLON", "COLON", "COMMA", "TAB", "PIPE",
            "ESCAPE_CHAR", "NULL_AS", "DATE_FORMAT", "STRICT_SCHEMA", "UTF16", "LATIN1", "UNICODE", "BACKSLASH_N", "EMPTY",
            "PASSWORD", "SHOW_PASSWORD", "OUTPUT", "INPUT", "REQUIRED", "PARALLEL", "RUN", "SCRIPT", "USE", "START", "STOP", "PAUSE", "KILL",
            "BULK", "LOAD", "BATCHSIZE", "MAXERRORS", "FIELDTERMINATOR", "ROWTERMINATOR", "FIRSTROW", "DATA_SOURCE",
            "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "INCLUDE_NULL_VALUES", "WITHOUT_ARRAY_WRAPPER",
            "JOB", "SCHEDULE", "EVERY", "HISTORY", "JOBS", "CRON", "LINT", "VERSION", "TRIGGER",

            "SETS", "SESSION", "SESSIONS", "CONNECTIONS", "VARIABLES", "LOCAL", "ANALYZE", "TABLES", "COLUMNS", "TAGS", "TAG", "VALUE", "BITS", "ALGORITHM", "PASSPHRASE", "COMMENT", "CONFIG", "PGP_KEY",
            "SUBSTRING", "POSITION", "OVERLAY", "EXTRACT", "TRIM", "PLACING", "LEADING", "TRAILING", "BOTH",
            "CHARACTER_LENGTH", "CHAR_LENGTH", "OCTET_LENGTH", "TITLE", "SUBTITLE", "REQUIRE", "SAFE", "ZONES",
            "JOIN_SPILL_THRESHOLD", "TEMP_TABLE_SPILL_THRESHOLD", "EXTERNAL_HASH_PARTITIONS", "EXTERNAL_SORT_CHUNK_SIZE", "WINDOW_SPILL_THRESHOLD",
            "MAX_RECURSIVE_DEPTH", "MAX_IN_MEMORY_BATCHES", "FOREACH_PAGE_SIZE", "MAX_MESSAGES", "MAX_FILE_OPERATIONS",
            "MAX_PARALLEL_DEGREE", "MAX_STRING_RESULT_SIZE", "REGEX_MATCH_TIMEOUT", "MAX_LAST_RESULT_ROWS", "MAX_GENERATE_ROWS", "MAX_SMTP_EMAILS_PER_SCRIPT", "MAX_INTERNAL_OPERATIONS",
            "SPILL_ENCRYPTION", "SPILL_COMPRESSION",
            "MAX_GROUPING_SETS", "SET_CUBE_LIMIT", "MAX_SESSION_SIZE", "TELEMETRY", "PERSIST",
            "GAUGE_STYLE", "SHOW_NO_DATA_PLACEHOLDER", "INTERACTIONS", "ON_SELECT", "MATCHING", "HIGHLIGHT",
            "SHOW_PROGRESS", "PROGRESS_STYLE", "SHOW_GOAL", "SHOW_PERCENT_OF_GOAL",
            "ABBREVIATE", "CLOSE_PCT", "MET_PCT",
            "COLOR_MET", "COLOR_CLOSE", "COLOR_MISSED",
            "ICON_MET", "ICON_CLOSE", "ICON_MISSED", "ICON_SET",
            "DELTA_FORMAT", "DELTA_LABEL", "TREND_DIR",
            "PREFIX", "SUFFIX",
            "LABEL_MET", "LABEL_CLOSE", "LABEL_MISSED",
            "RING", "POSITIVE_UP", "POSITIVE_DOWN",
            "TRAFFIC", "ARROWS", "CHECKS", "LAYER",
            "AXIS_SORT", "VALUE_DESC",
            "TOOLTIP", "BUTTON", "BACK", "REFRESH", "REFRESH_REPORT", "REFRESH_VISUALS", "EXPORT_CSV", "EXPORT_EXCEL", "EXPORT_PDF", "NAVIGATE_PAGE", "TEMPLATE_PATH", "MINMAX", "GENERATE", "ROWS",
            "FONT_SIZE", "CENTER", "INSIDE", "INSIDE_TOP", "INSIDE_BOTTOM", "INSIDE_LEFT", "INSIDE_RIGHT",
            "INSIDE_TOP_LEFT", "INSIDE_TOP_RIGHT", "INSIDE_BOTTOM_LEFT", "INSIDE_BOTTOM_RIGHT",
            "NONE", "HEADER", "FOOTER", "CSS", "JS", "FAVICON", "LOGO", "BACKGROUND",
            "MAX_GENERATE_ROWS", "MAX_SMTP_EMAILS_PER_SCRIPT", "MAX_INTERNAL_OPERATIONS", "ALLOW_FILE_OPERATIONS", "ALLOW_RECURSIVE_LAYERS",
            "LINEAGE_TAGS",
            "BAR", "HBAR", "LINE", "SCATTER", "PIE", "DONUT", "TABLE", "CARD", "TEXT", "SLICER", "DATEPICKER", "RELDATEPICKER", "SLIDER", "SEARCH",
            "CHECKBOX", "TEXTBOX", "NUMBERBOX", "LABEL_POSITION", "MIN", "MAX", "DECIMALS", "PLACEHOLDER", "CONTENT",
            "GAUGE", "FUNNEL", "WATERFALL", "BOXPLOT", "TREEMAP", "HEATMAP", "COMBO", "MAP", "STRUCTURE", "GAP", "MAPPINGS", "ACTIONS", "RUN_SCRIPT", "CLEAR_FILTERS",
            "ICON", "PINNABLE"
        };


        /// <summary>
        /// Standard governance tag names recognized for intellisense and documentation hints.
        /// These appear after /* @... */ in column and table annotations.
        /// </summary>
        public static readonly HashSet<string> StandardTags = new(StringComparer.OrdinalIgnoreCase)
        {
            // Security & privacy
            "pii", "phi", "pci", "sensitive", "classification", "encrypted_at_rest",
            // Ownership
            "owner", "domain", "steward", "contact", "tags", "category", "certification", "trusted",
            // Quality
            "freshness", "sla", "quality", "nullable",
            // Documentation
            "d", "example", "unit", "format",
            // Source
            "source_system", "source_table", "load_pattern"
        };

        public static readonly HashSet<string> DataTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT", "BIT", "BOOLEAN", "BOOL",
            "DECIMAL", "NUMERIC", "MONEY", "SMALLMONEY", "FLOAT", "REAL", "DOUBLE",
            "DATE", "DATETIME", "DATETIME2", "SMALLDATETIME", "DATETIMEOFFSET", "TIMESTAMP", "TIME",
            "CHAR", "VARCHAR", "VARCHAR2", "NCHAR", "NVARCHAR", "TEXT", "NTEXT", "BINARY", "VARBINARY", "IMAGE", "BLOB", "LOB", "STRING",
            "XML", "JSON", "UNIQUEIDENTIFIER", "UUID", "GUID", "GEOMETRY", "GEOGRAPHY", "HIERARCHYID",
            "VARIANT", "SQL_VARIANT", "TABLE", "CURSOR", "ANY", "MARKDOWN", "MINMAX", "VECTOR",
            "SENSITIVE", "SECRET", "NUMBER"
        };

        public static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            "CAST", "CONCAT", "UPPER", "LOWER", "SUBSTR", "SUBSTRING", "LEN", "GETDATE", "SYSDATE", "RELDATE", "ISNULL", "COALESCE",
            "FORMAT", "COUNT", "SUM", "AVG", "MIN", "MAX", "DENSE_RANK", "ROW_NUMBER", "RANK",
            "CHARINDEX", "TRIM", "LTRIM", "RTRIM", "DATEPART", "DATEDIFF", "INSTR", "REPLACE",
            "ABS", "ROUND", "CEILING", "FLOOR", "LPAD", "RPAD", "INITCAP", "POSITION", "STRPOS",
            "NVL", "NVL2", "NULLIF", "DECODE", "LEAST", "GREATEST", "POWER", "SQRT", "EXP", "LOG", "LN", "MOD", "TRUNC",
            "APPEND_TO_LIST", "REMOVE_FROM_LIST", "LENGTH", "SORT_LIST",
            "FILE_EXISTS", "DIRECTORY_EXISTS", "FILE_LIST", "DECOMPRESS_FILE",
            "DATETIMEFROMPARTS", "TIMEFROMPARTS", "DATETIMEOFFSETSFROMPARTS", "HASHBYTES", "NEWID", "NEWSEQUENTIALID", "CHECKSUM", "BINARY_CHECKSUM",
            "STUFF", "STRING_ESCAPE", "STRING_SPLIT", "ASCII", "UNICODE", "CHAR", "PATINDEX", "STR", "QUOTENAME", "TRANSLATE", "DATALENGTH", "TO_STR", "REPLICATE", "TRY_CAST",
            "SIN", "COS", "TAN", "ASIN", "ACOS", "ATAN", "ATAN2", "SIGN",
            "LAG", "LEAD", "NTILE", "FIRST_VALUE", "LAST_VALUE", "STRING_AGG",
            "CUME_DIST", "PERCENT_RANK", "NTH_VALUE", "PERCENTILE_CONT", "PERCENTILE_DISC",
            "VAR", "VARP", "VAR_SAMP", "VAR_POP", "STDEV", "STDEVP", "STDDEV", "STDDEV_SAMP", "STDDEV_POP",
            "COVAR_SAMP", "COVAR_POP", "CORR",
            "REGEXP_LIKE", "REGEXP_SUBSTR", "REGEXP_REPLACE", "REGEXP_INSTR", "REGEXP_COUNT", "REGEXP_MATCHES", "REGEXP_SPLIT_TO_TABLE",
            // JSON functions
            "JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "ISJSON", "JSON_EXISTS", "JSON_OBJECT", "JSON_ARRAY", "JSON_TABLE", "JSON_EXTRACT", "OPENJSON",
            // XML functions
            "XMLVALUE", "XMLEXISTS", "XMLQUERY", "XMLTABLE", "XMLELEMENT", "XMLATTRIBUTES", "XMLFOREST", "EXTRACTVALUE",
            // Data Generation Functions
            "SEQUENCE", "RANDOM", "RANDOM_INT", "RANDOM_DECIMAL",
            // Lineage & governance functions
            "GET_TAGS", "GET_TAG_VALUE", "HAS_TAG",
            // Fuzzy matching functions (Phases 1-3)
            "NORMALIZE", "SIMILARITY", "LEVENSHTEIN", "SOUNDEX", "METAPHONE", "DMETAPHONE", "DMETAPHONE_ALT", "NGRAMS", "NGRAM_TOKENS"
        };

        public static string EngineVersion => typeof(LanguageMetadata).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        public static string GetFullVersionString() => $"ETL-SQL {EngineVersion} (.NET 10.0)";
        
        /// <summary>Default number of batches held in memory before spilling to disk for #temp tables.</summary>
        public const int DefaultMaxInMemoryBatches = 100;

        /// <summary>Default number of rows held in memory before #temp tables spill to disk via SpillStore.</summary>
        public const long DefaultTempTableSpillThresholdRows = 1000000;
        
        /// <summary>Default number of rows before in-memory joins spill to disk.</summary>
        public const int DefaultJoinSpillThreshold = 100000;
        /// <summary>Default number of partitions used for external disk-spilling operations.</summary>
        public const int DefaultExternalHashPartitions = 16;
        /// <summary>Default number of rows per sort chunk before spilling to disk.</summary>
        public const int DefaultExternalSortChunkSize = 50000;
        /// <summary>Default number of rows before window functions spill to disk.</summary>
        public const int DefaultWindowSpillThreshold = 100000;

        /// <summary>Default number of rows before subquery results spill to disk.</summary>
        public const long DefaultSubquerySpillThresholdRows = 100000;
        
        /// <summary>Default maximum concurrency for PARALLEL blocks.</summary>
        public const int DefaultMaxParallelDegree = 32;
        /// <summary>Default maximum size in bytes for a single string function result.</summary>
        public const long DefaultMaxStringResultSize = 100 * 1024 * 1024; // 100 MiB
        
        /// <summary>Default maximum number of rows held in a SELECT result buffer for display.</summary>
        public const int DefaultMaxLastResultRows = 200000;
        
        /// <summary>Maximum number of grouping sets allowed in an aggregate CUBE/ROLLUP.</summary>
        public const int DefaultMaxGroupingSets = 1024;
        /// <summary>Default maximum size in bytes for a persisted session payload.</summary>
        public const long DefaultMaxSessionSize = 200 * 1024 * 1024; // 200 MiB

        /// <summary>Default minimum OS physical memory that must remain free (4GB).</summary>
        public const int DefaultSystemMemoryFloorMB = 4096;

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
