namespace ETL_SQL.Core.Parser
{
    public enum TokenType
    {
        // Keywords
        CREATE, CONNECTION, ON, FILE, DATABASE, EXCEL, JSON, XML,
        MSSQL, ORACLE, POSTGRES, MOCKDB, ODBC, FLATFILE,
        SELECT, FROM, WHERE, GROUP, BY, HAVING,
        INSERT, INTO, VALUES,
        UPDATE, SET,
        DELETE, TRUNCATE, PIVOT, UNPIVOT, MERGE, USING, MATCHED, SOURCE,
        COPY, MOVE, COMPRESS, SEND, RECEIVE, EMAIL, ADD, COLUMN,
        DOCKER, CLOSE, START_DOCKER, STOP_DOCKER, PAUSE_DOCKER, CLOSE_DOCKER,
        DECLARE,
        AND, OR, NOT, AS, IS,
        IN, LIKE, ESCAPE,
        CASE, WHEN, THEN, ELSE, END,
        TABLE, IF, EXISTS, IDENTITY, DEFAULT, DROP, WITH, WITHIN,
        DISTINCT, TOP, PERCENT, TIES, LIMIT, OFFSET, ROWS, ROW,
        BEGIN, WHILE, FOR, FOREACH, EACH, TO, STEP, ZONE, TIME,
        TRY, CATCH, THROW,
        PRINT, RAISEERROR, EXEC, EXECUTE,
        RETURN, BREAK, CONTINUE,
        PATH, ROOT, AUTO, RAW, EXPLICIT, ELEMENTS, INDEX, UNIQUE, EXPLAIN, SEMI, ANTI, PROCEDURE, FUNCTION, RETURNS,
        PRIMARY, KEY, FOREIGN, CHECK, REFERENCES, CONSTRAINT, CLEAR, SESSION,
        TRANSACTION, TRAN, COMMIT, ROLLBACK, ALTER, AT, RECURSIVE,
        DIRECTORY, RENAME, ENCRYPT, DECRYPT, DIRECTORY_CONTENTS,
        COPY_FILE, MOVE_FILE, RENAME_FILE, DELETE_FILE, COMPRESS_FILE, ENCRYPT_FILE, DECRYPT_FILE,
        CREATE_DIRECTORY, DELETE_DIRECTORY, RENAME_DIRECTORY, MOVE_DIRECTORY,
        COPY_DIRECTORY, DELETE_DIRECTORY_CONTENTS,
        COMPRESS_DIRECTORY, ENCRYPT_DIRECTORY, DECRYPT_DIRECTORY,
        SEND_FILE, RECEIVE_FILE, FILE_SEND, FILE_RECEIVE, SFTP, FTP_CONN, AZURE_BLOB,
        DATETIMEFROMPARTS, TIMEFROMPARTS, DATETIMEOFFSETSFROMPARTS, HASHBYTES, NEWID, NEWSEQUENTIALID, CHECKSUM, BINARY_CHECKSUM,
        STUFF, STRING_ESCAPE, STRING_SPLIT, ASCII, UNICODE, CHAR, PATINDEX, STR, QUOTENAME, TRANSLATE, DATALENGTH, TO_STR, REPLICATE, TRY_CAST,
        SIN, COS, TAN, ASIN, ACOS, ATAN, ATAN2, SIGN,
        WAITFOR, DELAY,
        HELP, TYPE, TARGET, TRUE, FALSE,
        PASSWORD, SHOW_PASSWORD, OUTPUT, INPUT, PARALLEL, RUN, SCRIPT, USE, START, STOP, PAUSE, OFF, PROFILE, PROFILING, SHOW, VERSION,
        BULK, LOAD, BATCHSIZE, MAXERRORS, FIELDTERMINATOR, ROWTERMINATOR, FIRSTROW, DATA_SOURCE,
        NULL, YEAR, MONTH, DAY, HOUR, MINUTE, SECOND, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER,
        LINEAGE, SEND_EMAIL, SUBJECT, BODY, ATTACH, CC, BCC,
        JOB, SCHEDULE, EVERY, HISTORY, JOBS, LINT, SSH_KEY_PAIR, WHAT_IF, SYSDATE,
        CONNECTIONS, TABLES, COLUMNS, TAGS, TAG, VALUE, BITS, ALGORITHM, PASSPHRASE, COMMENT,
        SUBSTRING, POSITION, OVERLAY, EXTRACT, TRIM, PLACING, LEADING, TRAILING, BOTH,
        CHARACTER_LENGTH, CHAR_LENGTH, OCTET_LENGTH,
        SETS, BANG,     // SETS keyword and ! prefix for set names
        ROLLUP, CUBE, GROUPING,   // GROUP BY extensions
        RANGE, BETWEEN, PRECEDING, FOLLOWING, UNBOUNDED, CURRENT,
        LF, CR, CRLF, TAB,
        
        // Joins
        JOIN, INNER, LEFT, RIGHT, OUTER, FULL, CROSS, APPLY, HASH, LOOP,
        
        // Set Operations
        UNION, ALL, EXCEPT, INTERSECT,
 
        // Functions
        UPPER, LOWER, CONCAT, CAST,
        CURRENT_TIMESTAMP, CURRENT_DATE, CURRENT_TIME, FORMAT,
        OVER, PARTITION, ORDER, ASC, DESC,
 
        // Data types
        INT, INTEGER, BIGINT, SMALLINT, TINYINT, BIT, BOOLEAN, BOOL,
        DECIMAL, NUMERIC, MONEY, SMALLMONEY, FLOAT, REAL, DOUBLE,
        DATE, DATETIME2, SMALLDATETIME, DATETIMEOFFSET, TIMESTAMP,
        VARCHAR, VARCHAR2, NCHAR, NVARCHAR, TEXT, NTEXT, BINARY, VARBINARY, IMAGE,
        UNIQUEIDENTIFIER, UUID, GUID, GEOMETRY, GEOGRAPHY, HIERARCHYID,
        VARIANT, SQL_VARIANT, ANY,

        IDENTIFIER,
        VARIABLE,   // @variableName
        STRING,
        NUMBER,
        DATETIME,   // Common parsed date types
 
        // Symbols / Operators
        STAR,       // *
        COMMA,      // ,
        SEMICOLON,  // ;
        DOT,        // .
        LPAREN,     // (
        RPAREN,     // )
        LBRACKET,   // [
        RBRACKET,   // ]
        EQUALS,     // =
        LESS_THAN,  // <
        GREATER_THAN, // >
        LESS_EQUALS, // <=
        GREATER_EQUALS, // >=
        NOT_EQUALS, // != or <>
 
        PLUS,       // +
        MINUS,      // -
        SLASH,      // /
        MODULO,     // %
        QUESTION,   // ?
        COLUMN_TAG, // /*@d: ... */

        // ── Report-SQL tokens (Phase 9A) ───────────────────────────────────
        // All are non-reserved: only treated as keywords inside CREATE VISUAL /
        // CREATE PAGE / CREATE DATASET context; safe to use as column/alias names elsewhere.
        VISUAL,         // CREATE VISUAL
        PAGE,           // CREATE PAGE
        DATASET,        // CREATE DATASET
        LAYOUT,         // AS LAYOUT (...)
        MAPPINGS,       // MAPPINGS ( ... )
        OPTIONS,        // OPTIONS ( ... )
        ACTIONS,        // ACTIONS ( ... )
        STRUCTURE,      // STRUCTURE = '...'
        MAP,            // MAP ( 'A' = VisualName )
        SERIES,         // series = Column
        // SOURCE already exists in the main token set (line 11)
        VISUAL_BAR,     // BAR
        VISUAL_LINE,    // LINE (separate from SQL LINE reserved word if any)
        VISUAL_SCATTER, // SCATTER
        VISUAL_PIE,     // PIE
        SLICER,         // SLICER
        VISUAL_TABLE,   // TABLE (contextual; plain TABLE already exists)
        CARD,           // CARD
        ON_CLICK,       // ON_CLICK
        DRILL_DOWN,     // DRILL_DOWN
        SET_PARAMETER,  // SET_PARAMETER
        ON_CHANGE,      // ON_CHANGE
        REFRESH,        // REFRESH EVERY '...' (EVERY already exists in main set)
        // EVERY already exists in main token set (line 42)
        // COMPRESS already exists in main token set (line 12)
        TTL,            // TTL = '...'
        KEYFILE,        // KEYFILE = '...'
        X_AXIS,         // X_AXIS ( ... )
        Y_AXIS,         // Y_AXIS ( ... )

        EOF         // End of file / string
    }
}
