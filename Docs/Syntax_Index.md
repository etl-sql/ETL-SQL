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
| `BEGIN TRANSACTION` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [TRANSACTION.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/TRANSACTION.md) |
| `COMMIT` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [TRANSACTION.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/TRANSACTION.md) |
| `ROLLBACK` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [TRANSACTION.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/TRANSACTION.md) |
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
| `SHOW LOCAL VARIABLES`| Diagnostics| [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L375) | [SHOW.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `SHOW CONNECTION` | Diagnostics| [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L674) | [SHOW.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `SHOW CONNECTIONS` | Diagnostics| [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L674) | [SHOW.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SHOW.md) |
| `CLEAR SESSION` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L335) | [CLEAR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CLEAR.md) |
| `USE PASSWORD` | Session / Security | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L327) | [USE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/USE.md) |
| `USE SETS` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L345) | [USE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/USE.md) |
| `CREATE SETS` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L345) | [CREATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `DROP SETS` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L345) | [DROP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DROP.md) |
| `REQUIRE VERSION` | Session | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md#L366) | [REQUIRE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/REQUIRE.md) |
| `BULK INSERT` | File IO | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [BULK.INSERT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/BULK.INSERT.md) |
| `COPY FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [COPY.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/COPY.md) |
| `MOVE FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [MOVE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/MOVE.md) |
| `DELETE FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DELETE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DELETE.md) |
| `ENCRYPT FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [ENCRYPT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/ENCRYPT.md) |
| `SEND FILE` | File IO / Conn | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [SEND/FILE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Operations/SEND/FILE.md) |
| `RECEIVE FILE` | File IO / Conn | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [RECEIVE/FILE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Operations/RECEIVE/FILE.md) |
| `SEND EMAIL` | Notifications | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [SEND/EMAIL.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Operations/SEND/EMAIL.md) |
| `DOCKER` | Containers | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DOCKER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Operations/DOCKER.md) |
| `CREATE JOB` | Orchestration | [Grammar.md] | [SCHEDULE.md] |
| `KILL JOB` | Orchestration | [Grammar.md] | - |
| `CREATE INDEX` | DDL | [Grammar.md] | [CREATE.md] |
| `CREATE PROCEDURE` | DDL | [Grammar.md] | [CREATE.md] |
| `CREATE FUNCTION` | DDL | [Grammar.md] | [CREATE.md] |
| `GENERATE` | DML | [Grammar.md] | [GENERATE.md] |
| `WITH` | CTE | [Grammar.md] | - |
| `WITH RECURSIVE` | CTE | [Grammar.md] | - |
| `PIVOT` / `UNPIVOT` | DML / Transform | [Grammar.md] | - |
| `EXPORT REPORT` | Orchestration | [Grammar.md] | - |
| `RAISEERROR` | Flow Control | [Grammar.md] | - |
| `HELP` | Diagnostics | [Grammar.md] | - |
| `EXPLAIN` | Diagnostics | [Grammar.md] | - |
| `ANALYZE` | Diagnostics | [Grammar.md] | - |
| `RENAME FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [RENAME.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/RENAME.md) |
| `COMPRESS FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [COMPRESS.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/COMPRESS.md) |
| `DECOMPRESS FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DECOMPRESS.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DECOMPRESS.md) |
| `DECRYPT FILE` | File IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DECRYPT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DECRYPT.md) |
| `CREATE DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [CREATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `COPY DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [COPY.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/COPY.md) |
| `MOVE DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [MOVE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/MOVE.md) |
| `RENAME DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [RENAME.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/RENAME.md) |
| `DELETE DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DELETE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DELETE.md) |
| `DELETE DIRECTORY_CONTENTS`| Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | - |
| `COMPRESS DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [COMPRESS.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/COMPRESS.md) |
| `DECOMPRESS DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DECOMPRESS.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DECOMPRESS.md) |
| `ENCRYPT DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [ENCRYPT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/ENCRYPT.md) |
| `DECRYPT DIRECTORY` | Dir IO | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DECRYPT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DECRYPT.md) |
| `CREATE SSH_KEY_PAIR` | Security | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [SSH_KEY_PAIR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SSH_KEY_PAIR.md) |
| `CREATE PGP_KEY_PAIR` | Security | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [PGP_KEY_PAIR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/PGP_KEY_PAIR.md) |
| `START DOCKER` | Containers | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DOCKER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DOCKER.md) |
| `STOP DOCKER` | Containers | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DOCKER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DOCKER.md) |
| `PAUSE DOCKER` | Containers | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DOCKER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DOCKER.md) |
| `CLOSE DOCKER` | Containers | [Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md) | [DOCKER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/DOCKER.md) |

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

### 3.1 File based table name
FILE - Default name of the "table" for any file based connections

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
| `INITCAP(s)` | String | [INITCAP.md] | Capitalizes first letter of each word |
| `LTRIM(s)` | String | [LTRIM.md] | Removes leading whitespace |
| `RTRIM(s)` | String | [RTRIM.md] | Removes trailing whitespace |
| `REVERSE(s)` | String | [REVERSE.md] | Reverses string characters |
| `LEFT(s, n)` | String | [LEFT.md] | Returns leftmost N characters |
| `RIGHT(s, n)` | String | [RIGHT.md] | Returns rightmost N characters |
| `INSTR(s, f)` | String | [INSTR.md] | Alias for POSITION |
| `CONCAT_WS(sep, ...)`| String | [CONCAT_WS.md] | Join with separator; skips nulls |
| `SPLIT_PART(s, d, n)`| String | [SPLIT_PART.md] | Returns Nth segment after split |
| `SPACE(n)` | String | [SPACE.md] | Returns N space characters |
| `TO_STR(v)` | String | [TO_STR.md] | Converts any value to string |
| `PATINDEX(p, s)` | String | [PATINDEX.md] | Position of wildcard pattern |
| `REPLICATE(s, n)` | String | [REPLICATE.md] | Repeats string N times |
| `QUOTENAME(s, [c])` | String | [QUOTENAME.md] | Returns delimited identifier |
| `ASCII(s)` | String | [ASCII.md] | Numeric code of first character |
| `UNICODE(s)` | String | [UNICODE.md] | Unicode code of first character |
| `CHAR(n)` | String | [CHAR.md] | Character for given code |
| `DATALENGTH(v)` | String | [DATALENGTH.md] | Byte count of value |
| `TRANSLATE(s, f, t)`| String | [TRANSLATE.md] | Replaces chars in f with chars in t |
| `STRING_ESCAPE(t, y)`| String | [STRING_ESCAPE.md] | Escapes special characters |
| `STRING_SPLIT(s, d)` | String | [STRING_SPLIT.md] | Table-valued split |
| `GETDATE()` | Date | [GETDATE.md] | Current local datetime |
| `NOW()` | Date | [NOW.md] | Current UTC datetime |
| `DATEADD(u, n, d)` | Date | [DATEADD.md] | Adds units to a date |
| `DATEDIFF(u, d1, d2)` | Date | [DATEDIFF.md] | Difference between dates |
| `DATENAME(u, d)` | Date | [DATENAME.md] | Returns name of date part |
| `DATEPART(u, d)` | Date | [DATEPART.md] | Returns integer date part |
| `EOMONTH(d)` | Date | [EOMONTH.md] | Last day of the month |
| `ISDATE(s)` | Date | [ISDATE.md] | 1 if parseable as date |
| `DATETIMEFROMPARTS` | Date | [DATETIMEFROMPARTS.md] | Build DATETIME from components |
| `TIMEFROMPARTS` | Date | [TIMEFROMPARTS.md] | Build TIME from components |
| `TRUNC(d)` | Date | [TRUNC.md] | Truncates time portion |
| `AT TIME ZONE` | Date | [AT_TIME_ZONE.md] | Converts to specified timezone |
| `ABS(n)` | Math | [ABS.md] | Absolute value |
| `ROUND(n, d)` | Math | [ROUND.md] | Rounds to d decimals |
| `FLOOR(n)` | Math | [FLOOR.md] | Largest integer <= n |
| `CEILING(n)` | Math | [CEILING.md] | Smallest integer >= n |
| `RAND()` | Math | [RAND.md] | Random number [0, 1) |
| `MOD(n, d)` / `n % d` | Math | [MOD.md] | Remainder of division |
| `POWER(b, e)` | Math | [POWER.md] | Base raised to exponent |
| `SQRT(n)` | Math | [SQRT.md] | Square root |
| `EXP(n)` | Math | [EXP.md] | e^n |
| `LOG(n)` / `LN(n)` | Math | [LOG.md] | Natural logarithm |
| `LOG10(n)` | Math | [LOG10.md] | Base-10 logarithm |
| `LEAST(v1, v2, ...)` | Math | [LEAST.md] | Smallest of arguments |
| `GREATEST(v1, v2, ...)`| Math | [GREATEST.md] | Largest of arguments |
| `COALESCE(v1, v2, ...)`| Logic | [COALESCE.md] | First non-null value |
| `ISNULL(v, d)` | Logic | [ISNULL.md] | Returns d if v is null |
| `IIF(c, t, f)` | Logic | [IIF.md] | Inline IF |
| `NVL(v, d)` | Logic | [NVL.md] | Alias for ISNULL |
| `IFNULL(v, d)` | Logic | [IFNULL.md] | Alias for ISNULL |
| `NVL2(v, n, f)` | Logic | [NVL2.md] | Oracle-style null conditional |
| `NULLIF(v1, v2)` | Logic | [NULLIF.md] | NULL if v1 = v2 |
| `IS_NULL(v)` | Logic | [IS_NULL.md] | 1 if v is null |
| `IS_NOT_NULL(v)` | Logic | [IS_NOT_NULL.md] | 1 if v is not null |
| `DECODE(v, ...)` | Logic | [DECODE.md] | Oracle-style CASE shorthand |
| `CAST(v AS t)` | System | [CAST.md] | Converts v to type t |
| `TRY_CAST(v AS t)` | System | [TRY_CAST.md] | Converts v to type t, NULL on fail |
| `HASHBYTES(a, s)` | System | [HASHBYTES.md] | Returns hash of string |
| `NEWID()` | System | [NEWID.md] | Generates a new GUID |
| `ERROR_MESSAGE()` | System | [ERROR_MESSAGE.md] | Error string in CATCH block |
| `ERROR_NUMBER()` | System | [ERROR_NUMBER.md] | Error code in CATCH block |
| `ERROR_SEVERITY()` | System | [ERROR_SEVERITY.md] | Error severity in CATCH block |
| `ERROR_STATE()` | System | [ERROR_STATE.md] | Error state in CATCH block |
| `ERROR_LINE()` | System | [ERROR_LINE.md] | Error line in CATCH block |
| `ENV(v)` | System | [ENV.md] | Host environment variable |
| `CHECKSUM(...)` | System | [CHECKSUM.md] | 64-bit integer hash |
| `BINARY_CHECKSUM(...)`| System | [BINARY_CHECKSUM.md] | Binary-compatible hash |
| `NEWSEQUENTIALID()` | System | [NEWSEQUENTIALID.md] | Time-ordered GUID v7 |
| `JSON_VALUE(j, p)` | JSON | [JSON_VALUE.md] | Extracts scalar from JSON |
| `JSON_QUERY(j, p)` | JSON | [JSON_QUERY.md] | Extracts object/array from JSON |
| `JSON_MODIFY(j, p, v)` | JSON | [JSON_MODIFY.md] | Updates JSON string |
| `ISJSON(s)` | JSON | [ISJSON.md] | 1 if valid JSON |
| `JSON_EXISTS(j, p)` | JSON | [JSON_EXISTS.md] | 1 if path exists |
| `JSON_OBJECT(...)` | JSON | [JSON_OBJECT.md] | Builds JSON object |
| `JSON_ARRAY(...)` | JSON | [JSON_ARRAY.md] | Builds JSON array |
| `JSON_TABLE(j, p)` | JSON | [JSON_TABLE.md] | Table from JSON |
| `OPENJSON(j, [p])` | JSON | [OPENJSON.md] | SQL Server-style expansion |
| `XMLVALUE(x, p)` | XML | [XMLVALUE.md] | Extracts scalar from XML |
| `XMLEXISTS(x, p)` | XML | [XMLEXISTS.md] | 1 if XPath exists |
| `XMLQUERY(x, p)` | XML | [XMLQUERY.md] | XML fragment |
| `XMLTABLE(x, p)` | XML | [XMLTABLE.md] | Table from XML |
| `XMLELEMENT(n, c)` | XML | [XMLELEMENT.md] | Builds XML element |
| `XMLATTRIBUTES(...)` | XML | [XMLATTRIBUTES.md] | XML attributes |
| `XMLFOREST(...)` | XML | [XMLFOREST.md] | Forest of XML elements |
| `FILE_EXISTS(p)` | File | [FILE_EXISTS.md] | 1 if file exists, 0 otherwise |
| `DIRECTORY_EXISTS(p)` | File | [DIRECTORY_EXISTS.md] | 1 if dir exists, 0 otherwise |
| `FILE_LIST(p, m)` | File | [FILE_LIST.md] | Returns table of files in path |
| `REMOTE_FILE_LIST` | File | [REMOTE_FILE_LIST.md] | Table of files on remote conn |
| `SUM(v)` | Aggregate | [SUM.md] | Sum of values |
| `COUNT(v)` | Aggregate | [COUNT.md] | Count of non-null values |
| `AVG(v)` | Aggregate | [AVG.md] | Average of values |
| `MAX(v)` | Aggregate | [MAX.md] | Maximum value |
| `MIN(v)` | Aggregate | [MIN.md] | Minimum value |
| `VAR(v)` / `VAR_SAMP` | Aggregate | [VAR.md] | Sample variance |
| `VARP(v)` / `VAR_POP` | Aggregate | [VARP.md] | Population variance |
| `STDEV(v)` / `STDDEV` | Aggregate | [STDEV.md] | Sample standard deviation |
| `STDEVP(v)` | Aggregate | [STDEVP.md] | Population standard deviation |
| `COVAR_SAMP(x, y)` | Aggregate | [COVAR_SAMP.md] | Sample covariance |
| `COVAR_POP(x, y)` | Aggregate | [COVAR_POP.md] | Population covariance |
| `CORR(x, y)` | Aggregate | [CORR.md] | Pearson correlation |
| `ROW_NUMBER()` | Window | [ROW_NUMBER.md] | Sequential row number |
| `RANK()` | Window | [RANK.md] | Rank with gaps |
| `DENSE_RANK()` | Window | [DENSE_RANK.md] | Rank without gaps |
| `LAG(v, n)` | Window | [LAG.md] | Value from n rows before |
| `LEAD(v, n)` | Window | [LEAD.md] | Value from n rows after |
| `NTILE(n)` | Window | [NTILE.md] | Bucket number 1-n |
| `PERCENT_RANK()` | Window | [PERCENT_RANK.md] | Relative rank (0-1) |
| `CUME_DIST()` | Window | [CUME_DIST.md] | Cumulative distribution |
| `FIRST_VALUE(v)` | Window | [FIRST_VALUE.md] | First value in partition |
| `LAST_VALUE(v)` | Window | [LAST_VALUE.md] | Last value in partition |
| `NTH_VALUE(v, n)` | Window | [NTH_VALUE.md] | Nth value in window frame |
| `PERCENTILE_CONT(n)` | Window | [PERCENTILE_CONT.md] | Continuous percentile |
| `PERCENTILE_DISC(n)` | Window | [PERCENTILE_DISC.md] | Discrete percentile |
| `REGEXP_LIKE(s, p)` | Regex | [REGEXP_LIKE.md] | 1 if string matches regex |
| `REGEXP_REPLACE(s, p, r)` | Regex | [REGEXP_REPLACE.md] | Replace matches in string |
| `REGEXP_SUBSTR(s, p)` | Regex | [REGEXP_SUBSTR.md] | Matched substring |
| `REGEXP_INSTR(s, p)` | Regex | [REGEXP_INSTR.md] | Position of match |
| `REGEXP_COUNT(s, p)` | Regex | [REGEXP_COUNT.md] | Count of matches |
| `REGEXP_MATCHES(s, p)` | Regex | [REGEXP_MATCHES.md] | Table of all matches |
| `REGEXP_SPLIT(...)` | Regex | [REGEXP_SPLIT.md] | Table of split segments |
| `LISTAGG(v, s)` | Aggregate | [LISTAGG.md] | Concatenates values with separator |
| `STRING_AGG(v, s)` | Aggregate | [STRING_AGG.md] | Concatenates strings with separator |
| `ADD_TO_LIST(l, v)` | List | [ADD_TO_LIST.md] | Appends value to a LIST |
| `SORT_LIST(l)` | List | [SORT_LIST.md] | Returns sorted copy of list |
| `APPEND_TO_LIST(l, v)`| List | [APPEND_TO_LIST.md] | Alias for ADD_TO_LIST |
| `REMOVE_FROM_LIST(l, v)`| List | [REMOVE_FROM_LIST.md] | Removes occurrences from list |
| `GET_TAGS(t, [c])` | Lineage | [GET_TAGS.md] | Returns list of tag names |
| `GET_TAG_VALUE(t, c, n)`| Lineage | [GET_TAG_VALUE.md] | Returns value of specific tag |
| `NORMALIZE(s, [m])` | Fuzzy | [NORMALIZE.md] | Domain-aware preprocessing |
| `SIMILARITY(a, b, [m])`| Fuzzy | [SIMILARITY.md] | Normalized similarity score (0-1) |
| `LEVENSHTEIN(a, b)` | Fuzzy | [LEVENSHTEIN.md] | Raw edit distance |
| `SOUNDEX(s)` | Fuzzy | [SOUNDEX.md] | 4-char phonetic code |
| `METAPHONE(s)` | Fuzzy | [METAPHONE.md] | English phonetic code |
| `DMETAPHONE(s)` | Fuzzy | [DMETAPHONE.md] | Double Metaphone primary code |
| `DMETAPHONE_ALT(s)` | Fuzzy | [DMETAPHONE_ALT.md] | Double Metaphone alternate code |
| `NGRAMS(s, n)` | Fuzzy | [NGRAMS.md] | Table of N-character grams |
| `NGRAM_TOKENS(s)` | Fuzzy | [NGRAM_TOKENS.md] | Table of 3-grams (blocking) |
| `GENERATE_SERIES` | System | [GENERATE_SERIES.md] | Returns table of numbers/dates |
| `SIN(n)` | Math | [SIN.md] | Sine of n (radians) |
| `COS(n)` | Math | [COS.md] | Cosine of n (radians) |
| `TAN(n)` | Math | [TAN.md] | Tangent of n (radians) |
| `ASIN(n)` | Math | [ASIN.md] | Arcsine of n |
| `ACOS(n)` | Math | [ACOS.md] | Arccosine of n |
| `ATAN(n)` | Math | [ATAN.md] | Arctangent of n |
| `ATAN2(y, x)` | Math | [ATAN2.md] | Arctangent of y/x |
| `SIGN(n)` | Math | [SIGN.md] | Returns -1, 0, or 1 |
| `STUFF(s, b, l, r)` | String | [STUFF.md] | Deletes part of string and inserts new |
| `STR(n, [l], [d])` | String | [STR.md] | Formats number as string |
| `PATINDEX(p, s)` | String | [PATINDEX.md] | Position of first match of pattern |
| `REPLICATE(s, n)` | String | [REPLICATE.md] | Repeats string n times |
| `QUOTENAME(s, [c])` | String | [QUOTENAME.md] | Adds delimiters to identifier |
| `TRANSLATE(s, f, t)`| String | [TRANSLATE.md] | Replaces characters 1-to-1 |
| `ASCII(s)` | String | [ASCII.md] | ASCII code of first character |
| `UNICODE(s)` | String | [UNICODE.md] | Unicode code of first character |
| `CHAR(n)` | String | [CHAR.md] | Character for ASCII/Unicode code |
| `DATETIMEFROMPARTS` | Date | [DATE_PARTS.md] | Builds date from integers |
| `TIMEFROMPARTS` | Date | [TIME_PARTS.md] | Builds time from integers |
| `SYSDATE()` | Date | [SYSDATE.md] | Current server datetime |
| `CHECKSUM(...)` | System | [CHECKSUM.md] | Computed hash of values |
| `BINARY_CHECKSUM(...)`| System | [BINARY_CHECKSUM.md] | Binary-aware hash |
| `NEWSEQUENTIALID()` | System | [GUID.md] | Generates a time-ordered GUID |
| `FORMAT(v, f)` | System | [FORMAT.md] | Formats value using string pattern |

*Note: Over 159 functions are registered. See [Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md) for the full list of signatures.*

---

## 4. Window Functions

Window functions perform calculations across a set of table rows that are somehow related to the current row.

### 4.1 Window Syntax
```sql
FUNCTION_NAME(args) OVER (
  [PARTITION BY col1, col2, ...]
  [ORDER BY colA [ASC|DESC], ...]
  [ROWS|RANGE BETWEEN <bound> AND <bound>]
)
```

**Supported Bounds:**
- `UNBOUNDED PRECEDING`
- `<n> PRECEDING`
- `CURRENT ROW`
- `<n> FOLLOWING`
- `UNBOUNDED FOLLOWING`

### 4.2 Dedicated Window Functions
| Function | Help File | Description |
| :--- | :--- | :--- |
| `ROW_NUMBER()` | [ROW_NUMBER.md] | Sequential row number within partition |
| `RANK()` | [RANK.md] | Rank with gaps for ties |
| `DENSE_RANK()` | [DENSE_RANK.md] | Rank without gaps for ties |
| `PERCENT_RANK()` | [PERCENT_RANK.md] | Relative rank (0 to 1) |
| `CUME_DIST()` | [CUME_DIST.md] | Cumulative distribution |
| `NTILE(n)` | [NTILE.md] | Divide rows into N buckets |
| `LAG(v, [n], [d])` | [LAG.md] | Value from N rows before |
| `LEAD(v, [n], [d])` | [LEAD.md] | Value from N rows after |
| `FIRST_VALUE(v)` | [FIRST_VALUE.md] | First value in window frame |
| `LAST_VALUE(v)` | [LAST_VALUE.md] | Last value in window frame |
| `NTH_VALUE(v, n)` | [NTH_VALUE.md] | Nth value in window frame |
| `PERCENTILE_CONT(n)` | [PERCENTILE_CONT.md] | Continuous percentile |
| `PERCENTILE_DISC(n)` | [PERCENTILE_DISC.md] | Discrete percentile |

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
| `@@RESULTSETS` | Count of result sets from last stmt | [@@RESULTSETS.md] |
| `@@PARTITIONS_COUNT` | External spill partition count | [@@PARTITIONS_COUNT.md] |
| `@@FILE_EXISTS(p)` | Helper for script existence checks | [@@FILE_EXISTS.md] |
| `@@DIRECTORY_EXISTS(p)`| Helper for script existence checks | [@@DIRECTORY_EXISTS.md] |

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
| `WEEK_START_DAY` | Localization | Monday | [Options/INDEX.md] |
| `EXTERNAL_HASH_PARTITIONS` | Performance | 32 | [Options/INDEX.md] |
| `EXTERNAL_SORT_CHUNK_SIZE` | Performance | 50,000 | [Options/INDEX.md] |
| `FOREACH_PAGE_SIZE` | Performance | 10,000 | [Options/INDEX.md] |
| `INTERACTIVE_MODE` | Session | OFF | [Options/INDEX.md] |
| `MAX_FILE_OPERATIONS` | Security | 100 | [Options/INDEX.md] |
| `MAX_GENERATE_ROWS` | Performance | 1,000,000 | [Options/INDEX.md] |
| `MAX_GROUPING_SETS` | Performance | 100 | [Options/INDEX.md] |
| `MAX_IN_MEMORY_BATCHES` | Performance | 100 | [Options/INDEX.md] |
| `MAX_MESSAGES` | Diagnostics | 1,000 | [Options/INDEX.md] |
| `MAX_RECURSIVE_DEPTH` | Flow | 10,000 | [Options/INDEX.md] |
| `MAX_SESSION_SIZE` | Performance | 500 MB | [Options/INDEX.md] |
| `MAX_STRING_RESULT_SIZE` | Performance | 5 MB | [Options/INDEX.md] |
| `PERSIST` | Session | ON | [Options/INDEX.md] |
| `REGEX_MATCH_TIMEOUT` | Flow | 1,000ms | [Options/INDEX.md] |
| `SPILL_COMPRESSION` | Performance | ON | [Options/INDEX.md] |
| `SPILL_ENCRYPTION` | Performance | ON | [Options/INDEX.md] |
| `SPILL_FORMAT` | Performance | AUTO | [Options/INDEX.md] |
| `WINDOW_SPILL_THRESHOLD` | Performance | 100,000 | [Options/INDEX.md] |
| `MAX_LAST_RESULT_ROWS` | Performance | 1,000 | [Options/INDEX.md] |
| `MAX_INTERNAL_OPERATIONS`| Performance | 1,000,000 | [Options/INDEX.md] |
| `SET_CUBE_LIMIT` | Performance | 10 | [Options/INDEX.md] |
| `SCRIPT_HASH_POLICY` | Security | VALIDATE | [Options/INDEX.md] |
| `CASE_SENSITIVE` | Execution | OFF | [Options/INDEX.md] |
| `TEMPLATE_PATH` | Report | NULL | [Options/INDEX.md] |
| `ALLOW_FILE_TYPE_ACCESS` | Security | OFF | [Options/INDEX.md] |
| `ALLOW_FILE_OPERATIONS` | Security | 100 | [Options/INDEX.md] |
| `ALLOW_RECURSIVE_LAYERS` | Security | 5 | [Options/INDEX.md] |
| `ALLOW_...` (various) | Security | OFF | [Options/INDEX.md] |

---

## 6. Object Creation Options (WITH Clauses)

Options available when creating or altering engine and report objects.

### 6.1 CREATE CONNECTION
```sql
CREATE CONNECTION name ON <Provider> WITH ( ... )
```
| Option | Description | Documentation |
| :--- | :--- | :--- |
| `HOST` / `SERVER` | Server hostname or IP | [Data_Connectors.md] |
| `PORT` | Network port | [Data_Connectors.md] |
| `DATABASE` | Database name | [Data_Connectors.md] |
| `USER` / `UID` | Username | [Data_Connectors.md] |
| `PASSWORD` / `PWD` | Password (can be 'ENC:...') | [Data_Connectors.md] |
| `TRUSTED_CONNECTION`| Use Windows Auth (MSSQL only) | [Data_Connectors.md] |
| `ENCRYPT` | Enable SSL/TLS encryption | [Data_Connectors.md] |
| `PATH` | Root path for file-based connectors | [Data_Connectors.md] |
| `DSN` / `DRIVER` | ODBC specific identifiers | [Data_Connectors.md] |
| `KEYFILE` | Path to private key (SFTP/PGP) | [Data_Connectors.md] |
| `PASSPHRASE` | Keyfile decryption password | [Data_Connectors.md] |
| `SSL_MODE` | Postgres SSL behavior | [Data_Connectors.md] |

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
| `PINNABLE` | Container | Enable/disable portal pinning |
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

## 7. Report-SQL (Object Summary)

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
| `RELDATEPICKER` | Filter | [Visuals/RELDATEPICKER.md] |
| `SEARCH` | Filter | [Visuals/SEARCH.md] |
| `SLIDER` | Filter | [Visuals/SLIDER.md] |
| `TREEMAP` | Chart | [Visuals/TREEMAP.md] |
| `WATERFALL` | Chart | [Visuals/WATERFALL.md] |
| `MATRIX` | Data | [Visuals/MATRIX.md] |
| `MULTISELECT` | Filter | [Visuals/MULTISELECT.md] |
| `RADAR` | Chart | [Visuals/RADAR.md] |
| `TEXT` | Static | [Visuals/TEXT.md] |
| `IMAGE` | Static | [Visuals/IMAGE.md] |
| `FUNNEL` | Chart | [Visuals/FUNNEL.md] |
| `BOXPLOT` | Chart | [Visuals/BOXPLOT.md] |
| `BUBBLE` | Chart | [Visuals/BUBBLE.md] |
| `CANDLESTICK` | Chart | [Visuals/CANDLESTICK.md] |
| `SANKEY` | Chart | [Visuals/SANKEY.md] |
| `SUNBURST` | Chart | [Visuals/SUNBURST.md] |
| `NETWORK` | Chart | [Visuals/NETWORK.md] |
| `TRELLIS` | Chart | [Visuals/TRELLIS.md] |
| `CREATE DASHBOARD` | Report Obj | [Report/DASHBOARD.md] |

---

## 7. Portal & Orchestrator Admin

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
| `SHOW CONNECTION` | Diagnostics | Lists all active connections |
| `SHOW CONNECTIONS` | Diagnostics | Lists all active connections |
| `SHOW SESSIONS` | Portal | Lists active web/CLI sessions |
| `SHOW ZONES` | Diagnostics | Lists security zones and policies |

---

## 9. Visual Action Commands

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

## 10. Operators & Symbols

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

## 11. Data Types

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

## 12. Join Syntax

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

## 13. Set Operations

Combine result sets from multiple `SELECT` statements.

| Operation | Description |
| :--- | :--- |
| `UNION` | Returns distinct rows from both sets |
| `UNION ALL` | Returns all rows from both sets (including duplicates) |
| `EXCEPT` | Returns rows from first set not present in second |
| `INTERSECT` | Returns only rows present in both sets |

---

## 14. Query Clauses & Modifiers

Standard clauses available within a `SELECT` statement.

| Clause | Description | Documentation |
| :--- | :--- | :--- |
| `DISTINCT` | Returns only unique rows | [Grammar.md] |
| `TOP (n)` | Limits results (MSSQL style) | [Grammar.md] |
| `LIMIT n` | Limits results (Postgres style) | [Grammar.md] |
| `OFFSET n` | Skips first N rows | [Grammar.md] |
| `GROUP BY` | Aggregates rows by column values | [Grammar.md] |
| `HAVING` | Filters aggregated groups | [Grammar.md] |
| `ORDER BY` | Sorts the final result set | [Grammar.md] |
| `ASC` / `DESC` | Sorting direction | [Grammar.md] |
| `ROLLUP` | Grouping set extension for hierarchies | [Grammar.md] |
| `CUBE` | Grouping set extension for all permutations| [Grammar.md] |
| `GROUPING SETS` | Explicit grouping set list | [Grammar.md] |
| `QUALIFY` | Filters results of window functions | [Grammar.md] |
| `OUTPUT` | Returns modified rows (DML only) | [Grammar.md] |
| `FOR JSON` | Formats output as JSON (PATH/AUTO/RAW) | [Grammar.md] |
| `FOR XML` | Formats output as XML (PATH/AUTO/RAW) | [Grammar.md] |
| `CASE` | Start of conditional expression | [Grammar.md] |
| `WHEN / THEN` | Conditional branch | [Grammar.md] |
| `ELSE / END` | Fallback and termination of CASE | [Grammar.md] |

---

## 15. Table Operators

Operators that transform the shape of a table in the `FROM` clause.

| Operator | Syntax | Description |
| :--- | :--- | :--- |
| `PIVOT` | `PIVOT ( agg(col) FOR pivot_col IN (...) )` | Rotates rows into columns |
| `UNPIVOT` | `UNPIVOT ( val_col FOR name_col IN (...) )` | Rotates columns into rows |

---

## 16. Metadata & Script Tags

Annotations used for lineage, security, and script behavior.

| Tag | Level | Usage |
| :--- | :--- | :--- |
| `/*@tag: val */` | Row / Column | Lineage and metadata tagging |
| `@tag: val;` | Script Header | Script-level metadata (e.g. `@author: dev`) |
| `ENC:...` | Literal | Prefix for engine-encrypted strings |
| `BANG` / `!` | Session | Prefix for named Environment Sets (e.g. `!PROD`) |



