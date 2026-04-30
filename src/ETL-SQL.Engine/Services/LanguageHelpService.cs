using System;
using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.Engine.Services
{
    public static class LanguageHelpService
    {
        public static void Initialize(ILanguageHelpRegistry registry)
        {
            // ── REPORT TOPICS ────────────────────────────────────────────────
            registry.RegisterHelp("REPORT", "Help for Report-SQL (dashboards). Use HELP REPORT <TOPIC> for details.", "INDEX");
            
            registry.RegisterHelp("REPORT", 
                "Syntax: CREATE VISUAL <name> AS <TYPE> ( ... body ... )\n" +
                "Visual Types: BAR, LINE, PIE, DONUT, SCATTER, BUBBLE, RADAR, BOXPLOT, TREEMAP, HEATMAP, GAUGE, FUNNEL, CANDLESTICK, MAP, TABLE, CARD, TEXT, IMAGE, SLICER, DATEPICKER, SLIDER, SEARCH, MULTISELECT.\n" +
                "Body Properties:\n" +
                "  - SOURCE:   The data source (e.g., #temp, @var, or &dataset).\n" +
                "  - TITLE:    Visual title (supports Markdown: 'MD:Title').\n" +
                "  - MAPPINGS: Maps columns to visual slots (X, Y, COLOR, SIZE).\n" +
                "  - OPTIONS:  Visual-specific settings (MIN, MAX, LEGEND, TOOLBOX, STACKED, SMOOTH).\n" +
                "  - STYLE:    Inline styles or reference to a CREATE STYLE object.\n" +
                "  - ACTIONS:  Define interactivity (ON_CLICK, ON_CHANGE).\n" +
                "  - OVERLAYS: Add trend lines (LINEAR, EXP, LOG) or constant GOAL lines.\n" +
                "  - SERIES:   Define column-specific visual types (e.g., BAR col1, LINE col2).\n" +
                "  - FORMATTING: Conditional formatting (Condition THEN 'color').\n" +
                "  - SUMMARY:  Add summary statistics to tables (GRAND_TOTAL, SUMMARIZE_COLUMN).", "VISUAL");

            registry.RegisterHelp("REPORT", 
                "Syntax: CREATE DATASET #name [REFRESH EVERY 'time'] [TTL 'time'] [ENCRYPT = MACHINE|PASSWORD|KEYFILE] AS (SELECT ...)\n" +
                "Purpose: Defines a persistent or cached result set that can be shared across reports.", "DATASET");

            registry.RegisterHelp("REPORT",
                "Syntax: CREATE PAGE <name> AS LAYOUT ( ... body ... )\n" +
                "Body Properties:\n" +
                "  - STRUCTURE: CSS grid-template-areas (e.g., 'A A / B C').\n" +
                "  - MAP:       Maps slots to visuals (MAP ('A' = MyChart)).\n" +
                "  - STYLE:     Page-level styling.", "PAGE");

            registry.RegisterHelp("REPORT",
                "Syntax: CREATE CONTAINER <name> AS BOX|SCROLL ( ... body ... )\n" +
                "Purpose: Groups visuals within a page layout using its own STRUCTURE and MAP.", "CONTAINER");

            registry.RegisterHelp("REPORT",
                "Syntax: CREATE STYLE <name> ( ... body ... )\n" +
                "Purpose: Defines reusable CSS-like properties (THEME, BACKGROUND, COLORS, etc.).", "STYLE");

            registry.RegisterHelp("REPORT",
                "Syntax: CREATE NAVIGATION <name> AS TAB|BUTTON|LINK ( ORIENTATION = ..., PAGES = (...) )\n" +
                "Purpose: Provides navigation between different report pages.", "NAVIGATION");

            registry.RegisterHelp("REPORT",
                "Syntax: CREATE BUTTON <name> AS BACK|REFRESH|LINK ( TITLE = '...', ACTIONS = (...) )\n" +
                "Purpose: Interactive button to perform UI or data actions.", "BUTTON");

            // ── STATEMENT TOPICS ─────────────────────────────────────────────
            registry.RegisterHelp("DECLARE", "Syntax: DECLARE @VariableName <DATATYPE> [ = <VALUE> ];\nDeclares a local variable within the engine scope.");
            registry.RegisterHelp("SET", "Syntax: SET @VariableName = <VALUE>;\nor SET <OPTION> = <VALUE> (e.g., SET WHAT_IF ON;)\nAssigns a value to a variable or updates engine settings.");
            registry.RegisterHelp("IF", "Syntax: IF <CONDITION> BEGIN ... END [ ELSE BEGIN ... END ];\nProvides conditional branching logic.");
            registry.RegisterHelp("WHILE", "Syntax: WHILE <CONDITION> BEGIN ... END;\nRepeats a block of code as long as the condition is true.");
            registry.RegisterHelp("FOR", "Syntax: FOR <VAR> = <START> TO <END> [ STEP <N> ] BEGIN ... END;\nStandard numeric loop.");
            registry.RegisterHelp("FOREACH", "Syntax: FOREACH <VAR> IN <SELECT_QUERY> BEGIN ... END;\nIterates over each row returned by a query.");
            registry.RegisterHelp("TRY", "Syntax: BEGIN TRY ... END TRY BEGIN CATCH ... END CATCH;\nError handling block.");
            registry.RegisterHelp("TRANSACTION", "Syntax: BEGIN TRANSACTION; COMMIT; ROLLBACK;\nControls atomic data operations. Use @TranCount to check nesting.");
            registry.RegisterHelp("PARALLEL", "Syntax: PARALLEL BEGIN ... END;\nRuns enclosed statements concurrently using a thread pool.");
            registry.RegisterHelp("BULK INSERT", "Syntax: BULK INSERT <TARGET> FROM <FILE_PATH> WITH ( BATCH_SIZE=n, MAX_ERRORS=n, ERROR_LOG_PATH='path', FIRST_ROW=n, LAST_ROW=n );\nHigh-performance file loading into remote or local tables.");
            
            // ── CORE STATEMENTS ──────────────────────────────────────────────
            registry.RegisterHelp("SELECT", "Syntax: SELECT [TOP n] <cols> [INTO <table>] FROM <src> [JOIN...] [WHERE...] [GROUP BY...] [ORDER BY...]\nQueries data from connections or #temp tables.");
            registry.RegisterHelp("INSERT", "Syntax: INSERT INTO <target> [(cols)] SELECT... | VALUES(...)\nAdds new rows to a table.");
            registry.RegisterHelp("UPDATE", "Syntax: UPDATE <target> SET <col>=<val> [WHERE...]\nModifies existing rows.");
            registry.RegisterHelp("DELETE", "Syntax: DELETE FROM <target> [WHERE...]\nRemoves rows from a table.");
            registry.RegisterHelp("MERGE", "Syntax: MERGE INTO <target> USING <source> ON <condition> WHEN MATCHED THEN UPDATE... WHEN NOT MATCHED THEN INSERT...\nSynchronizes two data sources.");
            registry.RegisterHelp("WAITFOR", "Syntax: WAITFOR DELAY 'hh:mm:ss' | TIME 'hh:mm:ss' | (condition)\nSuspends execution.");
            registry.RegisterHelp("PRINT", "Syntax: PRINT <expression> [, timestamp=TRUE|FALSE]\nOutputs a message to the console or log.");
            registry.RegisterHelp("CREATE CONNECTION", "Syntax: CREATE CONNECTION <name> ON <type>(<conn_string>) [WITH(...)];\nRegisters a data source.");

            // ── FILE & DIRECTORY ─────────────────────────────────────────────
            registry.RegisterHelp("DIRECTORY", 
                "VERBOSE: true\nSHORTHAND: DIR\n" +
                "Directory Operations:\n" +
                "  CREATE DIRECTORY 'path'\n" +
                "  DELETE DIRECTORY 'path'\n" +
                "  RENAME DIRECTORY 'old' TO 'new'\n" +
                "  MOVE DIRECTORY 'src' TO 'dest'\n" +
                "  COPY DIRECTORY 'src' TO 'dest'\n" +
                "  COMPRESS DIRECTORY 'src' TO 'dest.zip'\n" +
                "  ENCRYPT DIRECTORY 'src' TO 'dest' PASSWORD('pwd')\n" +
                "  DECRYPT DIRECTORY 'src' TO 'dest' PASSWORD('pwd')");

            registry.RegisterHelp("FILE",
                "VERBOSE: true\nSHORTHAND: none\n" +
                "File Operations:\n" +
                "  COPY FILE 'src' TO 'dest'\n" +
                "  MOVE FILE 'src' TO 'dest'\n" +
                "  RENAME FILE 'old' TO 'new'\n" +
                "  DELETE FILE 'path'\n" +
                "  COMPRESS FILE 'src' TO 'dest.zip'\n" +
                "  ENCRYPT FILE 'src' TO 'dest' PASSWORD('pwd')\n" +
                "  DECRYPT FILE 'src' TO 'dest' PASSWORD('pwd')");

            registry.RegisterHelp("TRANSFER",
                "VERBOSE: true\nSHORTHAND: XFER\n" +
                "File Transfer Operations:\n" +
                "  SEND FILE 'local' TO 'remote' AT conn\n" +
                "  RECEIVE FILE 'remote' TO 'local' AT conn");

            registry.RegisterHelp("SEND", "VERBOSE: true\nSHORTHAND: none\nSee HELP TRANSFER or HELP EMAIL.");
            registry.RegisterHelp("RECEIVE", "VERBOSE: true\nSHORTHAND: none\nSee HELP TRANSFER.");
            
            registry.RegisterHelp("SEND", 
                "VERBOSE: true\nSHORTHAND: none\n" +
                "Syntax: SEND FILE 'local' TO 'remote' AT conn [WITH (OVERWRITE=ON|OFF)]\n" +
                "Transmits a file to a remote destination.", "FILE");

            registry.RegisterHelp("RECEIVE",
                "VERBOSE: true\nSHORTHAND: none\n" +
                "Syntax: RECEIVE FILE 'remote' TO 'local' AT conn [WITH (OVERWRITE=ON|OFF)]\n" +
                "Retrieves a file from a remote source.", "FILE");

            registry.RegisterHelp("SEND",
                "VERBOSE: true\nSHORTHAND: none\n" +
                "Syntax: SEND EMAIL TO 'to' FROM 'from' SUBJECT 'subj' BODY 'body' AT conn\n" +
                "Sends an email with optional attachments.", "EMAIL");

            registry.RegisterHelp("EMAIL",
                "VERBOSE: true\nSHORTHAND: none\n" +
                "Email Operations:\n" +
                "  SEND EMAIL TO 'to' FROM 'from' SUBJECT 'subj' BODY 'body' AT conn [ATTACH 'file']");

            registry.RegisterHelp("SSH_KEY_PAIR",
                "SSH Key Pair Operations:\n" +
                "  CREATE SSH_KEY_PAIR 'path' WITH(BITS=2048, ALGORITHM='RSA', PASSPHRASE='pwd')");

            registry.RegisterHelp("DOCKER",
                "Docker Operations:\n" +
                "  START_DOCKER <image> [AS <alias>]\n" +
                "  STOP_DOCKER <alias>\n" +
                "  PAUSE_DOCKER <alias>\n" +
                "  RESUME_DOCKER <alias>\n" +
                "  CLOSE_DOCKER <alias|image>");

            // ── INTROSPECTION & CONFIG ────────────────────────────────────────
            registry.RegisterHelp("SHOW",
                "Introspection Commands:\n" +
                "  SHOW JOBS, SHOW CONNECTIONS, SHOW TABLES, SHOW COLUMNS,\n" +
                "  SHOW VARIABLES, SHOW PROFILE, SHOW VERSION, SHOW TAGS.");

            registry.RegisterHelp("VARIABLES",
                "System Variables (@@):\n" +
                "  @@VERSION, @@TRANCOUNT, @@ROWCOUNT, @@ERROR, @@LAST_EXEC_MS, @@PEAK_MEMORY_MB.\n" +
                "Telemetry Variables:\n" +
                "  @@SUBQUERY_CACHE_HITS, @@SUBQUERY_CACHE_MISSES, @@TOTAL_SPILLED_BYTES,\n" +
                "  @@SUBQUERY_SPILL_COUNT, @@SORT_SPILLS, @@AGGREGATE_GROUPS_COUNT.\n" +
                "Session Variables (@):\n" +
                "  Defined via DECLARE @varname. View all with SHOW VARIABLES.");

            registry.RegisterHelp("SECURITY",
                "Zero-Trust Security Guardrails:\n" +
                "  1. Path Isolation: Access to sensitive system paths is blocked.\n" +
                "  2. Script Immutability: Scripts cannot edit .sql/.etlsql/.rptsql files.\n" +
                "  3. Runaway Protection: Limits on file operations and recursion.");

            registry.RegisterHelp("SET",
                "System Configuration:\n" +
                "  SET WHAT_IF ON|OFF - Dry run mode.\n" +
                "  SET BATCHSIZE = n - Stream batch size.\n" +
                "  SET SHOW_PASSWORD ON|OFF - Mask passwords.\n" +
                "  SET TEMPLATE_PATH = 'path' - Dashboard templates.\n" +
                "  SET SPILL_COMPRESSION ON|OFF - Compress data spilled to disk.\n" +
                "  SET PARALLEL_MAX_DEGREE = n - Thread pool limit.");


            // ── CONNECTORS ───────────────────────────────────────────────────
            registry.RegisterHelp("CONNECTION", 
                "Available Types: MSSQL, POSTGRES, ORACLE, ODBC, FLATFILE, EXCEL, JSON, XML, PARQUET, AVRO, API, SFTP, FTP, AZURE_BLOB, SMTP, DIRECTORY, MOCKDB.\n" +
                "Use HELP CONNECTION <type> for specific options.");

            registry.RegisterHelp("CONNECTION", "Relational: MSSQL, SQLSERVER\nOptions: SERVER, DATABASE, USER, PASSWORD, TRUSTED_CONNECTION, CONNECT_TIMEOUT, TABLE, USE_SSL.", "MSSQL");
            registry.RegisterHelp("CONNECTION", "Relational: POSTGRES, PG, NPSQL\nOptions: HOST, PORT, DATABASE, USER, PASSWORD, SSL_MODE, TABLE.", "POSTGRES");
            registry.RegisterHelp("CONNECTION", "Relational: ORACLE\nOptions: HOST, PORT, SERVICE_NAME, TNS_NAME, USER, PASSWORD, TABLE.", "ORACLE");
            registry.RegisterHelp("CONNECTION", "Universal: ODBC Bridge\nOptions: DSN, DRIVER, SERVER, DATABASE, UID, PWD.", "ODBC");
            registry.RegisterHelp("CONNECTION", "File: CSV, TSV, FLATFILE\nOptions: PATH, DELIMITER, HEADER, ENCODING, FORMAT (DELIMITED|FIXED), COMPRESS, ENCRYPT, PASSWORD.", "FLATFILE");
            registry.RegisterHelp("CONNECTION", "File: XLSX, XLS, EXCEL\nOptions: PATH, SHEET, RANGE, HEADER, ENCRYPT, PASSWORD.", "EXCEL");
            registry.RegisterHelp("CONNECTION", "File: JSON (Newtonsoft.Json)\nOptions: PATH, ROOT_PATH, ENCODING, COMPRESS, ENCRYPT.", "JSON");
            registry.RegisterHelp("CONNECTION", "File: XML (XPath)\nOptions: PATH, ROOT_PATH, ENCODING, COMPRESS, ENCRYPT.", "XML");
            registry.RegisterHelp("CONNECTION", "File: Apache Parquet\nOptions: PATH, COMPRESSION (SNAPPY|GZIP|ZSTD), ENCRYPT, PASSWORD.", "PARQUET");
            registry.RegisterHelp("CONNECTION", "File: Apache Avro\nOptions: PATH, SCHEMA_FILE, ENCRYPT, PASSWORD.", "AVRO");
            registry.RegisterHelp("CONNECTION", "Protocol: REST API, HTTP\nOptions: URL, METHOD (GET|POST), AUTH_TYPE (BASIC|BEARER|APIKEY), TOKEN, ROOT_PATH, BODY.", "API");
            registry.RegisterHelp("CONNECTION", "Protocol: SFTP, SSH\nOptions: HOST, PORT, USER, PASSWORD, KEYFILE, PASSPHRASE.", "SFTP");
            registry.RegisterHelp("CONNECTION", "Protocol: FTP, FTPS\nOptions: HOST, PORT, USER, PASSWORD, USE_SSL.", "FTP");
            registry.RegisterHelp("CONNECTION", "Protocol: Azure Blob Storage\nOptions: CONTAINER, ACCOUNT_NAME, ACCOUNT_KEY (or full SAS connection string).", "AZURE_BLOB");
            registry.RegisterHelp("CONNECTION", "Protocol: SMTP Email\nOptions: PORT, USERNAME, PASSWORD, USE_SSL, DEFAULT_FROM.", "SMTP");
            registry.RegisterHelp("CONNECTION", "File: Local System Folder\nOptions: PATH, CREATE (ON|OFF). Enables SELECT listing of files.", "DIRECTORY");
            registry.RegisterHelp("CONNECTION", "Mock: In-memory test database for developer scripts.", "MOCKDB");
        }
    }
}
