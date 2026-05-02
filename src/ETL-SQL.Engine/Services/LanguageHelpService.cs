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
                "Report-SQL allows building interactive dashboards with visuals, pages, and navigation.\n" +
                "Key Components:\n" +
                "  - DATASET:    Cached data sources (CREATE DATASET #name AS SELECT ...)\n" +
                "  - VISUAL:     Charts and widgets (CREATE VISUAL name AS TYPE ...)\n" +
                "  - PAGE:       Layouts (CREATE PAGE name AS LAYOUT ...)\n" +
                "  - CONTAINER:  Nested groups (CREATE CONTAINER name AS BOX ...)\n" +
                "  - NAVIGATION: Menus (CREATE NAVIGATION name AS TAB ...)\n" +
                "  - STYLE:      Reusable formatting (CREATE STYLE name ...)\n" +
                "Use HELP REPORT <COMPONENT> (e.g. HELP REPORT VISUAL) for details.", "INDEX");

            registry.RegisterHelp("REPORT", 
                "Visuals are the building blocks of reports. Syntax:\n" +
                "CREATE VISUAL <name> AS <TYPE> ( SOURCE = ..., MAPPINGS (...), OPTIONS (...) )\n" +
                "Available Types:\n" +
                "  - Charts: BAR, HBAR, LINE, AREA, PIE, DONUT, SCATTER, BUBBLE, COMBO\n" +
                "  - Specialized: TREEMAP, HEATMAP, BOXPLOT, WATERFALL, GAUGE, FUNNEL\n" +
                "  - Data: TABLE, CARD\n" +
                "  - UI/Filters: SLICER, MULTISELECT, DATEPICKER, SLIDER, SEARCH, IMAGE, TEXT, BUTTON\n" +
                "Use HELP VISUAL <TYPE> (e.g. HELP VISUAL BAR) for type-specific details.", "VISUAL");

            // -- Visual Type Specific Help --
            registry.RegisterHelp("VISUAL", 
                "Type: BAR, HBAR\n" +
                "Mappings: X (categories), Y (metrics), COLOR (breakdown series).\n" +
                "Options: STACKED (ON|OFF), LEGEND (ON|OFF), LABEL_POSITION (INSIDE|OUTSIDE|NONE).\n" +
                "Example:\n  CREATE VISUAL SalesByRegion AS BAR (\n    SOURCE = #data, MAPPINGS (X = Region, Y = Sales)\n  );", "BAR");

            registry.RegisterHelp("VISUAL", "Alias for BAR with horizontal orientation.", "HBAR");

            registry.RegisterHelp("VISUAL", 
                "Type: LINE, AREA\n" +
                "Mappings: X (time/categories), Y (metrics), COLOR (series).\n" +
                "Options: SMOOTH (ON|OFF), SYMBOLS (ON|OFF), AREA (ON|OFF).\n" +
                "Example:\n  CREATE VISUAL Trend AS LINE (\n    SOURCE = #daily, MAPPINGS (X = Date, Y = Total)\n  );", "LINE");

            registry.RegisterHelp("VISUAL", "Alias for LINE with fill enabled.", "AREA");

            registry.RegisterHelp("VISUAL", 
                "Type: PIE, DONUT\n" +
                "Mappings: VALUE (metric), NAME (labels).\n" +
                "Options: ROSE_MODE (ON|OFF), RADIUS (inner%, outer%).\n" +
                "Example:\n  CREATE VISUAL Mix AS PIE (\n    SOURCE = #data, MAPPINGS (VALUE = Share, NAME = Category)\n  );", "PIE");

            registry.RegisterHelp("VISUAL", 
                "Type: SCATTER, BUBBLE\n" +
                "Mappings: X (numeric), Y (numeric), SIZE (bubble magnitude), COLOR (series).\n" +
                "Options: SHOW_REGRESSION (ON|OFF).\n" +
                "Example:\n  CREATE VISUAL Corr AS SCATTER (\n    SOURCE = #stats, MAPPINGS (X = Price, Y = Volume, SIZE = Margin)\n  );", "SCATTER");

            registry.RegisterHelp("VISUAL", "Alias for SCATTER with a SIZE mapping.", "BUBBLE");

            registry.RegisterHelp("VISUAL", 
                "Type: TABLE\n" +
                "Columns: Define visible columns and aliases.\n" +
                "Properties: GRAND_TOTAL (SUM|AVG|COUNT), PAGE_SIZE (n), STRIPED (ON|OFF).\n" +
                "Formatting: Condition THEN 'color'.\n" +
                "Example:\n  CREATE VISUAL TopUsers AS TABLE (\n    SOURCE = #users, COLUMNS (Name, Email, [Last Login] AS Date)\n  );", "TABLE");

            registry.RegisterHelp("VISUAL", 
                "Type: CARD\n" +
                "Mappings: VALUE (the large metric), LABEL (subtitle), GOAL (comparison target).\n" +
                "Formatting: GREEN IF > GOAL, RED IF < GOAL.\n" +
                "Example:\n  CREATE VISUAL Revenue AS CARD (\n    SOURCE = (SELECT SUM(val) FROM #t), MAPPINGS (VALUE = 1, LABEL = 'Total Revenue')\n  );", "CARD");

            registry.RegisterHelp("VISUAL", 
                "Type: SLICER, MULTISELECT\n" +
                "Mappings: VALUE (column to filter by).\n" +
                "Actions: ON_CHANGE = SET_PARAMETER(@var, VALUE).\n" +
                "Example:\n  CREATE VISUAL YearFilter AS SLICER (\n    SOURCE = (SELECT DISTINCT Yr FROM #t), MAPPINGS (VALUE = Yr)\n  );", "SLICER");

            registry.RegisterHelp("VISUAL", "Alias for SLICER allowing multiple selections.", "MULTISELECT");

            registry.RegisterHelp("VISUAL", "Interactive visual for date/time range selection. Does not require a SOURCE.", "DATEPICKER");
            registry.RegisterHelp("VISUAL", "Interactive slider for numeric range selection. Does not require a SOURCE.", "SLIDER");
            registry.RegisterHelp("VISUAL", "Global search bar for filtering dashboard data. Does not require a SOURCE.", "SEARCH");

            registry.RegisterHelp("VISUAL", 
                "Type: IMAGE\n" +
                "Properties: SRC (path/url), FIT (contain|cover|fill).\n" +
                "Example:\n  CREATE VISUAL Logo AS IMAGE ( OPTIONS (SRC = 'https://site.com/logo.png', FIT = 'contain') );", "IMAGE");

            registry.RegisterHelp("VISUAL", 
                "Type: BUTTON\n" +
                "Properties: TITLE, ACTIONS (ON_CLICK = SET_PARAMETER(...)|REFRESH|LINK).\n" +
                "Example:\n  CREATE VISUAL ResetBtn AS BUTTON ( TITLE = 'Reset', ACTIONS (ON_CLICK = REFRESH) );", "BUTTON");

            registry.RegisterHelp("VISUAL", 
                "Type: TEXT\n" +
                "Properties: VALUE (static text or markdown).\n" +
                "Example:\n  CREATE VISUAL Info AS TEXT ( OPTIONS (VALUE = '# Dashboard Info\nThis is a report.') );", "TEXT");

            registry.RegisterHelp("VISUAL", 
                "Type: COMBO\n" +
                "Mappings: X, Y (Bar), Y2 (Line), COLOR.\n" +
                "Example:\n  CREATE VISUAL RevMix AS COMBO (\n    SOURCE = #data, MAPPINGS (X = Month, Y = Sales, Y2 = Target)\n  );", "COMBO");

            registry.RegisterHelp("VISUAL", 
                "Type: TREEMAP\n" +
                "Mappings: NAME (labels), VALUE (size), COLOR (grouping).\n" +
                "Options: SHOW_LABELS (ON|OFF).\n" +
                "Example:\n  CREATE VISUAL MarketCap AS TREEMAP (\n    SOURCE = #data, MAPPINGS (NAME = Symbol, VALUE = Cap)\n  );", "TREEMAP");

            registry.RegisterHelp("VISUAL", 
                "Type: HEATMAP\n" +
                "Mappings: X (categories), Y (categories), VALUE (magnitude).\n" +
                "Example:\n  CREATE VISUAL Correlation AS HEATMAP (\n    SOURCE = #matrix, MAPPINGS (X = ColA, Y = ColB, VALUE = Val)\n  );", "HEATMAP");

            registry.RegisterHelp("VISUAL", 
                "Type: GAUGE\n" +
                "Mappings: VALUE (current metric).\n" +
                "Options: MIN (n), MAX (n).\n" +
                "Example:\n  CREATE VISUAL Speed AS GAUGE (\n    SOURCE = (SELECT top 1 kmh FROM #s), MAPPINGS (VALUE = kmh), OPTIONS (MAX = 200)\n  );", "GAUGE");

            registry.RegisterHelp("VISUAL", 
                "Type: BOXPLOT\n" +
                "Mappings: NAME (category), MIN, Q1, MEDIAN, Q3, MAX (numeric values).\n" +
                "Example:\n  CREATE VISUAL Stats AS BOXPLOT (\n    SOURCE = #stats, MAPPINGS (NAME = Group, MIN = m, Q1 = q1, MEDIAN = med, Q3 = q3, MAX = mx)\n  );", "BOXPLOT");

            registry.RegisterHelp("VISUAL", 
                "Type: WATERFALL\n" +
                "Mappings: NAME (step), VALUE (delta).\n" +
                "Example:\n  CREATE VISUAL CashFlow AS WATERFALL (\n    SOURCE = #flow, MAPPINGS (NAME = Stage, VALUE = Amount)\n  );", "WATERFALL");

            registry.RegisterHelp("VISUAL", 
                "Type: FUNNEL\n" +
                "Mappings: NAME (stage), VALUE (count).\n" +
                "Example:\n  CREATE VISUAL Pipeline AS FUNNEL (\n    SOURCE = #leads, MAPPINGS (NAME = Phase, VALUE = Cnt)\n  );", "FUNNEL");

            // -- Report Component Top-Level Shortcuts --
            registry.RegisterHelp("VISUAL", "Syntax: CREATE VISUAL <name> AS <TYPE> ( ... body ... )\nUse HELP REPORT VISUAL for properties or HELP VISUAL <TYPE> for specifics.");
            registry.RegisterHelp("CONTAINER", "Syntax: CREATE CONTAINER <name> AS BOX|SCROLL ( ... body ... )\nGroups visuals within a page layout using its own STRUCTURE and MAP.");
            registry.RegisterHelp("PAGE", "Syntax: CREATE PAGE <name> AS LAYOUT ( ... body ... )\nDefines a report dashboard layout using CSS Grid areas (STRUCTURE).");
            registry.RegisterHelp("BUTTON", "Syntax: CREATE BUTTON <name> AS BACK|REFRESH|LINK ( TITLE = '...', ACTIONS = (...) )\nInteractive button for UI actions.");
            registry.RegisterHelp("NAVIGATION", "Syntax: CREATE NAVIGATION <name> AS TAB|BUTTON|LINK ( ORIENTATION = ..., PAGES = (...) )\nGlobal navigation between report pages.");
            registry.RegisterHelp("DATASET", "Syntax: CREATE DATASET #name [REFRESH EVERY 'time'] AS (SELECT ...)\nDefines a persistent or cached result set shared across reports.");
            registry.RegisterHelp("STYLE", "Syntax: CREATE STYLE <name> ( THEME = dark, BACKGROUND = '#...', ... )\nDefines reusable styling tokens for visuals and pages.");

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
            registry.RegisterHelp("DECLARE", "Syntax: DECLARE @VariableName <DATATYPE> [ = <VALUE> ];\nTypes: STRING, INT, DECIMAL, BOOL, DATE, JSON, XML, MARKDOWN, LIST, PATH, MINMAX, SENSITIVE, SECRET, ENCRYPTED.");
            registry.RegisterHelp("SET", 
                "Syntax:\n" +
                "  SET @Variable = <value>          -- Assignment\n" +
                "  SET <OPTION> = ON|OFF|<n>        -- Engine Configuration\n\n" +
                "Common Options:\n" +
                "  - WHAT_IF:        Dry-run mode (suppresses destructive actions).\n" +
                "  - PROFILING:      Detailed statement-level timing.\n" +
                "  - SHOW_PASSWORD:  Reveals SENSITIVE variables in output.\n" +
                "  - BATCHSIZE:      Rows per pipeline batch (default 10,000).\n" +
                "  - ALLOW_FILE_TYPE_ACCESS: Overrides security whitelist.\n" +
                "Use HELP SET <OPTION> for details.");
            registry.RegisterHelp("IF", "Syntax: IF <CONDITION> BEGIN ... END [ ELSE BEGIN ... END ];\nProvides conditional branching logic.");
            registry.RegisterHelp("WHILE", "Syntax: WHILE <CONDITION> BEGIN ... END;\nRepeats a block of code as long as the condition is true. Use BREAK to exit and CONTINUE to skip to the next iteration.");
            registry.RegisterHelp("FOR", "Numeric Syntax: FOR @idx = <start> TO <end> [STEP <n>] BEGIN ... END;\nQuery Syntax: FOR @row IN (SELECT ...) BEGIN ... END;\nIterates over a range or the rows of a query result.");
            registry.RegisterHelp("FOREACH", "Syntax: FOREACH @item IN <COLLECTION> BEGIN ... END;\nIterates over a LIST, #temp table, or JSON array.");
            
            registry.RegisterHelp("LOOP", 
                "ETL-SQL supports several looping constructs:\n" +
                "  - WHILE:   Conditional loop.\n" +
                "  - FOR:     Numeric range or query row iteration.\n" +
                "  - FOREACH: Collection/Table iteration.\n" +
                "Use HELP <TYPE> for specific syntax.");
            registry.RegisterHelp("TRY", "Syntax: BEGIN TRY ... END TRY BEGIN CATCH ... END CATCH;\nError handling block.");
            registry.RegisterHelp("TRANSACTION", "Syntax: BEGIN TRANSACTION; COMMIT; ROLLBACK;\nControls atomic data operations. Use @TranCount to check nesting.");
            registry.RegisterHelp("PARALLEL", "Syntax: PARALLEL BEGIN ... END;\nRuns enclosed statements concurrently using a thread pool.");
            registry.RegisterHelp("BULK INSERT", "Syntax: BULK INSERT <TARGET> FROM <FILE_PATH> WITH ( BATCH_SIZE=n, MAX_ERRORS=n, ERROR_LOG_PATH='path', FIRST_ROW=n, LAST_ROW=n );\nHigh-performance file loading into remote or local tables.");
            
            // ── CORE STATEMENTS ──────────────────────────────────────────────
            registry.RegisterHelp("SELECT", "Syntax: SELECT [TOP n] <cols> [INTO <table>] FROM <src> [JOIN...] [WHERE...] [GROUP BY...] [ORDER BY...]\nQueries data from connections or #temp tables.");
            registry.RegisterHelp("FROM", "Part of a SELECT statement. Specifies the source connection or #temp table to query from.");
            registry.RegisterHelp("WHERE", "Part of a SELECT, UPDATE, or DELETE statement. Filters rows based on a boolean condition.");
            registry.RegisterHelp("GROUP BY", "Part of a SELECT statement. Groups rows that have the same values into summary rows (like finding the SUM of sales per region).");
            registry.RegisterHelp("ORDER BY", "Part of a SELECT statement. Sorts the result set by one or more columns in ascending (ASC) or descending (DESC) order.");
            registry.RegisterHelp("HAVING", "Part of a SELECT statement. Filters groups based on an aggregate condition (used after GROUP BY).");
            registry.RegisterHelp("LIMIT", "Syntax: LIMIT <n> [OFFSET <m>]\nRestricts the number of rows returned by a query. Use OFFSET to skip rows.");
            registry.RegisterHelp("OFFSET", "Skips a specific number of rows before beginning to return rows from a query.");
            registry.RegisterHelp("INSERT", "Syntax: INSERT INTO <target> [(cols)] SELECT... | VALUES(...)\nAdds new rows to a table.");
            registry.RegisterHelp("UPDATE", "Syntax: UPDATE <target> SET <col>=<val> [WHERE...]\nModifies existing rows.");
            registry.RegisterHelp("DELETE", "Syntax: DELETE FROM <target> [WHERE...]\nRemoves rows from a table.");
            registry.RegisterHelp("TRUNCATE", "Syntax: TRUNCATE TABLE <target>;\nRemoves all rows from a table quickly by deallocating pages.");
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
                "  SHOW CONNECTIONS      -- List all active data sources.\n" +
                "  SHOW VARIABLES        -- List all current variables.\n" +
                "  SHOW PROFILE          -- Execution timing (requires SET PROFILING ON).\n" +
                "  SHOW JOBS             -- List active background/scheduled jobs.\n" +
                "  SHOW TABLES           -- List tables for a specific connection.\n" +
                "  SHOW TAGS             -- List lineage tags applied in session.\n" +
                "  SHOW VERSION          -- Short version string.\n\n" +
                "Use SHOW <item> INTO #temp to query the results.");

            registry.RegisterHelp("VARIABLES",
                "System Variables (@@) are read-only values tracking session state:\n" +
                "  - @@ROWCOUNT:       Rows affected/returned by last stmt.\n" +
                "  - @@ERROR:          Last error code (0 = success).\n" +
                "  - @@VERSION:        Full engine version metadata.\n" +
                "  - @@TRANCOUNT:      Current transaction nesting level.\n" +
                "  - @@FETCH_STATUS:   Cursor/Loop status (0=OK, -1=EOF).\n" +
                "  - @@LAST_EXEC_MS:   Duration of last statement.\n" +
                "  - @@PEAK_MEMORY_MB: Peak RAM usage this session.\n" +
                "  - @@TOTAL_SPILLED_BYTES: Cumulative disk spill usage.\n" +
                "  - @@SUBQUERY_CACHE_HITS/MISSES: Cache performance stats.\n" +
                "  - @@SORT_SPILLS:    Number of external sorts to disk.\n" +
                "Session Variables (@):\n" +
                "  Defined via DECLARE @varname. View all with SHOW VARIABLES.");

            registry.RegisterHelp("VARIABLES", "Rows affected by the last DML or returned by the last SELECT.", "@@ROWCOUNT");
            registry.RegisterHelp("VARIABLES", "Integer error code of the preceding statement (0 = success).", "@@ERROR");
            registry.RegisterHelp("VARIABLES", "Full engine version and build metadata string.", "@@VERSION");
            registry.RegisterHelp("VARIABLES", "Current transaction nesting level (0 = no active transaction).", "@@TRANCOUNT");
            registry.RegisterHelp("VARIABLES", "Cursor/Foreach fetch status. 0 = Success, -1 = End of list.", "@@FETCH_STATUS");
            registry.RegisterHelp("VARIABLES", "Cumulative bytes written to disk for all spill operations this session.", "@@TOTAL_SPILLED_BYTES");
            registry.RegisterHelp("VARIABLES", "Milliseconds taken by the last statement.", "@@LAST_EXEC_MS");
            registry.RegisterHelp("VARIABLES", "Peak working-set memory in MB for the current process.", "@@PEAK_MEMORY_MB");
            registry.RegisterHelp("VARIABLES", "Scalar subquery results retrieved from session cache.", "@@SUBQUERY_CACHE_HITS");
            registry.RegisterHelp("VARIABLES", "Scalar subquery evaluations that required execution.", "@@SUBQUERY_CACHE_MISSES");
            registry.RegisterHelp("VARIABLES", "External sort runs that spilled to disk this session.", "@@SORT_SPILLS");

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


            // ── SECURITY & LINEAGE ───────────────────────────────────────────
            registry.RegisterHelp("SECURITY",
                "ETL-SQL Zero-Trust Security Features:\n" +
                "  - USE PASSWORD = '...':  Set session key for ENC: decryption.\n" +
                "  - ENCRYPT VALUE '...':   Produce an ENC: ciphertext string.\n" +
                "  - SENSITIVE/SECRET:      Variable types that mask output.\n" +
                "  - Guardrails:            Engine blocks unauthorized path access.");

            registry.RegisterHelp("TAGS",
                "Syntax: TAG <SOURCE> WITH (Key=Value);\n" +
                "Purpose: Attaches metadata/lineage to data as it flows through the engine.\n" +
                "Query:   SELECT * FROM SHOW_TAGS();");

            // ── CONNECTORS ───────────────────────────────────────────────────
            registry.RegisterHelp("CONNECTION", 
                "Connections link ETL-SQL to external data sources. Syntax:\n" +
                "CREATE CONNECTION <name> ON <TYPE>(<conn_string>) [WITH(...)];\n\n" +
                "Available Types by Category:\n" +
                "  - Relational: MSSQL, POSTGRES, ORACLE, ODBC, MOCKDB\n" +
                "  - Flat File:  FLATFILE (CSV/TSV), EXCEL, JSON, XML, PARQUET, AVRO\n" +
                "  - Protocol:   API (REST), SFTP, FTP, AZURE_BLOB, SMTP\n" +
                "  - Specialized: DIRECTORY (folder listing), REPORTPORTAL, ORCHESTRATOR\n\n" +
                "Use HELP CONNECTION <type> (e.g. HELP CONNECTION MSSQL) for specific options.");

            registry.RegisterHelp("CONNECTION", 
                "Connections link ETL-SQL to external data sources. Syntax:\n" +
                "CREATE CONNECTION <name> ON <TYPE>(<conn_string>) [WITH(...)];\n\n" +
                "Available Types by Category:\n" +
                "  - Relational: MSSQL, POSTGRES, ORACLE, ODBC, MOCKDB\n" +
                "  - Flat File:  FLATFILE (CSV/TSV), EXCEL, JSON, XML, PARQUET, AVRO\n" +
                "  - Protocol:   API (REST), SFTP, FTP, AZURE_BLOB, SMTP\n" +
                "  - Specialized: DIRECTORY (folder listing), REPORTPORTAL, ORCHESTRATOR\n\n" +
                "Use HELP CONNECTION <type> (e.g. HELP CONNECTION MSSQL) for specific options.", "INDEX");

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

            registry.RegisterHelp("REPORT",
                "Report-SQL enables building interactive dashboards using SQL scripts.\n" +
                "Core Components:\n" +
                "  - DATASET: Cached data sources.\n" +
                "  - VISUAL:  Charts, tables, and cards.\n" +
                "  - PAGE:    Layouts containing visuals.\n" +
                "  - CONTAINER: Nested layouts.\n" +
                "  - NAVIGATION: Global menus.\n" +
                "Use HELP VISUAL <type> for specific chart help.");
        }
    }
}
