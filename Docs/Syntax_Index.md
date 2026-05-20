# ETL-SQL Syntax Index

This document provides a comprehensive index of every keyword, command, function, and configuration option available in the ETL-SQL language. Use this as a central map to find definitions, examples, and help documentation.

> [!NOTE]
> This is a cross-reference inventory, not the primary explanation of the language. Use the reference docs for authoritative syntax and examples. The `Help File` column intentionally points at source-tree help assets and may use local/file links until this index is generated or normalized for release packaging.

---

## 1. Keywords & Commands (Statements)

Statements are the top-level actions in an ETL-SQL script.

| Command | Category | Documentation | Help File |
| :--- | :--- | :--- | :--- |
| `SELECT` | DML / Query | [Grammar.md](../Docs/Reference/Grammar.md) | [SELECT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SELECT.md) |
| `INSERT` | DML | [Grammar.md](../Docs/Reference/Grammar.md) | [INSERT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/INSERT.md) |
| `UPDATE` | DML | [Grammar.md](../Docs/Reference/Grammar.md) | [UPDATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/UPDATE.md) |
| `DELETE` | DML | [Grammar.md](../Docs/Reference/Grammar.md) | [DELETE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DELETE.md) |
| `MERGE` | DML | [Grammar.md](../Docs/Reference/Grammar.md) | [MERGE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/MERGE.md) |
| `TRUNCATE` | DML | [Grammar.md](../Docs/Reference/Grammar.md) | [TRUNCATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/TRUNCATE.md) |
| `CREATE CONNECTION` | DDL / Conn | [Grammar.md](../Docs/Reference/Grammar.md) | [CREATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `ALTER CONNECTION` | DDL / Conn | [Grammar.md](../Docs/Reference/Grammar.md) | [ALTER.md](../src/ETL-SQL.Core/Resources/Help/Keywords/ALTER.md) |
| `DROP CONNECTION` | DDL / Conn | [Grammar.md](../Docs/Reference/Grammar.md) | [DROP.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DROP.md) |
| `CREATE TABLE` | DDL | [Grammar.md](../Docs/Reference/Grammar.md) | [CREATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `ALTER TABLE` | DDL | [Grammar.md](../Docs/Reference/Grammar.md) | [ALTER.md](../src/ETL-SQL.Core/Resources/Help/Keywords/ALTER.md) |
| `DROP TABLE` | DDL | [Grammar.md](../Docs/Reference/Grammar.md) | [DROP.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DROP.md) |
| `DECLARE` | Variables | [Grammar.md](../Docs/Reference/Grammar.md) | [DECLARE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DECLARE.md) |
| `SET @var` | Variables | [Grammar.md](../Docs/Reference/Grammar.md) | [SET.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SET.md) |
| `IF / ELSE` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [IF.md](../src/ETL-SQL.Core/Resources/Help/Keywords/IF.md) |
| `WHILE` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [WHILE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/WHILE.md) |
| `FOR` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [FOR.md](../src/ETL-SQL.Core/Resources/Help/Keywords/FOR.md) |
| `FOREACH` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [FOREACH.md](../src/ETL-SQL.Core/Resources/Help/Keywords/FOREACH.md) |
| `TRY / CATCH` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [TRY.md](../src/ETL-SQL.Core/Resources/Help/Keywords/TRY.md) |
| `WAITFOR` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [WAITFOR.md](../src/ETL-SQL.Core/Resources/Help/Keywords/WAITFOR.md) |
| `BREAK` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [BREAK.md](../src/ETL-SQL.Core/Resources/Help/Keywords/BREAK.md) |
| `CONTINUE` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [CONTINUE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CONTINUE.md) |
| `RETURN` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [RETURN.md](../src/ETL-SQL.Core/Resources/Help/Keywords/RETURN.md) |
| `THROW` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | [THROW.md](../src/ETL-SQL.Core/Resources/Help/Keywords/THROW.md) |
| `BEGIN TRANSACTION` | Session | [Grammar.md](../Docs/Reference/Grammar.md) | [TRANSACTION.md](../src/ETL-SQL.Core/Resources/Help/Keywords/TRANSACTION.md) |
| `COMMIT` | Session | [Grammar.md](../Docs/Reference/Grammar.md) | [TRANSACTION.md](../src/ETL-SQL.Core/Resources/Help/Keywords/TRANSACTION.md) |
| `ROLLBACK` | Session | [Grammar.md](../Docs/Reference/Grammar.md) | [TRANSACTION.md](../src/ETL-SQL.Core/Resources/Help/Keywords/TRANSACTION.md) |
| `PRINT` | IO | [Grammar.md](../Docs/Reference/Grammar.md) | [PRINT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/PRINT.md) |
| `EXECUTE` | Orchestration | [Grammar.md](../Docs/Reference/Grammar.md) | [EXECUTE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/EXECUTE.md) |
| `RUN SCRIPT` | Orchestration | [Grammar.md](../Docs/Reference/Grammar.md) | [RUN.md](../src/ETL-SQL.Core/Resources/Help/Keywords/RUN.md) |
| `PARALLEL` | Orchestration | [Grammar.md](../Docs/Reference/Grammar.md) | [PARALLEL.md](../src/ETL-SQL.Core/Resources/Help/Keywords/PARALLEL.md) |
| `GO` | Scripting | [Grammar.md](../Docs/Reference/Grammar.md) | [GO.md](../src/ETL-SQL.Core/Resources/Help/Keywords/GO.md) |
| `ASSERT` | Validation | [Grammar.md](../Docs/Reference/Grammar.md) | [ASSERT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/ASSERT.md) |
| `EXPECT SCHEMA` | Validation | [Grammar.md](../Docs/Reference/Grammar.md) | - |
| `LINT` | Validation | [Grammar.md](../Docs/Reference/Grammar.md) | [LINT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/LINT.md) |
| `EXPLAIN` | Diagnostics | [Grammar.md](../Docs/Reference/Grammar.md) | - |
| `SHOW PROFILE` | Diagnostics | [Grammar.md](../Docs/Reference/Grammar.md) | [SHOW.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `SHOW VARIABLES` | Diagnostics | [Grammar.md](../Docs/Reference/Grammar.md) | [SHOW.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `SHOW LOCAL VARIABLES`| Diagnostics| [Grammar.md](../Docs/Reference/Grammar.md) | [SHOW.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `SHOW CONNECTION <conn> CONFIG` | Diagnostics| [Grammar.md](../Docs/Reference/Grammar.md) | [CONFIG.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CONFIG.md) |
| `SHOW CONNECTIONS` | Diagnostics| [Grammar.md](../Docs/Reference/Grammar.md) | [SHOW.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `CLEAR SESSION` | Session | [Grammar.md](../Docs/Reference/Grammar.md) | [CLEAR.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CLEAR.md) |
| `USE PASSWORD` | Session / Security | [Grammar.md](../Docs/Reference/Grammar.md) | [USE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/USE.md) |
| `USE SETS` | Session | [Grammar.md](../Docs/Reference/Grammar.md) | [USE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/USE.md) |
| `CREATE SETS` | Session | [Grammar.md](../Docs/Reference/Grammar.md) | [CREATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `DROP SETS` | Session | [Grammar.md](../Docs/Reference/Grammar.md) | [DROP.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DROP.md) |
| `REQUIRE VERSION` | Session | [Grammar.md](../Docs/Reference/Grammar.md) | [REQUIRE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/REQUIRE.md) |
| `BULK INSERT` | File IO | [Grammar.md](../Docs/Reference/Grammar.md) | [BULK.INSERT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/BULK.INSERT.md) |
| `COPY FILE` | File IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [COPY.md](../src/ETL-SQL.Core/Resources/Help/Keywords/COPY.md) |
| `MOVE FILE` | File IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [MOVE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/MOVE.md) |
| `DELETE FILE` | File IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DELETE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DELETE.md) |
| `ENCRYPT FILE` | File IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [ENCRYPT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/ENCRYPT.md) |
| `SEND FILE` | File IO / Conn | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [SEND/FILE.md](../src/ETL-SQL.Core/Resources/Help/Operations/SEND/FILE.md) |
| `RECEIVE FILE` | File IO / Conn | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [RECEIVE/FILE.md](../src/ETL-SQL.Core/Resources/Help/Operations/RECEIVE/FILE.md) |
| `SEND EMAIL` | Notifications | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [SEND/EMAIL.md](../src/ETL-SQL.Core/Resources/Help/Operations/SEND/EMAIL.md) |
| `DOCKER` | Containers | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DOCKER.md](../src/ETL-SQL.Core/Resources/Help/Operations/DOCKER.md) |
| `CREATE JOB` | Orchestration | [Grammar.md](../Docs/Reference/Grammar.md) | [SCHEDULE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SCHEDULE.md) |
| `KILL JOB` | Orchestration | [Grammar.md](../Docs/Reference/Grammar.md) | - |
| `CREATE INDEX` | DDL | [Grammar.md](../Docs/Reference/Grammar.md) | [CREATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `CREATE PROCEDURE` | DDL | [Grammar.md](../Docs/Reference/Grammar.md) | [CREATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `CREATE FUNCTION` | DDL | [Grammar.md](../Docs/Reference/Grammar.md) | [CREATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `GENERATE` | DML | [Grammar.md](../Docs/Reference/Grammar.md) | [GENERATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/GENERATE.md) |
| `CASE` | Expressions | [Grammar.md](../Docs/Reference/Grammar.md) | [CASE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CASE.md) |
| `WITH` | CTE | [Grammar.md](../Docs/Reference/Grammar.md) | [WITH.md](../src/ETL-SQL.Core/Resources/Help/Keywords/WITH.md) |
| `WITH RECURSIVE` | CTE | [Grammar.md](../Docs/Reference/Grammar.md) | [WITH.md](../src/ETL-SQL.Core/Resources/Help/Keywords/WITH.md) |
| `PIVOT` / `UNPIVOT` | DML / Transform | [Grammar.md](../Docs/Reference/Grammar.md) | [PIVOT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/PIVOT.md) |
| `MATCH_RECOGNIZE` | DML / Pattern Matching | [Grammar.md](../Docs/Reference/Grammar.md#59-match_recognize) | - |
| `EXPORT REPORT` | Orchestration | [Grammar.md](../Docs/Reference/Grammar.md) | [EXPORT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/EXPORT.md) |
| `SUBSCRIPTION` | Orchestration | [Grammar.md](../Docs/Reference/Grammar.md) | [SUBSCRIPTION.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SUBSCRIPTION.md) |
| `RELDATE` | Variables | [RelativeDate_Parameters.md](../Docs/Reference/RelativeDate_Parameters.md) | [RELDATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/RELDATE.md) |
| `RAISEERROR` | Flow Control | [Grammar.md](../Docs/Reference/Grammar.md) | - |
| `HELP` | Diagnostics | [Grammar.md](../Docs/Reference/Grammar.md) | - |
| `ANALYZE` | Diagnostics | [Grammar.md](../Docs/Reference/Grammar.md) | - |
| `RENAME FILE` | File IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [RENAME.md](../src/ETL-SQL.Core/Resources/Help/Keywords/RENAME.md) |
| `COMPRESS FILE` | File IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [COMPRESS.md](../src/ETL-SQL.Core/Resources/Help/Keywords/COMPRESS.md) |
| `DECOMPRESS FILE` | File IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DECOMPRESS.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DECOMPRESS.md) |
| `DECRYPT FILE` | File IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DECRYPT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DECRYPT.md) |
| `CREATE DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [CREATE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `COPY DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [COPY.md](../src/ETL-SQL.Core/Resources/Help/Keywords/COPY.md) |
| `MOVE DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [MOVE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/MOVE.md) |
| `RENAME DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [RENAME.md](../src/ETL-SQL.Core/Resources/Help/Keywords/RENAME.md) |
| `DELETE DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DELETE.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DELETE.md) |
| `DELETE DIRECTORY_CONTENTS`| Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | - |
| `COMPRESS DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [COMPRESS.md](../src/ETL-SQL.Core/Resources/Help/Keywords/COMPRESS.md) |
| `DECOMPRESS DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DECOMPRESS.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DECOMPRESS.md) |
| `ENCRYPT DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [ENCRYPT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/ENCRYPT.md) |
| `DECRYPT DIRECTORY` | Dir IO | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DECRYPT.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DECRYPT.md) |
| `CREATE SSH_KEY_PAIR` | Security | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [SSH_KEY_PAIR.md](../src/ETL-SQL.Core/Resources/Help/Keywords/SSH_KEY_PAIR.md) |
| `CREATE PGP_KEY_PAIR` | Security | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [PGP_KEY_PAIR.md](../src/ETL-SQL.Core/Resources/Help/Keywords/PGP_KEY_PAIR.md) |
| `START DOCKER` | Containers | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DOCKER.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DOCKER.md) |
| `STOP DOCKER` | Containers | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DOCKER.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DOCKER.md) |
| `PAUSE DOCKER` | Containers | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DOCKER.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DOCKER.md) |
| `CLOSE DOCKER` | Containers | [Specialized_Operations.md](../Docs/Reference/Specialized_Operations.md) | [DOCKER.md](../src/ETL-SQL.Core/Resources/Help/Keywords/DOCKER.md) |
| `CREATE USER` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `ALTER USER` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `DROP USER` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `CREATE GROUP` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `DROP GROUP` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `ADD USER TO GROUP` | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `CREATE FOLDER` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `DROP FOLDER` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `GRANT` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `REVOKE` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `PUBLISH REPORT` | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `ALTER REPORT` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `DROP REPORT` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `REFRESH REPORT` | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `REFRESH DATASET` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `ALTER DATASET` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `DROP DATASET` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `REBUILD SNAPSHOT` | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `DROP SNAPSHOT` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `CREATE REFRESH JOB` | Portal / Orchestrator | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `DROP REFRESH JOB` | Portal / Orchestrator | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `SHOW USERS` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `SHOW REPORTS` (portal) | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `DISCONNECT USER` | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |
| `REVOKE TOKENS FOR USER` | Portal Admin | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | - |

---

## 2. Data Connectors

Connectors define how to communicate with external data sources.

| Connector | Type | Help File | Supported Options |
| :--- | :--- | :--- | :--- |
| `MSSQL` | SQL | [MSSQL.md](../src/ETL-SQL.Core/Resources/Help/Connectors/MSSQL.md) | HOST, DATABASE, USER, PASSWORD, TRUSTED_CONNECTION, ... |
| `POSTGRES` | SQL | [POSTGRES.md](../src/ETL-SQL.Core/Resources/Help/Connectors/POSTGRES.md) | HOST, PORT, DATABASE, USER, PASSWORD, SSL_MODE, ... |
| `ORACLE` | SQL | [ORACLE.md](../src/ETL-SQL.Core/Resources/Help/Connectors/ORACLE.md) | HOST, PORT, SERVICE_NAME, USER, PASSWORD, ... |
| `ODBC` | SQL | [ODBC.md](../src/ETL-SQL.Core/Resources/Help/Connectors/ODBC.md) | DSN, DRIVER, SERVER, DATABASE, UID, PWD, ... |
| `SNOWFLAKE` | SQL | - | ACCOUNT, WAREHOUSE, DATABASE, SCHEMA, ... |
| `BIGQUERY` | SQL | - | PROJECT_ID, DATASET_ID, KEY_FILE, ... |
| `FLATFILE` | File | [FLATFILE.md](../src/ETL-SQL.Core/Resources/Help/Connectors/FLATFILE.md) | PATH, FORMAT, DELIMITER, HEADER, ENCODING, ... |
| `EXCEL` | File | [EXCEL.md](../src/ETL-SQL.Core/Resources/Help/Connectors/EXCEL.md) | PATH, SHEET, RANGE, HEADER, ... |
| `JSON` | File | [JSON.md](../src/ETL-SQL.Core/Resources/Help/Connectors/JSON.md) | PATH, ROOT_PATH, ENCODING, ... |
| `XML` | File | [XML.md](../src/ETL-SQL.Core/Resources/Help/Connectors/XML.md) | PATH, ROOT_PATH, ENCODING, ... |
| `PARQUET` | File | [PARQUET.md](../src/ETL-SQL.Core/Resources/Help/Connectors/PARQUET.md) | PATH, COMPRESSION, ... |
| `AVRO` | File | [AVRO.md](../src/ETL-SQL.Core/Resources/Help/Connectors/AVRO.md) | PATH, ... |
| `SFTP` | Transfer | [SFTP.md](../src/ETL-SQL.Core/Resources/Help/Connectors/SFTP.md) | HOST, PORT, USER, PASSWORD, KEYFILE, PASSPHRASE |
| `FTP` | Transfer | [FTP.md](../src/ETL-SQL.Core/Resources/Help/Connectors/FTP.md) | HOST, PORT, USER, PASSWORD, USE_SSL |
| `AZURE_BLOB` | Transfer | [AZURE_BLOB.md](../src/ETL-SQL.Core/Resources/Help/Connectors/AZURE_BLOB.md) | ACCOUNT_NAME, ACCOUNT_KEY, CONTAINER |
| `API` / `REST` | Service | [API.md](../src/ETL-SQL.Core/Resources/Help/Connectors/API.md) | URL, METHOD, AUTH_TYPE, TOKEN, BODY, ROOT_PATH, ... |
| `SMTP` | Service | [SMTP.md](../src/ETL-SQL.Core/Resources/Help/Connectors/SMTP.md) | HOST, PORT, USER, PASSWORD, USE_SSL, DEFAULT_FROM |
| `DIRECTORY` | Service | [DIRECTORY.md](../src/ETL-SQL.Core/Resources/Help/Connectors/DIRECTORY.md) | PATH, RECURSIVE, ... |
| `MOCKDB` | Testing | [MOCKDB.md](../src/ETL-SQL.Core/Resources/Help/Connectors/MOCKDB.md) | - |
| `REPORTPORTAL` | Admin Service | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#81-report-portal-reportportal) | HOST, PORT, USER, PASSWORD |
| `ORCHESTRATOR` | Admin Service | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md#82-orchestrator-orchestrator) | HOST, PORT, API_KEY |

### 2.1 File-Based Table Alias
`FILE` is the default table name used when querying any file-based connection (e.g. `SELECT * FROM src` where `src` is a FLATFILE connection).

### 2.2 Connector Aliases
`CSV` is an accepted alias for `FLATFILE` in `CREATE CONNECTION` statements.

---

## 3. Standard Library (Functions)

Functions used within `SELECT`, `WHERE`, `SET`, and other expressions.

| Function | Category | Help File | Description |
| :--- | :--- | :--- | :--- |
| `UPPER(string)` | String | [UPPER.md](../src/ETL-SQL.Core/Resources/Help/Functions/UPPER.md) | Converts string to uppercase |
| `LOWER(string)` | String | [LOWER.md](../src/ETL-SQL.Core/Resources/Help/Functions/LOWER.md) | Converts string to lowercase |
| `CONCAT(string1, string2, ...)` | String | [CONCAT.md](../src/ETL-SQL.Core/Resources/Help/Functions/CONCAT.md) | Concatenates multiple strings |
| `LEN(string)` / `LENGTH(string)` | String | [LEN.md](../src/ETL-SQL.Core/Resources/Help/Functions/LEN.md) / [LENGTH.md](../src/ETL-SQL.Core/Resources/Help/Functions/LENGTH.md) | Returns string length |
| `SUBSTRING(string, start, length)` | String | [SUBSTRING.md](../src/ETL-SQL.Core/Resources/Help/Functions/SUBSTRING.md) | Returns part of a string |
| `TRIM(string)` | String | [TRIM.md](../src/ETL-SQL.Core/Resources/Help/Functions/TRIM.md) | Removes leading/trailing whitespace |
| `REPLACE(string, find, replacement)` | String | [REPLACE.md](../src/ETL-SQL.Core/Resources/Help/Functions/REPLACE.md) | Replaces occurrences of a substring |
| `CHARINDEX(find, string)` | String | [CHARINDEX.md](../src/ETL-SQL.Core/Resources/Help/Functions/CHARINDEX.md) | Returns index of first occurrence |
| `INITCAP(string)` | String | [INITCAP.md](../Docs/Reference/Standard_Library.md#31-case--whitespace) | Capitalizes first letter of each word |
| `LTRIM(string)` | String | [LTRIM.md](../src/ETL-SQL.Core/Resources/Help/Functions/LTRIM.md) | Removes leading whitespace |
| `RTRIM(string)` | String | [RTRIM.md](../src/ETL-SQL.Core/Resources/Help/Functions/RTRIM.md) | Removes trailing whitespace |
| `REVERSE(string)` | String | [REVERSE.md](../src/ETL-SQL.Core/Resources/Help/Functions/REVERSE.md) | Reverses string characters |
| `LEFT(string, count)` | String | [LEFT.md](../src/ETL-SQL.Core/Resources/Help/Functions/LEFT.md) | Returns leftmost N characters |
| `RIGHT(string, count)` | String | [RIGHT.md](../src/ETL-SQL.Core/Resources/Help/Functions/RIGHT.md) | Returns rightmost N characters |
| `INSTR(string, find)` | String | [INSTR.md](../src/ETL-SQL.Core/Resources/Help/Functions/INSTR.md) | Alias for POSITION |
| `CONCAT_WS(separator, string1, ...)` | String | [CONCAT_WS.md](../src/ETL-SQL.Core/Resources/Help/Functions/CONCAT_WS.md) | Join with separator; skips nulls |
| `SPLIT_PART(string, delimiter, part)` | String | [SPLIT_PART.md](../Docs/Reference/Standard_Library.md#33-concatenation--splitting) | Returns Nth segment after split |
| `SPACE(count)` | String | [SPACE.md](../src/ETL-SQL.Core/Resources/Help/Functions/SPACE.md) | Returns N space characters |
| `TO_STR(value)` | String | [TO_STR.md](../src/ETL-SQL.Core/Resources/Help/Functions/TO_STR.md) | Converts any value to string |
| `PATINDEX(pattern, string)` | String | [PATINDEX.md](../src/ETL-SQL.Core/Resources/Help/Functions/PATINDEX.md) | Position of wildcard pattern |
| `REPLICATE(string, count)` | String | [REPLICATE.md](../src/ETL-SQL.Core/Resources/Help/Functions/REPLICATE.md) | Repeats string N times |
| `QUOTENAME(string, [delimiter])` | String | [QUOTENAME.md](../src/ETL-SQL.Core/Resources/Help/Functions/QUOTENAME.md) | Returns delimited identifier |
| `ASCII(string)` | String | [ASCII.md](../src/ETL-SQL.Core/Resources/Help/Functions/ASCII.md) | Numeric code of first character |
| `UNICODE(string)` | String | [UNICODE.md](../src/ETL-SQL.Core/Resources/Help/Functions/UNICODE.md) | Unicode code of first character |
| `CHAR(code)` | String | [CHAR.md](../src/ETL-SQL.Core/Resources/Help/Functions/CHAR.md) | Character for given code |
| `DATALENGTH(value)` | String | [DATALENGTH.md](../Docs/Reference/Standard_Library.md#35-character-encoding) | Byte count of value |
| `TRANSLATE(string, find_chars, replace_chars)` | String | [TRANSLATE.md](../src/ETL-SQL.Core/Resources/Help/Functions/TRANSLATE.md) | Replaces chars 1-to-1 |
| `STRING_ESCAPE(text, type)` | String | [STRING_ESCAPE.md](../src/ETL-SQL.Core/Resources/Help/Functions/STRING_ESCAPE.md) | Escapes special characters |
| `STRING_SPLIT(string, delimiter)` | String | [STRING_SPLIT.md](../src/ETL-SQL.Core/Resources/Help/Functions/STRING_SPLIT.md) | Table-valued split |
| `CHAR_LENGTH(string)` | String | [CHAR_LENGTH.md](../src/ETL-SQL.Core/Resources/Help/Functions/CHAR_LENGTH.md) | String length (SQL standard alias) |
| `OVERLAY(string, replacement, start, length)` | String | [OVERLAY.md](../src/ETL-SQL.Core/Resources/Help/Functions/OVERLAY.md) | Replaces substring at position |
| `POSITION(find IN string)` | String | [POSITION.md](../src/ETL-SQL.Core/Resources/Help/Functions/POSITION.md) | Position of substring (SQL standard) |
| `SUBSTR(string, start, length)` | String | [SUBSTR.md](../src/ETL-SQL.Core/Resources/Help/Functions/SUBSTR.md) | Alias for SUBSTRING |
| `STUFF(string, start, length, replacement)` | String | [STUFF.md](../src/ETL-SQL.Core/Resources/Help/Functions/STUFF.md) | Deletes part of string and inserts replacement |
| `STR(number, [length], [decimals])` | String | [STR.md](../src/ETL-SQL.Core/Resources/Help/Functions/STR.md) | Formats number as string |
| `GETDATE()` | Date | [GETDATE.md](../src/ETL-SQL.Core/Resources/Help/Functions/GETDATE.md) | Current local datetime |
| `NOW()` | Date | [NOW.md](../src/ETL-SQL.Core/Resources/Help/Functions/NOW.md) | Current UTC datetime |
| `DATEADD(datepart, number, date)` | Date | [DATEADD.md](../src/ETL-SQL.Core/Resources/Help/Functions/DATEADD.md) | Adds units to a date |
| `DATEDIFF(datepart, start_date, end_date)` | Date | [DATEDIFF.md](../src/ETL-SQL.Core/Resources/Help/Functions/DATEDIFF.md) | Difference between dates |
| `DATENAME(datepart, date)` | Date | [DATENAME.md](../src/ETL-SQL.Core/Resources/Help/Functions/DATENAME.md) | Returns name of date part |
| `DATEPART(datepart, date)` | Date | [DATEPART.md](../src/ETL-SQL.Core/Resources/Help/Functions/DATEPART.md) | Returns integer date part |
| `EOMONTH(date)` | Date | [EOMONTH.md](../src/ETL-SQL.Core/Resources/Help/Functions/EOMONTH.md) | Last day of the month |
| `ISDATE(string)` | Date | [ISDATE.md](../src/ETL-SQL.Core/Resources/Help/Functions/ISDATE.md) | 1 if parseable as date |
| `DATETIMEFROMPARTS(year, month, day, hour, minute, second, ms)` | Date | [DATETIMEFROMPARTS.md](../src/ETL-SQL.Core/Resources/Help/Functions/DATETIMEFROMPARTS.md) | Build DATETIME from components |
| `TIMEFROMPARTS(hour, minute, second, fractions, precision)` | Date | [TIMEFROMPARTS.md](../src/ETL-SQL.Core/Resources/Help/Functions/TIMEFROMPARTS.md) | Build TIME from components |
| `TRUNC(date)` | Date | [TRUNC.md](../src/ETL-SQL.Core/Resources/Help/Functions/TRUNC.md) | Truncates time portion |
| `AT TIME ZONE(date, timezone)` | Date | [AT_TIME_ZONE.md](../Docs/Reference/Standard_Library.md#4-date--time-functions) | Converts to specified timezone |
| `CURRENT_DATE()` | Date | [CURRENT_DATE.md](../src/ETL-SQL.Core/Resources/Help/Functions/CURRENT_DATE.md) | Current date (no time) |
| `CURRENT_TIME()` | Date | [CURRENT_TIME.md](../src/ETL-SQL.Core/Resources/Help/Functions/CURRENT_TIME.md) | Current time |
| `CURRENT_TIMESTAMP()` | Date | [CURRENT_TIMESTAMP.md](../src/ETL-SQL.Core/Resources/Help/Functions/CURRENT_TIMESTAMP.md) | Current datetime (UTC) |
| `DATETRUNC(datepart, date)` | Date | [DATETRUNC.md](../src/ETL-SQL.Core/Resources/Help/Functions/DATETRUNC.md) | Truncates date to unit boundary |
| `DAY(date)` | Date | [DAY.md](../src/ETL-SQL.Core/Resources/Help/Functions/DAY.md) | Day-of-month component |
| `MONTH(date)` | Date | [MONTH.md](../src/ETL-SQL.Core/Resources/Help/Functions/MONTH.md) | Month component |
| `YEAR(date)` | Date | [YEAR.md](../src/ETL-SQL.Core/Resources/Help/Functions/YEAR.md) | Year component |
| `HOUR(date)` | Date | [HOUR.md](../src/ETL-SQL.Core/Resources/Help/Functions/HOUR.md) | Hour component |
| `MINUTE(date)` | Date | [MINUTE.md](../src/ETL-SQL.Core/Resources/Help/Functions/MINUTE.md) | Minute component |
| `SECOND(date)` | Date | [SECOND.md](../src/ETL-SQL.Core/Resources/Help/Functions/SECOND.md) | Second component |
| `ABS(number)` | Math | [ABS.md](../src/ETL-SQL.Core/Resources/Help/Functions/ABS.md) | Absolute value |
| `ROUND(number, decimals)` | Math | [ROUND.md](../src/ETL-SQL.Core/Resources/Help/Functions/ROUND.md) | Rounds to N decimal places |
| `FLOOR(number)` | Math | [FLOOR.md](../src/ETL-SQL.Core/Resources/Help/Functions/FLOOR.md) | Largest integer <= number |
| `CEILING(number)` | Math | [CEILING.md](../src/ETL-SQL.Core/Resources/Help/Functions/CEILING.md) | Smallest integer >= number |
| `CEIL(number)` | Math | [CEIL.md](../src/ETL-SQL.Core/Resources/Help/Functions/CEIL.md) | Alias for CEILING |
| `RAND()` | Math | [RAND.md](../src/ETL-SQL.Core/Resources/Help/Functions/RAND.md) | Random number [0, 1) |
| `RANDOM()` | Math | [RANDOM.md](../src/ETL-SQL.Core/Resources/Help/Functions/RANDOM.md) | Alias for RAND() |
| `RANDOM_INT(min, max)` | Math | [RANDOM_INT.md](../src/ETL-SQL.Core/Resources/Help/Functions/RANDOM_INT.md) | Random integer in range |
| `RANDOM_DECIMAL(min, max)` | Math | [RANDOM_DECIMAL.md](../src/ETL-SQL.Core/Resources/Help/Functions/RANDOM_DECIMAL.md) | Random decimal in range |
| `MOD(number, divisor)` / `number % divisor` | Math | [MOD.md](../src/ETL-SQL.Core/Resources/Help/Functions/MOD.md) | Remainder of division |
| `POWER(base, exponent)` | Math | [POWER.md](../src/ETL-SQL.Core/Resources/Help/Functions/POWER.md) | Base raised to exponent |
| `POW(base, exponent)` | Math | [POW.md](../src/ETL-SQL.Core/Resources/Help/Functions/POW.md) | Alias for POWER |
| `SQRT(number)` | Math | [SQRT.md](../src/ETL-SQL.Core/Resources/Help/Functions/SQRT.md) | Square root |
| `EXP(number)` | Math | [EXP.md](../src/ETL-SQL.Core/Resources/Help/Functions/EXP.md) | e raised to the power of number |
| `LOG(number)` / `LN(number)` | Math | [LOG.md](../src/ETL-SQL.Core/Resources/Help/Functions/LOG.md) | Natural logarithm |
| `LOG10(number)` | Math | [LOG10.md](../src/ETL-SQL.Core/Resources/Help/Functions/LOG10.md) | Base-10 logarithm |
| `LEAST(value1, value2, ...)` | Math | [LEAST.md](../src/ETL-SQL.Core/Resources/Help/Functions/LEAST.md) | Smallest of arguments |
| `GREATEST(value1, value2, ...)` | Math | [GREATEST.md](../src/ETL-SQL.Core/Resources/Help/Functions/GREATEST.md) | Largest of arguments |
| `SIN(radians)` | Math | [SIN.md](../src/ETL-SQL.Core/Resources/Help/Functions/SIN.md) | Sine |
| `COS(radians)` | Math | [COS.md](../src/ETL-SQL.Core/Resources/Help/Functions/COS.md) | Cosine |
| `TAN(radians)` | Math | [TAN.md](../src/ETL-SQL.Core/Resources/Help/Functions/TAN.md) | Tangent |
| `ASIN(number)` | Math | [ASIN.md](../src/ETL-SQL.Core/Resources/Help/Functions/ASIN.md) | Arcsine |
| `ACOS(number)` | Math | [ACOS.md](../src/ETL-SQL.Core/Resources/Help/Functions/ACOS.md) | Arccosine |
| `ATAN(number)` | Math | [ATAN.md](../src/ETL-SQL.Core/Resources/Help/Functions/ATAN.md) | Arctangent |
| `ATAN2(y, x)` | Math | [ATAN2.md](../src/ETL-SQL.Core/Resources/Help/Functions/ATAN2.md) | Arctangent of y/x |
| `SIGN(number)` | Math | [SIGN.md](../src/ETL-SQL.Core/Resources/Help/Functions/SIGN.md) | Returns -1, 0, or 1 |
| `DEGREES(radians)` | Math | [DEGREES.md](../src/ETL-SQL.Core/Resources/Help/Functions/DEGREES.md) | Converts radians to degrees |
| `RADIANS(degrees)` | Math | [RADIANS.md](../src/ETL-SQL.Core/Resources/Help/Functions/RADIANS.md) | Converts degrees to radians |
| `PI()` | Math | [PI.md](../src/ETL-SQL.Core/Resources/Help/Functions/PI.md) | Mathematical constant Ï€ |
| `QUOTIENT(number, divisor)` | Math | [QUOTIENT.md](../src/ETL-SQL.Core/Resources/Help/Functions/QUOTIENT.md) | Integer quotient of division |
| `TRUNCATE(number, decimals)` | Math | [TRUNCATE.md](../src/ETL-SQL.Core/Resources/Help/Functions/TRUNCATE.md) | Truncates number to N decimal places |
| `COALESCE(value1, value2, ...)` | Logic | [COALESCE.md](../src/ETL-SQL.Core/Resources/Help/Functions/COALESCE.md) | First non-null value |
| `ISNULL(value, default)` | Logic | [ISNULL.md](../src/ETL-SQL.Core/Resources/Help/Functions/ISNULL.md) | Returns default if value is null |
| `IIF(condition, true_value, false_value)` | Logic | [IIF.md](../src/ETL-SQL.Core/Resources/Help/Functions/IIF.md) | Inline IF |
| `NVL(value, default)` | Logic | [NVL.md](../src/ETL-SQL.Core/Resources/Help/Functions/NVL.md) | Alias for ISNULL |
| `IFNULL(value, default)` | Logic | [IFNULL.md](../src/ETL-SQL.Core/Resources/Help/Functions/IFNULL.md) | Alias for ISNULL |
| `NVL2(value, not_null_result, null_result)` | Logic | [NVL2.md](../src/ETL-SQL.Core/Resources/Help/Functions/NVL2.md) | Oracle-style null conditional |
| `NULLIF(value1, value2)` | Logic | [NULLIF.md](../src/ETL-SQL.Core/Resources/Help/Functions/NULLIF.md) | NULL if value1 = value2 |
| `IS_NULL(value)` | Logic | [IS_NULL.md](../src/ETL-SQL.Core/Resources/Help/Functions/IS_NULL.md) | 1 if value is null |
| `IS_NOT_NULL(value)` | Logic | [IS_NOT_NULL.md](../src/ETL-SQL.Core/Resources/Help/Functions/IS_NOT_NULL.md) | 1 if value is not null |
| `DECODE(value, search1, result1, ..., [default])` | Logic | [DECODE.md](../src/ETL-SQL.Core/Resources/Help/Functions/DECODE.md) | Oracle-style CASE shorthand |
| `CAST(value AS type)` | System | [CAST.md](../src/ETL-SQL.Core/Resources/Help/Functions/CAST.md) | Converts value to type |
| `TRY_CAST(value AS type)` | System | [TRY_CAST.md](../src/ETL-SQL.Core/Resources/Help/Functions/TRY_CAST.md) | Converts value to type, NULL on fail |
| `CONVERT(type, value)` | System | [CONVERT.md](../src/ETL-SQL.Core/Resources/Help/Functions/CONVERT.md) | Converts value to type |
| `TRY_CONVERT(type, value)` | System | [TRY_CONVERT.md](../src/ETL-SQL.Core/Resources/Help/Functions/TRY_CONVERT.md) | CONVERT with NULL on failure |
| `PARSE(string, type)` | System | [PARSE.md](../src/ETL-SQL.Core/Resources/Help/Functions/PARSE.md) | Culture-aware string to type |
| `TRY_PARSE(string, type)` | System | [TRY_PARSE.md](../src/ETL-SQL.Core/Resources/Help/Functions/TRY_PARSE.md) | PARSE with NULL on failure |
| `HASHBYTES(algorithm, string)` | System | [HASHBYTES.md](../src/ETL-SQL.Core/Resources/Help/Functions/HASHBYTES.md) | Returns hash of string |
| `NEWID()` | System | [NEWID.md](../src/ETL-SQL.Core/Resources/Help/Functions/NEWID.md) | Generates a new GUID |
| `NEWSEQUENTIALID()` | System | [NEWSEQUENTIALID.md](../src/ETL-SQL.Core/Resources/Help/Functions/NEWSEQUENTIALID.md) | Time-ordered GUID v7 |
| `FORMAT(value, format_string)` | System | [FORMAT.md](../src/ETL-SQL.Core/Resources/Help/Functions/FORMAT.md) | Formats value using string pattern |
| `CHECKSUM(value1, ...)` | System | [CHECKSUM.md](../src/ETL-SQL.Core/Resources/Help/Functions/CHECKSUM.md) | 64-bit integer hash |
| `BINARY_CHECKSUM(value1, ...)` | System | [BINARY_CHECKSUM.md](../src/ETL-SQL.Core/Resources/Help/Functions/BINARY_CHECKSUM.md) | Binary-compatible hash |
| `ENV(variable_name)` | System | [ENV.md](../src/ETL-SQL.Core/Resources/Help/Functions/ENV.md) | Host environment variable value |
| `GENERATE_SERIES(start, stop, [step])` | System | [GENERATE_SERIES.md](../src/ETL-SQL.Core/Resources/Help/Functions/GENERATE_SERIES.md) | Returns table of numbers/dates |
| `ERROR_MESSAGE()` | System | [ERROR_MESSAGE.md](../src/ETL-SQL.Core/Resources/Help/Functions/ERROR_MESSAGE.md) | Error string in CATCH block |
| `ERROR_NUMBER()` | System | [ERROR_NUMBER.md](../src/ETL-SQL.Core/Resources/Help/Functions/ERROR_NUMBER.md) | Error code in CATCH block |
| `ERROR_SEVERITY()` | System | [ERROR_SEVERITY.md](../src/ETL-SQL.Core/Resources/Help/Functions/ERROR_SEVERITY.md) | Error severity in CATCH block |
| `ERROR_STATE()` | System | [ERROR_STATE.md](../src/ETL-SQL.Core/Resources/Help/Functions/ERROR_STATE.md) | Error state in CATCH block |
| `ERROR_LINE()` | System | [ERROR_LINE.md](../src/ETL-SQL.Core/Resources/Help/Functions/ERROR_LINE.md) | Error line in CATCH block |
| `JSON_VALUE(json, path)` | JSON | [JSON_VALUE.md](../src/ETL-SQL.Core/Resources/Help/Functions/JSON_VALUE.md) | Extracts scalar from JSON |
| `JSON_QUERY(json, path)` | JSON | [JSON_QUERY.md](../src/ETL-SQL.Core/Resources/Help/Functions/JSON_QUERY.md) | Extracts object/array from JSON |
| `JSON_MODIFY(json, path, new_value)` | JSON | [JSON_MODIFY.md](../src/ETL-SQL.Core/Resources/Help/Functions/JSON_MODIFY.md) | Updates JSON string |
| `ISJSON(string)` | JSON | [ISJSON.md](../src/ETL-SQL.Core/Resources/Help/Functions/ISJSON.md) | 1 if valid JSON |
| `JSON_EXISTS(json, path)` | JSON | [JSON_EXISTS.md](../Docs/Reference/Standard_Library.md#11-json-functions) | 1 if path exists |
| `JSON_OBJECT(key, value, ...)` | JSON | [JSON_OBJECT.md](../Docs/Reference/Standard_Library.md#11-json-functions) | Builds JSON object |
| `JSON_ARRAY(value1, ...)` | JSON | [JSON_ARRAY.md](../Docs/Reference/Standard_Library.md#11-json-functions) | Builds JSON array |
| `JSON_TABLE(json, path COLUMNS (...))` | JSON | [JSON_TABLE.md](../Docs/Reference/Standard_Library.md#11-json-functions) | Table projected from JSON rows |
| `OPENJSON(json, [path])` | JSON | [OPENJSON.md](../Docs/Reference/Standard_Library.md#11-json-functions) | SQL Server-style JSON expansion |
| `XMLVALUE(xml, xpath)` | XML | [XMLVALUE.md](../src/ETL-SQL.Core/Resources/Help/Functions/XMLVALUE.md) | Extracts scalar from XML |
| `XMLEXISTS(xml, xpath)` | XML | [XMLEXISTS.md](../src/ETL-SQL.Core/Resources/Help/Functions/XMLEXISTS.md) | 1 if XPath exists |
| `XMLQUERY(xml, xpath)` | XML | [XMLQUERY.md](../src/ETL-SQL.Core/Resources/Help/Functions/XMLQUERY.md) | XML fragment |
| `XMLTABLE(xml, xpath)` | XML | [XMLTABLE.md](../Docs/Reference/Standard_Library.md#12-xml-functions) | Table from XML |
| `XMLELEMENT(name, content)` | XML | [XMLELEMENT.md](../Docs/Reference/Standard_Library.md#12-xml-functions) | Builds XML element |
| `XMLATTRIBUTES(name, value, ...)` | XML | [XMLATTRIBUTES.md](../Docs/Reference/Standard_Library.md#12-xml-functions) | XML attributes |
| `XMLFOREST(value1, ...)` | XML | [XMLFOREST.md](../Docs/Reference/Standard_Library.md#12-xml-functions) | Forest of XML elements |
| `FILE_EXISTS(path)` | File | [FILE_EXISTS.md](../src/ETL-SQL.Core/Resources/Help/Functions/FILE_EXISTS.md) | 1 if file exists, 0 otherwise |
| `DIRECTORY_EXISTS(path)` | File | [DIRECTORY_EXISTS.md](../src/ETL-SQL.Core/Resources/Help/Functions/DIRECTORY_EXISTS.md) | 1 if directory exists, 0 otherwise |
| `FILE_LIST(path, [mask])` | File | [FILE_LIST.md](../src/ETL-SQL.Core/Resources/Help/Functions/FILE_LIST.md) | Returns table of files in path |
| `REMOTE_FILE_LIST(connection, path)` | File | [REMOTE_FILE_LIST.md](../src/ETL-SQL.Core/Resources/Help/Functions/REMOTE_FILE_LIST.md) | Table of files on remote connection |
| `DIRECTORY(path)` | File | [DIRECTORY.md](../src/ETL-SQL.Core/Resources/Help/Connectors/DIRECTORY.md) | Returns directory metadata |
| `SUM(expression)` | Aggregate | [SUM.md](../src/ETL-SQL.Core/Resources/Help/Functions/SUM.md) | Sum of values |
| `COUNT(expression)` | Aggregate | [COUNT.md](../src/ETL-SQL.Core/Resources/Help/Functions/COUNT.md) | Count of non-null values |
| `AVG(expression)` | Aggregate | [AVG.md](../src/ETL-SQL.Core/Resources/Help/Functions/AVG.md) | Average of values |
| `MAX(expression)` | Aggregate | [MAX.md](../src/ETL-SQL.Core/Resources/Help/Functions/MAX.md) | Maximum value |
| `MIN(expression)` | Aggregate | [MIN.md](../src/ETL-SQL.Core/Resources/Help/Functions/MIN.md) | Minimum value |
| `APPROX_COUNT_DISTINCT(expression)` | Aggregate | [Standard_Library.md](../Docs/Reference/Standard_Library.md#5-aggregate-functions) | HyperLogLog approximate distinct count |
| `EVERY(expression)` / `ANY(expression)` / `SOME(expression)` | Aggregate | [Standard_Library.md](../Docs/Reference/Standard_Library.md#5-aggregate-functions) | Standard boolean aggregates |
| `MEDIAN(expression)` | Aggregate | [MEDIAN.md](../src/ETL-SQL.Core/Resources/Help/Functions/MEDIAN.md) | Median (50th percentile) |
| `VAR(expression)` / `VAR_SAMP` | Aggregate | [VAR.md](../src/ETL-SQL.Core/Resources/Help/Functions/VAR.md) | Sample variance |
| `VARP(expression)` / `VAR_POP` | Aggregate | [VARP.md](../src/ETL-SQL.Core/Resources/Help/Functions/VARP.md) | Population variance |
| `STDEV(expression)` / `STDDEV` | Aggregate | [STDEV.md](../src/ETL-SQL.Core/Resources/Help/Functions/STDEV.md) | Sample standard deviation |
| `STDEVP(expression)` | Aggregate | [STDEVP.md](../src/ETL-SQL.Core/Resources/Help/Functions/STDEVP.md) | Population standard deviation |
| `COVAR_SAMP(expr1, expr2)` | Aggregate | [COVAR_SAMP.md](../Docs/Reference/Standard_Library.md#6-statistical-aggregates) | Sample covariance |
| `COVAR_POP(expr1, expr2)` | Aggregate | [COVAR_POP.md](../Docs/Reference/Standard_Library.md#6-statistical-aggregates) | Population covariance |
| `CORR(expr1, expr2)` | Aggregate | [CORR.md](../Docs/Reference/Standard_Library.md#6-statistical-aggregates) | Pearson correlation |
| `LISTAGG(expression, separator)` | Aggregate | [LISTAGG.md](../src/ETL-SQL.Core/Resources/Help/Functions/LISTAGG.md) | Concatenates values with separator |
| `STRING_AGG(expression, separator)` | Aggregate | [STRING_AGG.md](../src/ETL-SQL.Core/Resources/Help/Functions/STRING_AGG.md) | Concatenates strings with separator |
| `ROW_NUMBER()` | Window | [ROW_NUMBER.md](../src/ETL-SQL.Core/Resources/Help/Functions/ROW_NUMBER.md) | Sequential row number |
| `RANK()` | Window | [RANK.md](../src/ETL-SQL.Core/Resources/Help/Functions/RANK.md) | Rank with gaps |
| `DENSE_RANK()` | Window | [DENSE_RANK.md](../src/ETL-SQL.Core/Resources/Help/Functions/DENSE_RANK.md) | Rank without gaps |
| `LAG(expression, [offset], [default])` | Window | [LAG.md](../src/ETL-SQL.Core/Resources/Help/Functions/LAG.md) | Value from N rows before |
| `LEAD(expression, [offset], [default])` | Window | [LEAD.md](../src/ETL-SQL.Core/Resources/Help/Functions/LEAD.md) | Value from N rows after |
| `NTILE(buckets)` | Window | [NTILE.md](../src/ETL-SQL.Core/Resources/Help/Functions/NTILE.md) | Bucket number 1-N |
| `PERCENT_RANK()` | Window | [PERCENT_RANK.md](../Docs/Reference/Standard_Library.md#132-ranking-functions) | Relative rank (0-1) |
| `CUME_DIST()` | Window | [CUME_DIST.md](../Docs/Reference/Standard_Library.md#13-window-functions) | Cumulative distribution |
| `FIRST_VALUE(expression)` | Window | [FIRST_VALUE.md](../src/ETL-SQL.Core/Resources/Help/Functions/FIRST_VALUE.md) | First value in partition |
| `LAST_VALUE(expression)` | Window | [LAST_VALUE.md](../src/ETL-SQL.Core/Resources/Help/Functions/LAST_VALUE.md) | Last value in partition |
| `NTH_VALUE(expression, nth)` | Window | [NTH_VALUE.md](../Docs/Reference/Standard_Library.md#133-analytic-functions) | Nth value in window frame |
| `PERCENTILE_CONT(fraction)` | Window | [PERCENTILE_CONT.md](../src/ETL-SQL.Core/Resources/Help/Functions/PERCENTILE_CONT.md) | Continuous percentile |
| `PERCENTILE_DISC(fraction)` | Window | [PERCENTILE_DISC.md](../src/ETL-SQL.Core/Resources/Help/Functions/PERCENTILE_DISC.md) | Discrete percentile |
| `REGEXP_LIKE(string, pattern)` | Regex | [REGEXP_LIKE.md](../src/ETL-SQL.Core/Resources/Help/Functions/REGEXP_LIKE.md) | 1 if string matches regex |
| `REGEXP_REPLACE(string, pattern, replacement)` | Regex | [REGEXP_REPLACE.md](../src/ETL-SQL.Core/Resources/Help/Functions/REGEXP_REPLACE.md) | Replace matches in string |
| `REGEXP_SUBSTR(string, pattern)` | Regex | [REGEXP_SUBSTR.md](../src/ETL-SQL.Core/Resources/Help/Functions/REGEXP_SUBSTR.md) | Matched substring |
| `REGEXP_INSTR(string, pattern)` | Regex | [REGEXP_INSTR.md](../src/ETL-SQL.Core/Resources/Help/Functions/REGEXP_INSTR.md) | Position of match |
| `REGEXP_COUNT(string, pattern)` | Regex | [REGEXP_COUNT.md](../Docs/Reference/Standard_Library.md#37-regex-pcre) | Count of matches |
| `REGEXP_MATCHES(string, pattern)` | Regex | [REGEXP_MATCHES.md](../Docs/Reference/Standard_Library.md#37-regex-pcre) | Table of all matches |
| `REGEXP_SPLIT(string, pattern)` | Regex | [REGEXP_SPLIT.md](../Docs/Reference/Standard_Library.md#37-regex-pcre) | Table of split segments |
| `ADD_TO_LIST(list, value)` | List | [ADD_TO_LIST.md](../src/ETL-SQL.Core/Resources/Help/Functions/ADD_TO_LIST.md) | Appends value to a LIST |
| `SORT_LIST(list)` | List | [SORT_LIST.md](../src/ETL-SQL.Core/Resources/Help/Functions/SORT_LIST.md) | Returns sorted copy of list |
| `APPEND_TO_LIST(list, value)` | List | [APPEND_TO_LIST.md](../src/ETL-SQL.Core/Resources/Help/Functions/APPEND_TO_LIST.md) | Alias for ADD_TO_LIST |
| `REMOVE_FROM_LIST(list, value)` | List | [REMOVE_FROM_LIST.md](../src/ETL-SQL.Core/Resources/Help/Functions/REMOVE_FROM_LIST.md) | Removes occurrences from list |
| `GET_TAGS(table, [column])` | Lineage | [GET_TAGS.md](../Docs/Reference/Standard_Library.md#15-lineage--metadata-tag-functions) | Returns list of tag names |
| `GET_TAG_VALUE(table, column, tag_name)` | Lineage | [GET_TAG_VALUE.md](../Docs/Reference/Standard_Library.md#15-lineage--metadata-tag-functions) | Returns value of specific tag |
| `NORMALIZE(string, [mode])` | Fuzzy | [NORMALIZE.md](../Docs/Reference/Standard_Library.md#161-normalize--domain-aware-preprocessing) | Domain-aware preprocessing |
| `SIMILARITY(string1, string2, [mode])` | Fuzzy | [SIMILARITY.md](../Docs/Reference/Standard_Library.md#162-similarity--normalized-similarity-score) | Normalized similarity score (0-1) |
| `LEVENSHTEIN(string1, string2)` | Fuzzy | [LEVENSHTEIN.md](../Docs/Reference/Standard_Library.md#163-levenshtein--raw-edit-distance) | Raw edit distance |
| `SOUNDEX(string)` | Fuzzy | [SOUNDEX.md](../src/ETL-SQL.Core/Resources/Help/Functions/SOUNDEX.md) | 4-char phonetic code |
| `METAPHONE(string)` | Fuzzy | [METAPHONE.md](../Docs/Reference/Standard_Library.md#164-phonetic-encoding-functions) | English phonetic code |
| `DMETAPHONE(string)` | Fuzzy | [DMETAPHONE.md](../Docs/Reference/Standard_Library.md#164-phonetic-encoding-functions) | Double Metaphone primary code |
| `DMETAPHONE_ALT(string)` | Fuzzy | [DMETAPHONE_ALT.md](../Docs/Reference/Standard_Library.md#164-phonetic-encoding-functions) | Double Metaphone alternate code |
| `NGRAMS(string, size)` | Fuzzy | [NGRAMS.md](../Docs/Reference/Standard_Library.md#165-ngrams--ngram_tokens--blocking-utilities) | Table of N-character grams |
| `NGRAM_TOKENS(string)` | Fuzzy | [NGRAM_TOKENS.md](../Docs/Reference/Standard_Library.md#165-ngrams--ngram_tokens--blocking-utilities) | Table of 3-grams (blocking) |
| `DIFFERENCE(string1, string2)` | Fuzzy | [DIFFERENCE.md](../src/ETL-SQL.Core/Resources/Help/Functions/DIFFERENCE.md) | SOUNDEX difference score (0-4) |

*Note: Over 190 functions are registered. See [Standard_Library.md](../Docs/Reference/Standard_Library.md) for full signatures and examples.*

---

### 3.1 Keyword Parameter Enumerations

The following parameters accept a **fixed set of keyword values** only. Functions that use these parameters link here rather than repeating the list inline.

#### `datepart` — DATEADD, DATEDIFF, DATENAME, DATEPART, DATETRUNC, EXTRACT

| Keyword | Abbreviations | Description |
| :--- | :--- | :--- |
| `YEAR` | `YY`, `YYYY` | Calendar year |
| `QUARTER` | `QQ`, `Q` | Quarter of year (1–4) |
| `MONTH` | `MM`, `M` | Month of year (1–12) |
| `WEEK` | `WK`, `WW` | ISO week number |
| `DAYOFYEAR` | `DY`, `Y` | Day within the year (1–366) |
| `DAY` | `DD`, `D` | Day of month (1–31) |
| `WEEKDAY` | `DW` | Day of week (1 = Sunday by default) |
| `HOUR` | `HH` | Hour (0–23) |
| `MINUTE` | `MI`, `N` | Minute (0–59) |
| `SECOND` | `SS`, `S` | Second (0–59) |
| `MILLISECOND` | `MS` | Millisecond (0–999) |

> **Note:** `DATETRUNC` supports only: `YEAR`, `QUARTER`, `MONTH`, `WEEK`, `DAY`, `HOUR`, `MINUTE`, `SECOND`.
> `EXTRACT` uses SQL-standard field names: `YEAR`, `MONTH`, `DAY`, `HOUR`, `MINUTE`, `SECOND`, `DOW` (day-of-week), `DOY` (day-of-year).

#### `algorithm` — HASHBYTES

| Value | Description |
| :--- | :--- |
| `'MD5'` | MD5 hash — 128-bit / 16-byte output |
| `'SHA1'` | SHA-1 hash — 160-bit / 20-byte output |
| `'SHA256'` / `'SHA2_256'` | SHA-256 hash — 256-bit / 32-byte output |
| `'SHA512'` / `'SHA2_512'` | SHA-512 hash — 512-bit / 64-byte output |

#### `mode` — NORMALIZE

| Value | What it does |
| :--- | :--- |
| *(omitted)* | Base: lowercase, trim, collapse whitespace, Unicode NFC, strip control characters |
| `'COMPANY'` | Removes legal suffixes (LLC, Inc, Corp…), expands `&` → `and`, strips leading articles |
| `'PERSON'` | Removes titles and generational suffixes (Mr, Mrs, Dr, Jr, Sr, MD, PhD…) |
| `'ADDRESS'` | Expands directional and street-type abbreviations, removes unit designators |
| `'PHONE'` | Strips all non-digit characters; removes leading country code `1` if 11 digits |
| `'EMAIL'` | Lowercase and trim only |

#### `mode` — SIMILARITY

| Value | Best for |
| :--- | :--- |
| `'JAROWINKLER'` *(default)* | Person names, short identifiers, prefix-heavy strings |
| `'LEVENSHTEIN'` | Short strings with typos, product codes |
| `'TRIGRAM'` | General purpose, partial matches, longer strings |
| `'JACCARD'` | Strings where word presence matters more than order |
| `'TOKENSORT'` | Names where first/last may be swapped |

#### `type` — STRING_ESCAPE

| Value | Description |
| :--- | :--- |
| `'json'` | Escapes characters invalid in JSON strings (`"`, `\`, control chars) |

#### `timezone` — AT TIME ZONE

Any Windows timezone ID string. Common values:

| Value | Region |
| :--- | :--- |
| `'UTC'` | Coordinated Universal Time |
| `'Eastern Standard Time'` | US Eastern (UTC-5 / UTC-4 DST) |
| `'Central Standard Time'` | US Central (UTC-6 / UTC-5 DST) |
| `'Mountain Standard Time'` | US Mountain (UTC-7 / UTC-6 DST) |
| `'Pacific Standard Time'` | US Pacific (UTC-8 / UTC-7 DST) |
| `'GMT Standard Time'` | UK / Ireland |
| `'W. Europe Standard Time'` | Central Europe |
| `'Tokyo Standard Time'` | Japan (UTC+9, no DST) |

Full list: any ID returned by `TimeZoneInfo.GetSystemTimeZones()` on the host OS.

---

## 4. Window Functions

Window functions perform calculations across a set of table rows that are somehow related to the current row.

### 4.1 Window Syntax
```sql
FUNCTION_NAME(args) OVER (
  [PARTITION BY col1, col2, ...]
  [ORDER BY colA [ASC|DESC], ...]
  [ROWS|RANGE|GROUPS BETWEEN <bound> AND <bound>]
  [EXCLUDE CURRENT ROW|GROUP|TIES|NO OTHERS]
)
```

**Supported Bounds:**
- `UNBOUNDED PRECEDING`
- `<n> PRECEDING`
- `CURRENT ROW`
- `<n> FOLLOWING`
- `UNBOUNDED FOLLOWING`

**Frame Modes and Exclusions:**
- `ROWS` counts physical rows.
- `RANGE` groups rows by ordering value range.
- `GROUPS` counts peer groups with equal `ORDER BY` values.
- `EXCLUDE CURRENT ROW`, `EXCLUDE GROUP`, `EXCLUDE TIES`, and `EXCLUDE NO OTHERS` remove rows from the resolved frame.

### 4.2 Dedicated Window Functions
| Function | Help File | Description |
| :--- | :--- | :--- |
| `ROW_NUMBER()` | [ROW_NUMBER.md](../src/ETL-SQL.Core/Resources/Help/Functions/ROW_NUMBER.md) | Sequential row number within partition |
| `RANK()` | [RANK.md](../src/ETL-SQL.Core/Resources/Help/Functions/RANK.md) | Rank with gaps for ties |
| `DENSE_RANK()` | [DENSE_RANK.md](../src/ETL-SQL.Core/Resources/Help/Functions/DENSE_RANK.md) | Rank without gaps for ties |
| `PERCENT_RANK()` | [PERCENT_RANK.md](../Docs/Reference/Standard_Library.md#132-ranking-functions) | Relative rank (0 to 1) |
| `CUME_DIST()` | [CUME_DIST.md](../Docs/Reference/Standard_Library.md#13-window-functions) | Cumulative distribution |
| `NTILE(buckets)` | [NTILE.md](../src/ETL-SQL.Core/Resources/Help/Functions/NTILE.md) | Divide rows into N buckets |
| `LAG(expression, [offset], [default])` | [LAG.md](../src/ETL-SQL.Core/Resources/Help/Functions/LAG.md) | Value from N rows before |
| `LEAD(expression, [offset], [default])` | [LEAD.md](../src/ETL-SQL.Core/Resources/Help/Functions/LEAD.md) | Value from N rows after |
| `FIRST_VALUE(expression)` | [FIRST_VALUE.md](../src/ETL-SQL.Core/Resources/Help/Functions/FIRST_VALUE.md) | First value in window frame |
| `LAST_VALUE(expression)` | [LAST_VALUE.md](../src/ETL-SQL.Core/Resources/Help/Functions/LAST_VALUE.md) | Last value in window frame |
| `NTH_VALUE(expression, nth)` | [NTH_VALUE.md](../Docs/Reference/Standard_Library.md#133-analytic-functions) | Nth value in window frame |
| `PERCENTILE_CONT(fraction)` | [PERCENTILE_CONT.md](../src/ETL-SQL.Core/Resources/Help/Functions/PERCENTILE_CONT.md) | Continuous percentile |
| `PERCENTILE_DISC(fraction)` | [PERCENTILE_DISC.md](../src/ETL-SQL.Core/Resources/Help/Functions/PERCENTILE_DISC.md) | Discrete percentile |

## 4. Window Functions

Window functions perform calculations across a set of table rows that are somehow related to the current row.

### 4.1 Window Syntax
```sql
FUNCTION_NAME(args) OVER (
  [PARTITION BY col1, col2, ...]
  [ORDER BY colA [ASC|DESC], ...]
  [ROWS|RANGE|GROUPS BETWEEN <bound> AND <bound>]
  [EXCLUDE CURRENT ROW|GROUP|TIES|NO OTHERS]
)
```

**Supported Bounds:**
- `UNBOUNDED PRECEDING`
- `<n> PRECEDING`
- `CURRENT ROW`
- `<n> FOLLOWING`
- `UNBOUNDED FOLLOWING`

**Frame Modes and Exclusions:**
- `ROWS` counts physical rows.
- `RANGE` groups rows by ordering value range.
- `GROUPS` counts peer groups with equal `ORDER BY` values.
- `EXCLUDE CURRENT ROW`, `EXCLUDE GROUP`, `EXCLUDE TIES`, and `EXCLUDE NO OTHERS` remove rows from the resolved frame.

### 4.2 Dedicated Window Functions
| Function | Help File | Description |
| :--- | :--- | :--- |
| `ROW_NUMBER()` | [ROW_NUMBER.md](../src/ETL-SQL.Core/Resources/Help/Functions/ROW_NUMBER.md) | Sequential row number within partition |
| `RANK()` | [RANK.md](../src/ETL-SQL.Core/Resources/Help/Functions/RANK.md) | Rank with gaps for ties |
| `DENSE_RANK()` | [DENSE_RANK.md](../src/ETL-SQL.Core/Resources/Help/Functions/DENSE_RANK.md) | Rank without gaps for ties |
| `PERCENT_RANK()` | [PERCENT_RANK.md](../Docs/Reference/Standard_Library.md#132-ranking-functions) | Relative rank (0 to 1) |
| `CUME_DIST()` | [CUME_DIST.md](../Docs/Reference/Standard_Library.md#13-window-functions) | Cumulative distribution |
| `NTILE(n)` | [NTILE.md](../src/ETL-SQL.Core/Resources/Help/Functions/NTILE.md) | Divide rows into N buckets |
| `LAG(v, [n], [d])` | [LAG.md](../src/ETL-SQL.Core/Resources/Help/Functions/LAG.md) | Value from N rows before |
| `LEAD(v, [n], [d])` | [LEAD.md](../src/ETL-SQL.Core/Resources/Help/Functions/LEAD.md) | Value from N rows after |
| `FIRST_VALUE(v)` | [FIRST_VALUE.md](../src/ETL-SQL.Core/Resources/Help/Functions/FIRST_VALUE.md) | First value in window frame |
| `LAST_VALUE(v)` | [LAST_VALUE.md](../src/ETL-SQL.Core/Resources/Help/Functions/LAST_VALUE.md) | Last value in window frame |
| `NTH_VALUE(v, n)` | [NTH_VALUE.md](../Docs/Reference/Standard_Library.md#133-analytic-functions) | Nth value in window frame |
| `PERCENTILE_CONT(n)` | [PERCENTILE_CONT.md](../src/ETL-SQL.Core/Resources/Help/Functions/PERCENTILE_CONT.md) | Continuous percentile |
| `PERCENTILE_DISC(n)` | [PERCENTILE_DISC.md](../src/ETL-SQL.Core/Resources/Help/Functions/PERCENTILE_DISC.md) | Discrete percentile |

### 4.3 Aggregate-as-Window Functions
Any standard aggregate function can be used as a window function by appending the `OVER` clause.
| Function | Example |
| :--- | :--- |
| `SUM(v)` | `SUM(Sales) OVER(PARTITION BY Region)` |
| `AVG(v)` | `AVG(Price) OVER(ORDER BY Date ROWS BETWEEN 7 PRECEDING AND CURRENT ROW)` |
| `COUNT(v)` | `COUNT(*) OVER()` |
| `MAX(v)` / `MIN(v)` | `MAX(Total) OVER(PARTITION BY Category)` |
| `STDEV(v)` / `VAR(v)` | `STDEV(Score) OVER(PARTITION BY Class)` |

---

## 5. Variables

### 5.1 System Variables (`@@`)
Read-only counters tracking session state.

| Variable | Description | Help File |
| :--- | :--- | :--- |
| `@@ROWCOUNT` | Rows affected by last statement | [@@ROWCOUNT.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@ROWCOUNT.md) |
| `@@ERROR` | Last error code (0 = success) | [@@ERROR.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@ERROR.md) |
| `@@VERSION` | Engine version string | [@@VERSION.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@VERSION.md) |
| `@@TRANCOUNT` | Transaction nesting level | [@@TRANCOUNT.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@TRANCOUNT.md) |
| `@@FETCH_STATUS` | Last fetch result (0 = success) | [@@FETCH_STATUS.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@FETCH_STATUS.md) |
| `@@LAST_EXEC_MS` | Duration of last statement | [@@LAST_EXEC_MS.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@LAST_EXEC_MS.md) |
| `@@PEAK_MEMORY_MB` | Peak memory usage in MB | [@@PEAK_MEMORY_MB.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@PEAK_MEMORY_MB.md) |
| `@@TOTAL_SPILLED_BYTES` | Cumulative spill disk usage | [@@TOTAL_SPILLED_BYTES.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@TOTAL_SPILLED_BYTES.md) |
| `@@SORT_SPILLS` | Count of external sort spills | [@@SORT_SPILLS.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@SORT_SPILLS.md) |
| `@@SUBQUERY_CACHE_HITS` | Subquery cache hit count | [@@SUBQUERY_CACHE_HITS.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@SUBQUERY_CACHE_HITS.md) |
| `@@SUBQUERY_CACHE_MISSES` | Subquery cache miss count | [@@SUBQUERY_CACHE_MISSES.md](../src/ETL-SQL.Core/Resources/Help/Variables/@@SUBQUERY_CACHE_MISSES.md) |
| `@@RESULTSETS` | Count of result sets from last stmt | [Standard_Library.md](Reference/Standard_Library.md) |
| `@@PARTITIONS_COUNT` | External spill partition count | [Standard_Library.md](Reference/Standard_Library.md) |
| `@@FILE_EXISTS(p)` | File existence check (also available as function `FILE_EXISTS()`) | - |
| `@@DIRECTORY_EXISTS(p)` | Directory existence check (also available as function `DIRECTORY_EXISTS()`) | - |

### 5.2 Specialty Variable Types
Used in `DECLARE` to define behavior.

| Type | Purpose | Documentation |
| :--- | :--- | :--- |
| `PATH` | Filesystem path with security validation | [Grammar.md#L63] |
| `JSON` | Validated JSON string | [Grammar.md#L82] |
| `XML` | Validated XML string | [Grammar.md#L106] |
| `LIST` / `LIST(t)` | Ordered collection | [Grammar.md#L137] |
| `MINMAX(t)` | Pair of values (.MIN, .MAX) | [Grammar.md#L151] |
| `RELDATE` | Relative date expression (e.g. 'D-7') | [RelativeDate_Parameters.md](../Docs/Reference/RelativeDate_Parameters.md) |
| `SENSITIVE` | Masked in output, auto-decrypts `ENC:` | [Grammar.md#L195] |
| `SECRET` | Same as SENSITIVE, purged at session end | [Grammar.md#L213] |
| `MARKDOWN` | Hint for Report Portal rendering | [Grammar.md#L125] |

---

## 6. SET Options (Configuration)

Options configured via `SET <Option> = <Value>` or `SET <Option> ON|OFF`.

| Option | Category | Default | Help File |
| :--- | :--- | :--- | :--- |
| `WHAT_IF` | Execution | OFF | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `PROFILING` | Execution | OFF | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `SHOW_PASSWORD` | Security | OFF | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `LINEAGE` | Data | ON | [LINEAGE.md](../src/ETL-SQL.Core/Resources/Help/Operations/LINEAGE.md) |
| `TELEMETRY` | Metrics | ON | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `BATCHSIZE` | Performance | 10,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `JOIN_SPILL_THRESHOLD` | Performance | 100,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `TEMP_TABLE_SPILL_THRESHOLD` | Performance | 1,000,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_PARALLEL_DEGREE` | Performance | CPU Count | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `WEEK_START_DAY` | Localization | Monday | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `EXTERNAL_HASH_PARTITIONS` | Performance | 32 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `EXTERNAL_SORT_CHUNK_SIZE` | Performance | 50,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `FOREACH_PAGE_SIZE` | Performance | 10,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `INTERACTIVE_MODE` | Session | OFF | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_FILE_OPERATIONS` | Security | 100 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_GENERATE_ROWS` | Performance | 1,000,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_SMTP_EMAILS_PER_SCRIPT` | Security | 100 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_GROUPING_SETS` | Performance | 100 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_IN_MEMORY_BATCHES` | Performance | 100 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_MESSAGES` | Diagnostics | 1,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_RECURSIVE_DEPTH` | Flow | 10,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_SESSION_SIZE` | Performance | 500 MB | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_STRING_RESULT_SIZE` | Performance | 5 MB | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `PERSIST` | Session | ON | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `REGEX_MATCH_TIMEOUT` | Flow | 1,000ms | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `SPILL_COMPRESSION` | Performance | ON | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `SPILL_ENCRYPTION` | Performance | ON | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `SPILL_FORMAT` | Performance | AUTO | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `WINDOW_SPILL_THRESHOLD` | Performance | 100,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_LAST_RESULT_ROWS` | Performance | 1,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `MAX_INTERNAL_OPERATIONS`| Performance | 1,000,000 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `SET_CUBE_LIMIT` | Performance | 10 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `SCRIPT_HASH_POLICY` | Security | VALIDATE | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `CASE_SENSITIVE` | Execution | OFF | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `TEMPLATE_PATH` | Report | NULL | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `ALLOW_FILE_TYPE_ACCESS` | Security | OFF | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `ALLOW_FILE_OPERATIONS` | Security | 100 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `ALLOW_RECURSIVE_LAYERS` | Security | 5 | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |
| `ALLOW_...` (various) | Security | OFF | [Options/INDEX.md](../src/ETL-SQL.Core/Resources/Help/Options/INDEX.md) |

---

## 7. Object Creation Options (WITH Clauses)

Options available when creating or altering engine and report objects.

### 6.1 CREATE CONNECTION
```sql
CREATE CONNECTION name ON <Provider> WITH ( ... )
```
| Option | Description | Documentation |
| :--- | :--- | :--- |
| `HOST` / `SERVER` | Server hostname or IP | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `PORT` | Network port | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `DATABASE` | Database name | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `USER` / `UID` | Username | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `PASSWORD` / `PWD` | Password (can be 'ENC:...') | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `TRUSTED_CONNECTION`| Use Windows Auth (MSSQL only) | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `ENCRYPT` | Enable SSL/TLS encryption | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `PATH` | Root path for file-based connectors | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `DSN` / `DRIVER` | ODBC specific identifiers | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `KEYFILE` | Path to private key (SFTP/PGP) | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `PASSPHRASE` | Keyfile decryption password | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |
| `SSL_MODE` | Postgres SSL behavior | [Data_Connectors.md](../Docs/Reference/Data_Connectors.md) |

### 6.2 CREATE TABLE
```sql
CREATE TABLE name ( col type [OPTIONS], ... ) [WITH ( ... )]
```
| Option | Context | Description |
| :--- | :--- | :--- |
| `IDENTITY` | Column | Auto-incrementing integer |
| `PRIMARY KEY` | Column/Table | Unique identifier constraint |
| `UNIQUE` | Column/Table | Unique value constraint |
| `NOT NULL` / `NULL` | Column | Nullability constraint |
| `CHECK(expr)` | Column/Table | Validation expression |
| `DEFAULT expr` | Column | Default value expression |
| `REFERENCES tbl(col)`| Column/Table | Foreign key constraint |

### 6.3 CREATE JOB
```sql
CREATE JOB name AS ... WITH ( ... )
```
| Option | Default | Description |
| :--- | :--- | :--- |
| `MAX_RETRIES` | 0 | Number of retry attempts on failure |
| `RETRY_DELAY` | '00:01:00' | Delay between retries (hh:mm:ss) |

### 6.4 CREATE SSH_KEY_PAIR / PGP_KEY_PAIR
```sql
CREATE SSH_KEY_PAIR name WITH ( ... )
```
| Option | Default | Description |
| :--- | :--- | :--- |
| `BITS` | 2048 / 4096 | Key strength |
| `ALGORITHM` | 'RSA' | Key algorithm (RSA, ED25519) |
| `PASSPHRASE` | NULL | Key protection password |
| `IDENTITY` | NULL | PGP User ID |
| `COMMENT` | NULL | Metadata comment |

### 6.5 CREATE DATASET
```sql
CREATE DATASET &name [OPTIONS] AS SELECT ...
```
| Option | Syntax | Description |
| :--- | :--- | :--- |
| `REFRESH` | `REFRESH EVERY 'hh:mm:ss'` | Auto-refresh interval |
| `TTL` | `TTL = 'hh:mm:ss'` | Data expiration period |
| `COMPRESS` | `COMPRESS = ON/OFF` | Enable row compression |
| `ENCRYPT` | `ENCRYPT = MACHINE/PASSWORD/KEYFILE` | Data at rest encryption mode |
| `PASSWORD` | `PASSWORD = '...'` | Encryption password |
| `KEYFILE` | `KEYFILE = '...'` | Encryption key path |
| `ACCESS` | `ACCESS = PUBLIC/PRIVATE` | Portal visibility level |

### 6.6 CREATE VISUAL / BUTTON
```sql
CREATE VISUAL name AS <Type> ( ... )
```
| Section | Option | Description |
| :--- | :--- | :--- |
| `SOURCE` | `SOURCE = #dataset / SELECT ...` | Data source definition |
| `TITLE` | `TITLE = '...' / ('MD'...)` | Primary display title |
| `SUBTITLE` | `SUBTITLE = '...' / ('MD'...)` | Secondary display title |
| `VISIBLE` | `VISIBLE = ON/OFF` | Initial visibility state |
| `MAPPINGS` | `MAPPINGS ( Role = Column, ... )` | Data field assignments |
| `OPTIONS` | `OPTIONS ( Key = Value, ... )` | Visual-specific settings (X_AXIS, COLORS, etc.) |
| `ACTIONS` | `ACTIONS ( Trigger = Action, ... )` | Interactive behavior (ON_CLICK, ON_CHANGE) |
| `INTERACTIONS` | `INTERACTIONS ( Key = Value, ... )` | Cross-visual filtering behavior |
| `STYLE` | `STYLE = Name / ( ... )` | CSS/Theme overrides |
| `SERIES` | `SERIES ( Type Column, ... )` | Multi-series type mapping (BAR/LINE) |
| `FORMATTING` | `FORMATTING ( expr THEN color, ... )` | Conditional formatting rules |
| `OVERLAYS` | `OVERLAYS ( Type AS Style, ... )` | Trend lines, goals, and averages |
| `SUMMARY` | `SUMMARY ( Agg(Col), ... )` | Table footer/total summaries |
| `TOOLTIP` | `TOOLTIP = ... / ( ... )` | Custom hover information |
| `MIN` / `MAX` | `MIN = n, MAX = n` | Range limits for controls |
| `DECIMALS` | `DECIMALS = n` | Numeric precision |
| `PLACEHOLDER` | `PLACEHOLDER = '...'` | Empty state text |

Common `OPTIONS` keys for report visuals:

| Key | Applies to | Values | Description |
| :--- | :--- | :--- | :--- |
| `FORMAT` | `CARD`, `TABLE`, data labels | .NET format string such as `'N0'`, `'C2'`, `'P1'` | Numeric display format |
| `AXIS_SORT` | `BAR`, `HBAR`, `LINE`, `AREA`, `COMBO` | `ASC`, `DESC`, `SOURCE`, `VALUE`, `VALUE_DESC` | Controls category-axis order. `ASC` type-sorts datetime, numeric, then text values; `SOURCE` preserves query order; `VALUE` and `VALUE_DESC` sort by the metric value. |
| `ABBREVIATE` | `CARD` | `ON` / `OFF` | Shortens large numbers, such as `1250000` to `1.25M` |
| `GOAL` | `CARD` | Numeric literal | Supplies a literal target when `MAPPINGS(GOAL = column)` is not used |
| `SHOW_GOAL` | `CARD` | `ON` / `OFF` | Shows the target value line |
| `SHOW_PERCENT_OF_GOAL` | `CARD` | `ON` / `OFF` | Shows percent-to-target text |
| `SHOW_PROGRESS` | `CARD` | `ON` / `OFF` | Shows a goal progress indicator |
| `PROGRESS_STYLE` | `CARD` | `BAR` / `RING` | Chooses the progress indicator style |
| `CLOSE_PCT` / `MET_PCT` | `CARD` | Decimal ratio from `0` to `1` | Sets the close/met status thresholds |
| `COLOR_MET` / `COLOR_CLOSE` / `COLOR_MISSED` | `CARD` | CSS color | Status accent colors |
| `ICON_SET` | `CARD` | `CHECKS`, `ARROWS`, `TRAFFIC` | Preset status badge icon family |
| `ICON_MET` / `ICON_CLOSE` / `ICON_MISSED` | `CARD` | String | Custom status badge icons |
| `LABEL_MET` / `LABEL_CLOSE` / `LABEL_MISSED` | `CARD` | String | Status label overrides |
| `TREND_DIR` | `CARD` | `POSITIVE_UP`, `POSITIVE_DOWN` | Chooses whether an upward or downward delta is favorable |
| `DELTA_FORMAT` | `CARD` | .NET format string | Numeric format for the delta display |
| `DELTA_LABEL` | `CARD` | String | Label shown next to the delta |

### 6.7 CREATE PAGE / CONTAINER
```sql
CREATE PAGE name AS ( ... ) [WITH ( ... )]
```
| Option | Context | Description |
| :--- | :--- | :--- |
| `STRUCTURE` | Page/Container | CSS Grid template area string |
| `MAP` | Page/Container | Mapping of grid slots to visuals/containers |
| `LAYOUT` | Container | Inner layout configuration |
| `GAP` | Page/Layout | Space between grid elements |
| `PINNABLE` | Container layout | Enable/disable portal pinning |
| `ICON` | Container | Header icon identifier |
| `REFRESH` | Page (WITH) | Auto-refresh interval in seconds |

### 6.8 CREATE NAVIGATION
```sql
CREATE NAVIGATION name AS <Type> ( ... )
```
| Option | Default | Description |
| :--- | :--- | :--- |
| `ORIENTATION` | HORIZONTAL | Navigation layout (HORIZONTAL/VERTICAL) |
| `DEFAULT` | NULL | Initial active page |
| `PAGES` | `PAGES ( P1, P2, ... )` | Ordered list of pages in the nav |

---

## 8. Report-SQL (Object Summary)

Specific to `.rptsql` files and the reporting engine.

### 8.1 Report Objects
| Command | Purpose | Help File |
| :--- | :--- | :--- |
| `CREATE VISUAL` | Defines a chart or filter | [VISUAL.md](../src/ETL-SQL.Core/Resources/Help/Report/VISUAL.md) |
| `CREATE DATASET` | Defines a data source for visuals | [DATASET.md](../src/ETL-SQL.Core/Resources/Help/Report/DATASET.md) |
| `CREATE PAGE` | Defines a dashboard page layout | [PAGE.md](../src/ETL-SQL.Core/Resources/Help/Report/PAGE.md) |
| `CREATE CONTAINER` | Groups visuals in a layout | [CONTAINER.md](../src/ETL-SQL.Core/Resources/Help/Report/CONTAINER.md) |
| `CREATE NAVIGATION` | Defines sidebar/top-nav links | [NAVIGATION.md](../src/ETL-SQL.Core/Resources/Help/Report/NAVIGATION.md) |
| `CREATE STYLE` | Defines CSS/Theme overrides | [STYLE.md](../src/ETL-SQL.Core/Resources/Help/Report/STYLE.md) |
| `CREATE BUTTON` | Defines a clickable button | [BUTTON.md](../src/ETL-SQL.Core/Resources/Help/Report/BUTTON.md) |
| `ACTIONS` block | Interactive event bindings | [ACTIONS.md](../src/ETL-SQL.Core/Resources/Help/Report/ACTIONS.md) |
| `INTERACTIONS` block | Cross-visual filtering rules | [INTERACTIONS.md](../src/ETL-SQL.Core/Resources/Help/Report/INTERACTIONS.md) |

### 8.2 Visual Types
| Type | Category | Help File |
| :--- | :--- | :--- |
| `BAR` / `HBAR` | Chart | [BAR.md](../src/ETL-SQL.Core/Resources/Help/Visuals/BAR.md) / [HBAR.md](../src/ETL-SQL.Core/Resources/Help/Visuals/HBAR.md) |
| `LINE` | Chart | [LINE.md](../src/ETL-SQL.Core/Resources/Help/Visuals/LINE.md) |
| `PIE` / `DONUT` | Chart | [PIE.md](../src/ETL-SQL.Core/Resources/Help/Visuals/PIE.md) / [DONUT.md](../src/ETL-SQL.Core/Resources/Help/Visuals/DONUT.md) |
| `GAUGE` | Chart | [GAUGE.md](../src/ETL-SQL.Core/Resources/Help/Visuals/GAUGE.md) |
| `HEATMAP` | Chart | [HEATMAP.md](../src/ETL-SQL.Core/Resources/Help/Visuals/HEATMAP.md) |
| `SCATTER` | Chart | [SCATTER.md](../src/ETL-SQL.Core/Resources/Help/Visuals/SCATTER.md) |
| `GANTT` | Chart | [GANTT.md](../src/ETL-SQL.Core/Resources/Help/Visuals/GANTT.md) |
| `WATERFALL` | Chart | [WATERFALL.md](../src/ETL-SQL.Core/Resources/Help/Visuals/WATERFALL.md) |
| `FUNNEL` | Chart | [FUNNEL.md](../src/ETL-SQL.Core/Resources/Help/Visuals/FUNNEL.md) |
| `BOXPLOT` | Chart | [BOXPLOT.md](../src/ETL-SQL.Core/Resources/Help/Visuals/BOXPLOT.md) |
| `BUBBLE` | Chart | [BUBBLE.md](../src/ETL-SQL.Core/Resources/Help/Visuals/BUBBLE.md) |
| `CANDLESTICK` | Chart | [CANDLESTICK.md](../src/ETL-SQL.Core/Resources/Help/Visuals/CANDLESTICK.md) |
| `COMBO` | Chart | [COMBO.md](../src/ETL-SQL.Core/Resources/Help/Visuals/COMBO.md) |
| `TREEMAP` | Chart | [TREEMAP.md](../src/ETL-SQL.Core/Resources/Help/Visuals/TREEMAP.md) |
| `RADAR` | Chart | [RADAR.md](../src/ETL-SQL.Core/Resources/Help/Visuals/RADAR.md) |
| `SANKEY` | Chart | [SANKEY.md](../src/ETL-SQL.Core/Resources/Help/Visuals/SANKEY.md) |
| `SUNBURST` | Chart | [SUNBURST.md](../src/ETL-SQL.Core/Resources/Help/Visuals/SUNBURST.md) |
| `NETWORK` | Chart | [NETWORK.md](../src/ETL-SQL.Core/Resources/Help/Visuals/NETWORK.md) |
| `TRELLIS` | Chart | [TRELLIS.md](../src/ETL-SQL.Core/Resources/Help/Visuals/TRELLIS.md) |
| `MATRIX` | Data | [MATRIX.md](../src/ETL-SQL.Core/Resources/Help/Visuals/MATRIX.md) |
| `TABLE` | Data | [TABLE.md](../src/ETL-SQL.Core/Resources/Help/Visuals/TABLE.md) |
| `CARD` | KPI with value, label, goal/progress, and delta support | [CARD.md](../src/ETL-SQL.Core/Resources/Help/Visuals/CARD.md) |
| `MAP` | Chart | [MAP.md](../src/ETL-SQL.Core/Resources/Help/Visuals/MAP.md) |
| `TEXT` | Static | [TEXT.md](../src/ETL-SQL.Core/Resources/Help/Visuals/TEXT.md) |
| `IMAGE` | Static | [IMAGE.md](../src/ETL-SQL.Core/Resources/Help/Visuals/IMAGE.md) |
| `SLICER` | Filter | [SLICER.md](../src/ETL-SQL.Core/Resources/Help/Visuals/SLICER.md) |
| `DATEPICKER` | Filter | [DATEPICKER.md](../src/ETL-SQL.Core/Resources/Help/Visuals/DATEPICKER.md) |
| `RELDATEPICKER` | Filter | [RELDATEPICKER.md](../src/ETL-SQL.Core/Resources/Help/Visuals/RELDATEPICKER.md) |
| `SEARCH` | Filter | [SEARCH.md](../src/ETL-SQL.Core/Resources/Help/Visuals/SEARCH.md) |
| `SLIDER` | Filter | [SLIDER.md](../src/ETL-SQL.Core/Resources/Help/Visuals/SLIDER.md) |
| `MULTISELECT` | Filter | [MULTISELECT.md](../src/ETL-SQL.Core/Resources/Help/Visuals/MULTISELECT.md) |
| `CHECKBOX` | Control | [CHECKBOX.md](../src/ETL-SQL.Core/Resources/Help/Visuals/CHECKBOX.md) |
| `TEXTBOX` | Control | [TEXTBOX.md](../src/ETL-SQL.Core/Resources/Help/Visuals/TEXTBOX.md) |
| `NUMBERBOX` | Control | [NUMBERBOX.md](../src/ETL-SQL.Core/Resources/Help/Visuals/NUMBERBOX.md) |

---

## 9. Portal & Orchestrator Admin

Commands executed via `EXECUTE portal BEGIN ... END` or `EXECUTE orch BEGIN ... END`.

| Command | Context | Purpose |
| :--- | :--- | :--- |
| `CREATE USER` | Portal | Adds a portal user |
| `ALTER USER` | Portal | Modifies user properties or status |
| `DROP USER` | Portal | Deletes a user |
| `CREATE GROUP` | Portal | Adds a security group |
| `DROP GROUP` | Portal | Deletes a security group |
| `ADD USER ... TO GROUP` | Portal | Manages group membership |
| `CREATE FOLDER` | Portal | Adds a navigation folder |
| `DROP FOLDER` | Portal | Deletes a navigation folder |
| `GRANT` | Portal | Assigns folder or dataset permissions |
| `REVOKE` | Portal | Removes folder or dataset permissions |
| `PUBLISH REPORT` | Portal | Deploys a report script |
| `ALTER REPORT` | Portal | Modifies report metadata |
| `DROP REPORT` | Portal | Deletes a report |
| `CREATE REFRESH JOB`| Portal | Schedules automated snapshot refresh |
| `REFRESH REPORT` | Portal | Manually starts a report refresh cycle |
| `REFRESH DATASET` | Portal | Marks a portal dataset stale and queues refresh when possible |
| `ALTER DATASET` | Portal | Updates portal dataset access/TTL metadata |
| `DROP DATASET` | Portal | Removes a portal dataset registry entry |
| `DROP REFRESH JOB` | Portal | Removes a refresh schedule |
| `REBUILD SNAPSHOT` | Portal | Forces a data refresh |
| `DROP SNAPSHOT` | Portal | Deletes existing snapshot data |
| `CREATE SUBSCRIPTION`| Portal | Schedules email/PDF report delivery |
| `ALTER SUBSCRIPTION` | Portal | Modifies subscription settings |
| `DROP SUBSCRIPTION` | Portal | Deletes a subscription |
| `DISCONNECT USER` | Portal | Force-closes an active session |
| `REVOKE TOKENS` | Portal | Invalidates all user authentication tokens |
| `RESTART PORTAL` | Portal | Restarts the portal web service |
| `SHUTDOWN PORTAL` | Portal | Stops the portal web service |
| `CREATE JOB` | Orch | Schedules a recurring script task |
| `KILL JOB` | Orch | Stops a running background task |
| `SHOW USERS` | Portal | Lists all registered users |
| `SHOW REPORTS` | Portal | Lists reports in a folder |
| `SHOW ACTIVE SESSIONS`| Portal| Lists current web sessions |
| `SHOW JOBS` | Orch | Lists scheduled background tasks |
| `SHOW TABLES` | Diagnostics | Lists tables in a connection |
| `SHOW COLUMNS` | Diagnostics | Lists columns in a table |
| `SHOW TAGS` | Lineage | Lists tags on a table/column |
| `SHOW CONNECTION <conn> CONFIG` | Diagnostics | Lists configuration options for a specific connection |
| `SHOW CONNECTIONS` | Diagnostics | Lists all active connections |
| `SHOW SESSIONS` | Portal | Lists active web/CLI sessions |
| `SHOW ZONES` | Diagnostics | Lists security zones and policies |

---

## 10. Visual Action Commands

Used inside `ACTIONS ( ... )` blocks for interactive reports.

| Action | Syntax | Description |
| :--- | :--- | :--- |
| `DRILL_DOWN` | `DRILL_DOWN ( Target = Visual, Key = Col )` | Filter target visual by selected key |
| `DRILL_IN` | `DRILL_IN ( HIERARCHY = ( Col1, ... ) )` | Step down through a column hierarchy |
| `SET_PARAMETER` | `SET_PARAMETER (@Name, Column/Expr)` | Updates a report parameter |
| `RUN_SCRIPT` | `RUN_SCRIPT ('Path', @P = Col, ...)` | Executes an ETL-SQL script on event |
| `DRILL_REPORT` | `DRILL_REPORT ( FILE = 'Path', ... )` | Opens another report with parameters |
| `CLEAR_FILTERS` | `CLEAR_FILTERS` | Resets all active filters on the page |
| `APPLY_PARAMETERS`| `APPLY_PARAMETERS` | Forces a data refresh with current params |
| `NAVIGATE_PAGE` | `NAVIGATE_PAGE ( 'PageName' )` | Switch to a specific report page |
| `REFRESH_VISUALS` | `REFRESH_VISUALS ( V1, V2, ... )` | Force data refresh for specific visuals |
| `SET_UI_STATE` | `SET_UI_STATE ( Target, Key, Value )` | Dynamically change UI props (VISIBLE, etc.) |
| `BACK` | `BACK` | Return to previous page/report |
| `REFRESH_REPORT` | `REFRESH_REPORT` | Reload the entire report |
| `EXPORT_CSV` | `EXPORT_CSV` | Download current data as CSV |
| `EXPORT_EXCEL` | `EXPORT_EXCEL` | Download current data as Excel |
| `EXPORT_PDF` | `EXPORT_PDF` | Download current page as PDF |

---

## 11. Operators & Symbols

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
| `+`, `-`, `*`, `/`, `%` | Arithmetic | Standard math operators |
| `=`, `<>`, `!=`, `<`, `<=`, `>`, `>=` | Comparison | Equality and range operators |
| `AND`, `OR`, `NOT` | Logical | Boolean logic operators |
| `IS NULL`, `IS NOT NULL` | Nullity | Testing for null values |
| `LIKE`, `IN`, `BETWEEN`, `EXISTS` | Membership | SQL-style predicate operators |
| `(` ... `)` | Grouping | Expression and function call grouping |
| `,` | Separator | Argument and list separator |
| `;` | Terminator | Optional statement terminator |
| `--`, `/* ... */` | Comments | Single and multi-line comments |

---

## 12. Data Types

Supported types for `DECLARE`, `CREATE TABLE`, and `CAST`.

| Type Group | Specific Types | Description |
| :--- | :--- | :--- |
| **Integer** | `INT`, `INTEGER`, `BIGINT`, `SMALLINT`, `TINYINT` | 1 to 8 byte signed integers |
| **Numeric** | `DECIMAL(p,s)`, `NUMERIC`, `FLOAT`, `DOUBLE`, `REAL` | Exact and approximate decimals |
| **Monetary** | `MONEY`, `SMALLMONEY` | Currency types |
| **Boolean** | `BIT`, `BOOLEAN`, `BOOL` | True/False or 0/1 values |
| **Character** | `VARCHAR`, `NVARCHAR`, `TEXT`, `NCHAR`, `STRING` | Variable and fixed length strings |
| **Date/Time** | `DATE`, `DATETIME`, `TIMESTAMP`, `DATETIMEOFFSET` | Calendar and clock types |
| **Binary** | `BINARY`, `VARBINARY`, `IMAGE` | Raw byte buffers |
| **Identity** | `UNIQUEIDENTIFIER`, `UUID`, `GUID` | 128-bit unique identifiers |
| **Specialty** | `MINMAX`, `SENSITIVE`, `SECRET`, `RELDATE` | ETL-SQL specific types |
| **Spatial** | `GEOMETRY`, `GEOGRAPHY` | GIS coordinate types |
| **System** | `VARIANT`, `HIERARCHYID`, `ANY` | Dynamic and hierarchical types |

---

## 13. Join Syntax

Used in the `FROM` clause to combine rows from multiple sources.

| Keyword | Category | Usage |
| :--- | :--- | :--- |
| `INNER JOIN` | Type | Default join; returns rows with matching values |
| `LEFT JOIN` | Type | Returns all rows from left, matching from right |
| `RIGHT JOIN` | Type | Returns all rows from right, matching from left |
| `FULL JOIN` | Type | Returns all rows when there is a match in either |
| `CROSS JOIN` | Type | Cartesian product of both tables |
| `CROSS APPLY` | Type | Joins table to a table-valued function/subquery |
| `OUTER APPLY` | Type | Left outer version of CROSS APPLY |
| `HASH JOIN` | Hint | Forces hash-based join algorithm |
| `LOOP JOIN` | Hint | Forces nested-loop join algorithm |
| `FUZZY JOIN` | Hint | Enables similarity-based matching |
| `SEMI` / `ANTI` | Type | Used for existence/non-existence filtering |

---

## 14. Set Operations

Combine result sets from multiple `SELECT` statements.

| Operation | Description |
| :--- | :--- |
| `UNION` | Returns distinct rows from both sets |
| `UNION ALL` | Returns all rows from both sets (including duplicates) |
| `EXCEPT` | Returns rows from first set not present in second |
| `INTERSECT` | Returns only rows present in both sets |

---

## 15. Query Clauses & Modifiers

Standard clauses available within a `SELECT` statement.

| Clause | Description | Documentation |
| :--- | :--- | :--- |
| `DISTINCT` | Returns only unique rows | [Grammar.md](../Docs/Reference/Grammar.md) |
| `TOP (n)` | Limits results (MSSQL style) | [Grammar.md](../Docs/Reference/Grammar.md) |
| `LIMIT n` | Limits results (Postgres style) | [Grammar.md](../Docs/Reference/Grammar.md) |
| `OFFSET n` | Skips first N rows | [Grammar.md](../Docs/Reference/Grammar.md) |
| `FETCH FIRST/NEXT n ROWS ONLY` | SQL:2008 result limiting | [Grammar.md](../Docs/Reference/Grammar.md#53-top--limit--offset-fetch) |
| `VALUES (...) AS alias(...)` | Standalone table constructor in `FROM`/`JOIN` | [Grammar.md](../Docs/Reference/Grammar.md#54-values-table-constructor) |
| `GROUP BY` | Aggregates rows by column values | [Grammar.md](../Docs/Reference/Grammar.md) |
| `HAVING` | Filters aggregated groups | [Grammar.md](../Docs/Reference/Grammar.md) |
| `ORDER BY` | Sorts the final result set | [Grammar.md](../Docs/Reference/Grammar.md) |
| `ASC` / `DESC` | Sorting direction | [Grammar.md](../Docs/Reference/Grammar.md) |
| `ROLLUP` | Grouping set extension for hierarchies | [Grammar.md](../Docs/Reference/Grammar.md) |
| `CUBE` | Grouping set extension for all permutations| [Grammar.md](../Docs/Reference/Grammar.md) |
| `GROUPING SETS` | Explicit grouping set list | [Grammar.md](../Docs/Reference/Grammar.md) |
| `QUALIFY` | Filters results of window functions | [Grammar.md](../Docs/Reference/Grammar.md) |
| `FILTER (WHERE ...)` | Per-aggregate conditional filter | [Grammar.md](../Docs/Reference/Grammar.md) |
| `ILIKE` | Case-insensitive pattern match | [Grammar.md](../Docs/Reference/Grammar.md) |
| `~` / `~*` | Regex match / case-insensitive regex match | [Grammar.md](../Docs/Reference/Grammar.md) |
| `OUTPUT` | Returns modified rows (DML only) | [Grammar.md](../Docs/Reference/Grammar.md) |
| `FOR JSON` | Formats output as JSON (PATH/AUTO/RAW) | [Grammar.md](../Docs/Reference/Grammar.md) |
| `FOR XML` | Formats output as XML (PATH/AUTO/RAW) | [Grammar.md](../Docs/Reference/Grammar.md) |
| `CASE` | Start of conditional expression | [Grammar.md](../Docs/Reference/Grammar.md) |
| `WHEN / THEN` | Conditional branch | [Grammar.md](../Docs/Reference/Grammar.md) |
| `ELSE / END` | Fallback and termination of CASE | [Grammar.md](../Docs/Reference/Grammar.md) |

---

## 16. Table Operators

Operators that transform the shape of a table in the `FROM` clause.

| Operator | Syntax | Description |
| :--- | :--- | :--- |
| `PIVOT` | `PIVOT ( agg(col) FOR pivot_col IN (...) )` | Rotates rows into columns |
| `UNPIVOT` | `UNPIVOT ( val_col FOR name_col IN (...) )` | Rotates columns into rows |
| `MATCH_RECOGNIZE` | `MATCH_RECOGNIZE (PARTITION BY ... ORDER BY ... MEASURES ... PATTERN (...) DEFINE ...)` | Finds row patterns in ordered sequences |

---

## 17. Metadata & Script Tags

Annotations used for lineage, security, and script behavior.

| Tag | Level | Usage |
| :--- | :--- | :--- |
| `/*@tag: val */` | Row / Column | Lineage and metadata tagging |
| `@tag: val;` | Script Header | Script-level metadata (e.g. `@author: dev`) |
| `ENC:...` | Literal | Prefix for engine-encrypted strings |
| `BANG` / `!` | Session | Prefix for named Environment Sets (e.g. `!PROD`) |





