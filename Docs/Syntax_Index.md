# ETL-SQL Syntax Index

This document provides a comprehensive index of every keyword, command, function, and configuration option available in the ETL-SQL language. Use this as a central map to find definitions, examples, and help documentation.

---

## 1. Keywords & Commands (Statements)

Statements are the top-level actions in an ETL-SQL script.

| Command | Category | Documentation | Help File |
| :--- | :--- | :--- | :--- |
| `SELECT` | DML / Query | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L339) | [SELECT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SELECT.md) |
| `INSERT` | DML | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [INSERT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/INSERT.md) |
| `UPDATE` | DML | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [UPDATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/UPDATE.md) |
| `DELETE` | DML | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [DELETE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DELETE.md) |
| `MERGE` | DML | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [MERGE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/MERGE.md) |
| `TRUNCATE` | DML | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [TRUNCATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/TRUNCATE.md) |
| `CREATE CONNECTION` | DDL / Conn | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L513) | [CREATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `ALTER CONNECTION` | DDL / Conn | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L655) | [ALTER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/ALTER.md) |
| `DROP CONNECTION` | DDL / Conn | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L667) | [DROP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DROP.md) |
| `CREATE TABLE` | DDL | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [CREATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `ALTER TABLE` | DDL | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [ALTER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/ALTER.md) |
| `DROP TABLE` | DDL | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [DROP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DROP.md) |
| `DECLARE` | Variables | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L11) | [DECLARE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DECLARE.md) |
| `SET @var` | Variables | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L263) | [SET.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SET.md) |
| `IF / ELSE` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L709) | [IF.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/IF.md) |
| `WHILE` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L726) | [WHILE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/WHILE.md) |
| `FOR` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L739) | [FOR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/FOR.md) |
| `FOREACH` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L766) | [FOREACH.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/FOREACH.md) |
| `TRY / CATCH` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [TRY.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/TRY.md) |
| `WAITFOR` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [WAITFOR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/WAITFOR.md) |
| `BREAK` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L796) | [BREAK.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/BREAK.md) |
| `CONTINUE` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L796) | [CONTINUE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CONTINUE.md) |
| `RETURN` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L796) | [RETURN.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/RETURN.md) |
| `THROW` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [THROW.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/THROW.md) |
| `PRINT` | IO | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [PRINT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/PRINT.md) |
| `EXECUTE` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [EXECUTE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/EXECUTE.md) |
| `RUN SCRIPT` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L302) | [RUN.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/RUN.md) |
| `PARALLEL` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [PARALLEL.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/PARALLEL.md) |
| `GO` | Scripting | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [GO.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/GO.md) |
| `ASSERT` | Validation | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [ASSERT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/ASSERT.md) |
| `EXPECT SCHEMA` | Validation | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | - |
| `LINT` | Validation | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [LINT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/LINT.md) |
| `EXPLAIN` | Diagnostics | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | - |
| `SHOW PROFILE` | Diagnostics | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L408) | [SHOW.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `SHOW VARIABLES` | Diagnostics | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L375) | [SHOW.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `CLEAR SESSION` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L335) | [CLEAR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CLEAR.md) |
| `USE PASSWORD` | Session / Security | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L327) | [USE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/USE.md) |
| `REQUIRE VERSION` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L366) | [REQUIRE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/REQUIRE.md) |
| `BULK INSERT` | File IO | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [BULK.INSERT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/BULK.INSERT.md) |
| `COPY FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [COPY.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/COPY.md) |
| `MOVE FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [MOVE.md] |
| `DELETE FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DELETE.md] |
| `ENCRYPT FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [ENCRYPT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/ENCRYPT.md) |
| `SEND FILE` | File IO / Conn | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [SEND/FILE.md] |
| `RECEIVE FILE` | File IO / Conn | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [RECEIVE/FILE.md] |
| `SEND EMAIL` | Notifications | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | - |
| `DOCKER` | Containers | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DOCKER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Operations/DOCKER.md) |
| `CREATE JOB` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [SCHEDULE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SCHEDULE.md) |
| `KILL JOB` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | - |

---

## 2. Data Connectors

Connectors define how to communicate with external data sources.

| Connector | Type | Help File | Supported Options |
| :--- | :--- | :--- | :--- |
| `MSSQL` | SQL | [MSSQL.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/MSSQL.md) | HOST, DATABASE, USER, PASSWORD, TRUSTED_CONNECTION, ... |
| `POSTGRES` | SQL | [POSTGRES.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/POSTGRES.md) | HOST, PORT, DATABASE, USER, PASSWORD, SSL_MODE, ... |
| `ORACLE` | SQL | [ORACLE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/ORACLE.md) | HOST, PORT, SERVICE_NAME, USER, PASSWORD, ... |
| `ODBC` | SQL | [ODBC.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/ODBC.md) | DSN, DRIVER, SERVER, DATABASE, UID, PWD, ... |
| `SNOWFLAKE` | SQL | - | ACCOUNT, WAREHOUSE, DATABASE, SCHEMA, ... |
| `BIGQUERY` | SQL | - | PROJECT_ID, DATASET_ID, KEY_FILE, ... |
| `FLATFILE` | File | [FLATFILE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/FLATFILE.md) | PATH, FORMAT, DELIMITER, HEADER, ENCODING, ... |
| `EXCEL` | File | [EXCEL.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/EXCEL.md) | PATH, SHEET, RANGE, HEADER, ... |
| `JSON` | File | [JSON.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/JSON.md) | PATH, ROOT_PATH, ENCODING, ... |
| `XML` | File | [XML.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/XML.md) | PATH, ROOT_PATH, ENCODING, ... |
| `PARQUET` | File | [PARQUET.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/PARQUET.md) | PATH, COMPRESSION, ... |
| `AVRO` | File | [AVRO.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/AVRO.md) | PATH, ... |
| `SFTP` | Transfer | [SFTP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/SFTP.md) | HOST, PORT, USER, PASSWORD, KEYFILE, PASSPHRASE |
| `FTP` | Transfer | [FTP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/FTP.md) | HOST, PORT, USER, PASSWORD, USE_SSL |
| `AZURE_BLOB` | Transfer | [AZURE_BLOB.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/AZURE_BLOB.md) | ACCOUNT_NAME, ACCOUNT_KEY, CONTAINER |
| `API` / `REST` | Service | [API.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/API.md) | URL, METHOD, AUTH_TYPE, TOKEN, BODY, ROOT_PATH, ... |
| `SMTP` | Service | [SMTP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/SMTP.md) | HOST, PORT, USER, PASSWORD, USE_SSL, DEFAULT_FROM |
| `DIRECTORY` | Service | [DIRECTORY.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/DIRECTORY.md) | PATH, RECURSIVE, ... |
| `MOCKDB` | Testing | [MOCKDB.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Connectors/MOCKDB.md) | - |

---

## 3. Standard Library (Functions)

Functions used within `SELECT`, `WHERE`, `SET`, and other expressions.

| Function | Category | Help File | Description |
| :--- | :--- | :--- | :--- |
| `UPPER(s)` | String | [UPPER.md] | Converts string to uppercase |
| `LOWER(s)` | String | [LOWER.md] | Converts string to lowercase |
| `CONCAT(s1, s2, ...)` | String | [CONCAT.md] | Concatenates multiple strings |
| `LEN(s)` / `LENGTH(s)` | String | [LEN.md] / [LENGTH.md] | Returns string length |
| `SUBSTRING(s, start, len)` | String | [SUBSTRING.md] | Returns part of a string |
| `TRIM(s)` | String | [TRIM.md] | Removes leading/trailing whitespace |
| `REPLACE(s, f, r)` | String | [REPLACE.md] | Replaces occurrences of a substring |
| `CHARINDEX(f, s)` | String | [CHARINDEX.md] | Returns index of first occurrence |
| `GETDATE()` | Date | [GETDATE.md] | Current local datetime |
| `NOW()` | Date | [NOW.md] | Current UTC datetime |
| `DATEADD(u, n, d)` | Date | [DATEADD.md] | Adds units to a date |
| `DATEDIFF(u, d1, d2)` | Date | [DATEDIFF.md] | Difference between dates |
| `DATENAME(u, d)` | Date | [DATENAME.md] | Returns name of date part |
| `DATEPART(u, d)` | Date | [DATEPART.md] | Returns integer date part |
| `EOMONTH(d)` | Date | [EOMONTH.md] | Last day of the month |
| `ABS(n)` | Math | [ABS.md] | Absolute value |
| `ROUND(n, d)` | Math | [ROUND.md] | Rounds to d decimals |
| `FLOOR(n)` | Math | [FLOOR.md] | Largest integer <= n |
| `CEILING(n)` | Math | [CEILING.md] | Smallest integer >= n |
| `RAND()` | Math | [RAND.md] | Random number [0, 1) |
| `COALESCE(v1, v2, ...)`| Logic | [COALESCE.md] | First non-null value |
| `ISNULL(v, d)` | Logic | [ISNULL.md] | Returns d if v is null |
| `IIF(c, t, f)` | Logic | [IIF.md] | Inline IF |
| `CAST(v AS t)` | System | [CAST.md] | Converts v to type t |
| `TRY_CAST(v AS t)` | System | [TRY_CAST.md] | Converts v to type t, NULL on fail |
| `HASHBYTES(a, s)` | System | [HASHBYTES.md] | Returns hash of string |
| `NEWID()` | System | [NEWID.md] | Generates a new GUID |
| `JSON_VALUE(j, p)` | JSON | [JSON_VALUE.md] | Extracts scalar from JSON |
| `JSON_QUERY(j, p)` | JSON | [JSON_QUERY.md] | Extracts object/array from JSON |
| `XMLVALUE(x, p)` | XML | [XMLVALUE.md] | Extracts scalar from XML |
| `FILE_EXISTS(p)` | File | [FILE_EXISTS.md] | 1 if file exists, 0 otherwise |
| `DIRECTORY_EXISTS(p)` | File | [DIRECTORY_EXISTS.md] | 1 if dir exists, 0 otherwise |
| `FILE_LIST(p, m)` | File | [FILE_LIST.md] | Returns table of files in path |

*Note: Over 159 functions are registered. See [Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md) for the full list.*

---

## 4. Variables

### 4.1 System Variables (`@@`)
Read-only counters tracking session state.

| Variable | Description | Help File |
| :--- | :--- | :--- |
| `@@ROWCOUNT` | Rows affected by last statement | [@@ROWCOUNT.md] |
| `@@ERROR` | Last error code (0 = success) | [@@ERROR.md] |
| `@@VERSION` | Engine version string | [@@VERSION.md] |
| `@@TRANCOUNT` | Transaction nesting level | [@@TRANCOUNT.md] |
| `@@FETCH_STATUS` | Last fetch result (0 = success) | [@@FETCH_STATUS.md] |
| `@@LAST_EXEC_MS` | Duration of last statement | [@@LAST_EXEC_MS.md] |
| `@@PEAK_MEMORY_MB` | Peak memory usage in MB | [@@PEAK_MEMORY_MB.md] |
| `@@TOTAL_SPILLED_BYTES` | Cumulative spill disk usage | [@@TOTAL_SPILLED_BYTES.md] |
| `@@SORT_SPILLS` | Count of external sort spills | [@@SORT_SPILLS.md] |
| `@@SUBQUERY_CACHE_HITS` | Subquery cache hit count | [@@SUBQUERY_CACHE_HITS.md] |
| `@@SUBQUERY_CACHE_MISSES` | Subquery cache miss count | [@@SUBQUERY_CACHE_MISSES.md] |

### 4.2 Specialty Variable Types
Used in `DECLARE` to define behavior.

| Type | Purpose | Documentation |
| :--- | :--- | :--- |
| `PATH` | Filesystem path with security validation | [Grammar.md#L63] |
| `JSON` | Validated JSON string | [Grammar.md#L82] |
| `XML` | Validated XML string | [Grammar.md#L106] |
| `LIST` / `LIST(t)` | Ordered collection | [Grammar.md#L137] |
| `MINMAX(t)` | Pair of values (.MIN, .MAX) | [Grammar.md#L151] |
| `RELDATE` | Relative date expression (e.g. 'D-7') | [RelativeDate_Parameters.md] |
| `SENSITIVE` | Masked in output, auto-decrypts `ENC:` | [Grammar.md#L195] |
| `SECRET` | Same as SENSITIVE, purged at session end | [Grammar.md#L213] |
| `MARKDOWN` | Hint for Report Portal rendering | [Grammar.md#L125] |

---

## 5. SET Options (Configuration)

Options configured via `SET <Option> = <Value>` or `SET <Option> ON|OFF`.

| Option | Category | Default | Help File |
| :--- | :--- | :--- | :--- |
| `WHAT_IF` | Execution | OFF | [Options/INDEX.md] |
| `PROFILING` | Execution | OFF | [Options/INDEX.md] |
| `SHOW_PASSWORD` | Security | OFF | [Options/INDEX.md] |
| `LINEAGE` | Data | ON | [Lineage.md] |
| `TELEMETRY` | Metrics | ON | [Options/INDEX.md] |
| `BATCHSIZE` | Performance | 10,000 | [Options/INDEX.md] |
| `JOIN_SPILL_THRESHOLD` | Performance | 100,000 | [Options/INDEX.md] |
| `TEMP_TABLE_SPILL_THRESHOLD` | Performance | 1,000,000 | [Options/INDEX.md] |
| `MAX_PARALLEL_DEGREE` | Performance | CPU Count | [Options/INDEX.md] |
| `ALLOW_FILE_TYPE_ACCESS` | Security | OFF | [Options/INDEX.md] |
| `WEEK_START_DAY` | Localization | Monday | [Options/INDEX.md] |

---

## 6. Report-SQL

Specific to `.rptsql` files and the reporting engine.

### 6.1 Report Objects
| Command | Purpose | Help File |
| :--- | :--- | :--- |
| `CREATE VISUAL` | Defines a chart or filter | [Report/VISUAL.md] |
| `CREATE DATASET` | Defines a data source for visuals | [Report/DATASET.md] |
| `CREATE PAGE` | Defines a dashboard page layout | [Report/PAGE.md] |
| `CREATE CONTAINER` | Groups visuals in a layout | [Report/CONTAINER.md] |
| `CREATE NAVIGATION` | Defines sidebar/top-nav links | [Report/NAVIGATION.md] |
| `CREATE STYLE` | Defines CSS/Theme overrides | [Report/STYLE.md] |

### 6.2 Visual Types
| Type | Category | Help File |
| :--- | :--- | :--- |
| `BAR` / `HBAR` | Chart | [Visuals/BAR.md] |
| `LINE` | Chart | [Visuals/LINE.md] |
| `PIE` / `DONUT` | Chart | [Visuals/PIE.md] |
| `GAUGE` | Chart | [Visuals/GAUGE.md] |
| `HEATMAP` | Chart | [Visuals/HEATMAP.md] |
| `SCATTER` | Chart | [Visuals/SCATTER.md] |
| `GANTT` | Chart | [Visuals/GANTT.md] |
| `TABLE` | Data | [Visuals/TABLE.md] |
| `CARD` | KPI | [Visuals/CARD.md] |
| `SLICER` | Filter | [Visuals/SLICER.md] |
| `DATEPICKER` | Filter | [Visuals/DATEPICKER.md] |
| `SEARCH` | Filter | [Visuals/SEARCH.md] |

---

## 7. Portal & Orchestrator Admin

Commands executed via `EXECUTE portal BEGIN ... END` or `EXECUTE orch BEGIN ... END`.

| Command | Context | Purpose |
| :--- | :--- | :--- |
| `CREATE USER` | Portal | Adds a portal user |
| `GRANT` / `REVOKE` | Portal | Manages folder permissions |
| `PUBLISH REPORT` | Portal | Deploys a script to the portal |
| `CREATE SUBSCRIPTION` | Portal | Schedules email/PDF delivery |
| `RESTART PORTAL` | Portal | Restarts the portal service |
| `CREATE JOB` | Orch | Schedules a recurring task |
| `KILL JOB` | Orch | Stops a running task |
| `SHOW JOBS` | Orch | Lists scheduled tasks |

---

## 8. Operators & Symbols

| Symbol | Name | Usage |
| :--- | :--- : | :--- |
| `@` | Variable | Prefix for user variables (e.g. `@name`) |
| `@@` | System Var | Prefix for system variables (e.g. `@@ROWCOUNT`) |
| `#` | Temp Table | Prefix for in-memory tables (e.g. `#staging`) |
| `!` | Env Set | Prefix for environment sets (e.g. `!PROD`) |
| `.` | Member Access | Dot notation for table/schema or MINMAX members |
| `*` | Wildcard | SELECT all columns or path matching |
| `/*@tag: val */` | Metadata Tag | Row-level or column-level tagging |
| `[` ... `]` | Delimiter | Quotes for identifiers with spaces |
| `''` | String | Single quotes for literal strings |
| `ENC:` | Encrypted | Prefix for ciphertext strings |
