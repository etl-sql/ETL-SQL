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
| `CREATE JOB` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [SCHEDULE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SCHEDULE.md) |
| `KILL JOB` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | - |
| `CREATE INDEX` | DDL | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [CREATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `CREATE PROCEDURE` | DDL | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [CREATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `CREATE FUNCTION` | DDL | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [CREATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CREATE.md) |
| `GENERATE` | DML | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [GENERATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/GENERATE.md) |
| `CASE` | Expressions | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [CASE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/CASE.md) |
| `WITH` | CTE | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [WITH.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/WITH.md) |
| `WITH RECURSIVE` | CTE | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [WITH.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/WITH.md) |
| `PIVOT` / `UNPIVOT` | DML / Transform | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [PIVOT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/PIVOT.md) |
| `EXPORT REPORT` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [EXPORT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/EXPORT.md) |
| `SUBSCRIPTION` | Orchestration | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | [SUBSCRIPTION.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/SUBSCRIPTION.md) |
| `RELDATE` | Variables | [RelativeDate_Parameters.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/RelativeDate_Parameters.md) | [RELDATE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Keywords/RELDATE.md) |
| `RAISEERROR` | Flow Control | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | - |
| `HELP` | Diagnostics | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | - |
| `ANALYZE` | Diagnostics | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) | - |
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

### 2.1 File-Based Table Alias
`FILE` is the default table name used when querying any file-based connection (e.g. `SELECT * FROM src` where `src` is a FLATFILE connection).

### 2.2 Connector Aliases
`CSV` is an accepted alias for `FLATFILE` in `CREATE CONNECTION` statements.

---

## 3. Standard Library (Functions)

Functions used within `SELECT`, `WHERE`, `SET`, and other expressions.

| Function | Category | Help File | Description |
| :--- | :--- | :--- | :--- |
| `UPPER(string)` | String | [UPPER.md] | Converts string to uppercase |
| `LOWER(string)` | String | [LOWER.md] | Converts string to lowercase |
| `CONCAT(string1, string2, ...)` | String | [CONCAT.md] | Concatenates multiple strings |
| `LEN(string)` / `LENGTH(string)` | String | [LEN.md] / [LENGTH.md] | Returns string length |
| `SUBSTRING(string, start, length)` | String | [SUBSTRING.md] | Returns part of a string |
| `TRIM(string)` | String | [TRIM.md] | Removes leading/trailing whitespace |
| `REPLACE(string, find, replacement)` | String | [REPLACE.md] | Replaces occurrences of a substring |
| `CHARINDEX(find, string)` | String | [CHARINDEX.md] | Returns index of first occurrence |
| `INITCAP(string)` | String | [INITCAP.md] | Capitalizes first letter of each word |
| `LTRIM(string)` | String | [LTRIM.md] | Removes leading whitespace |
| `RTRIM(string)` | String | [RTRIM.md] | Removes trailing whitespace |
| `REVERSE(string)` | String | [REVERSE.md] | Reverses string characters |
| `LEFT(string, count)` | String | [LEFT.md] | Returns leftmost N characters |
| `RIGHT(string, count)` | String | [RIGHT.md] | Returns rightmost N characters |
| `INSTR(string, find)` | String | [INSTR.md] | Alias for POSITION |
| `CONCAT_WS(separator, string1, ...)` | String | [CONCAT_WS.md] | Join with separator; skips nulls |
| `SPLIT_PART(string, delimiter, part)` | String | [SPLIT_PART.md] | Returns Nth segment after split |
| `SPACE(count)` | String | [SPACE.md] | Returns N space characters |
| `TO_STR(value)` | String | [TO_STR.md] | Converts any value to string |
| `PATINDEX(pattern, string)` | String | [PATINDEX.md] | Position of wildcard pattern |
| `REPLICATE(string, count)` | String | [REPLICATE.md] | Repeats string N times |
| `QUOTENAME(string, [delimiter])` | String | [QUOTENAME.md] | Returns delimited identifier |
| `ASCII(string)` | String | [ASCII.md] | Numeric code of first character |
| `UNICODE(string)` | String | [UNICODE.md] | Unicode code of first character |
| `CHAR(code)` | String | [CHAR.md] | Character for given code |
| `DATALENGTH(value)` | String | [DATALENGTH.md] | Byte count of value |
| `TRANSLATE(string, find_chars, replace_chars)` | String | [TRANSLATE.md] | Replaces chars 1-to-1 |
| `STRING_ESCAPE(text, type)` | String | [STRING_ESCAPE.md] | Escapes special characters |
| `STRING_SPLIT(string, delimiter)` | String | [STRING_SPLIT.md] | Table-valued split |
| `CHAR_LENGTH(string)` | String | [CHAR_LENGTH.md] | String length (SQL standard alias) |
| `OVERLAY(string, replacement, start, length)` | String | [OVERLAY.md] | Replaces substring at position |
| `POSITION(find IN string)` | String | [POSITION.md] | Position of substring (SQL standard) |
| `SUBSTR(string, start, length)` | String | [SUBSTR.md] | Alias for SUBSTRING |
| `STUFF(string, start, length, replacement)` | String | [STUFF.md] | Deletes part of string and inserts replacement |
| `STR(number, [length], [decimals])` | String | [STR.md] | Formats number as string |
| `GETDATE()` | Date | [GETDATE.md] | Current local datetime |
| `NOW()` | Date | [NOW.md] | Current UTC datetime |
| `DATEADD(datepart, number, date)` | Date | [DATEADD.md] | Adds units to a date |
| `DATEDIFF(datepart, start_date, end_date)` | Date | [DATEDIFF.md] | Difference between dates |
| `DATENAME(datepart, date)` | Date | [DATENAME.md] | Returns name of date part |
| `DATEPART(datepart, date)` | Date | [DATEPART.md] | Returns integer date part |
| `EOMONTH(date)` | Date | [EOMONTH.md] | Last day of the month |
| `ISDATE(string)` | Date | [ISDATE.md] | 1 if parseable as date |
| `DATETIMEFROMPARTS(year, month, day, hour, minute, second, ms)` | Date | [DATETIMEFROMPARTS.md] | Build DATETIME from components |
| `TIMEFROMPARTS(hour, minute, second, fractions, precision)` | Date | [TIMEFROMPARTS.md] | Build TIME from components |
| `TRUNC(date)` | Date | [TRUNC.md] | Truncates time portion |
| `AT TIME ZONE(date, timezone)` | Date | [AT_TIME_ZONE.md] | Converts to specified timezone |
| `CURRENT_DATE()` | Date | [CURRENT_DATE.md] | Current date (no time) |
| `CURRENT_TIME()` | Date | [CURRENT_TIME.md] | Current time |
| `CURRENT_TIMESTAMP()` | Date | [CURRENT_TIMESTAMP.md] | Current datetime (UTC) |
| `DATETRUNC(datepart, date)` | Date | [DATETRUNC.md] | Truncates date to unit boundary |
| `DAY(date)` | Date | [DAY.md] | Day-of-month component |
| `MONTH(date)` | Date | [MONTH.md] | Month component |
| `YEAR(date)` | Date | [YEAR.md] | Year component |
| `HOUR(date)` | Date | [HOUR.md] | Hour component |
| `MINUTE(date)` | Date | [MINUTE.md] | Minute component |
| `SECOND(date)` | Date | [SECOND.md] | Second component |
| `ABS(number)` | Math | [ABS.md] | Absolute value |
| `ROUND(number, decimals)` | Math | [ROUND.md] | Rounds to N decimal places |
| `FLOOR(number)` | Math | [FLOOR.md] | Largest integer <= number |
| `CEILING(number)` | Math | [CEILING.md] | Smallest integer >= number |
| `CEIL(number)` | Math | [CEIL.md] | Alias for CEILING |
| `RAND()` | Math | [RAND.md] | Random number [0, 1) |
| `RANDOM()` | Math | [RANDOM.md] | Alias for RAND() |
| `RANDOM_INT(min, max)` | Math | [RANDOM_INT.md] | Random integer in range |
| `RANDOM_DECIMAL(min, max)` | Math | [RANDOM_DECIMAL.md] | Random decimal in range |
| `MOD(number, divisor)` / `number % divisor` | Math | [MOD.md] | Remainder of division |
| `POWER(base, exponent)` | Math | [POWER.md] | Base raised to exponent |
| `POW(base, exponent)` | Math | [POW.md] | Alias for POWER |
| `SQRT(number)` | Math | [SQRT.md] | Square root |
| `EXP(number)` | Math | [EXP.md] | e raised to the power of number |
| `LOG(number)` / `LN(number)` | Math | [LOG.md] | Natural logarithm |
| `LOG10(number)` | Math | [LOG10.md] | Base-10 logarithm |
| `LEAST(value1, value2, ...)` | Math | [LEAST.md] | Smallest of arguments |
| `GREATEST(value1, value2, ...)` | Math | [GREATEST.md] | Largest of arguments |
| `SIN(radians)` | Math | [SIN.md] | Sine |
| `COS(radians)` | Math | [COS.md] | Cosine |
| `TAN(radians)` | Math | [TAN.md] | Tangent |
| `ASIN(number)` | Math | [ASIN.md] | Arcsine |
| `ACOS(number)` | Math | [ACOS.md] | Arccosine |
| `ATAN(number)` | Math | [ATAN.md] | Arctangent |
| `ATAN2(y, x)` | Math | [ATAN2.md] | Arctangent of y/x |
| `SIGN(number)` | Math | [SIGN.md] | Returns -1, 0, or 1 |
| `DEGREES(radians)` | Math | [DEGREES.md] | Converts radians to degrees |
| `RADIANS(degrees)` | Math | [RADIANS.md] | Converts degrees to radians |
| `PI()` | Math | [PI.md] | Mathematical constant Ï€ |
| `QUOTIENT(number, divisor)` | Math | [QUOTIENT.md] | Integer quotient of division |
| `TRUNCATE(number, decimals)` | Math | [TRUNCATE.md] | Truncates number to N decimal places |
| `COALESCE(value1, value2, ...)` | Logic | [COALESCE.md] | First non-null value |
| `ISNULL(value, default)` | Logic | [ISNULL.md] | Returns default if value is null |
| `IIF(condition, true_value, false_value)` | Logic | [IIF.md] | Inline IF |
| `NVL(value, default)` | Logic | [NVL.md] | Alias for ISNULL |
| `IFNULL(value, default)` | Logic | [IFNULL.md] | Alias for ISNULL |
| `NVL2(value, not_null_result, null_result)` | Logic | [NVL2.md] | Oracle-style null conditional |
| `NULLIF(value1, value2)` | Logic | [NULLIF.md] | NULL if value1 = value2 |
| `IS_NULL(value)` | Logic | [IS_NULL.md] | 1 if value is null |
| `IS_NOT_NULL(value)` | Logic | [IS_NOT_NULL.md] | 1 if value is not null |
| `DECODE(value, search1, result1, ..., [default])` | Logic | [DECODE.md] | Oracle-style CASE shorthand |
| `CAST(value AS type)` | System | [CAST.md] | Converts value to type |
| `TRY_CAST(value AS type)` | System | [TRY_CAST.md] | Converts value to type, NULL on fail |
| `CONVERT(type, value)` | System | [CONVERT.md] | Converts value to type |
| `TRY_CONVERT(type, value)` | System | [TRY_CONVERT.md] | CONVERT with NULL on failure |
| `PARSE(string, type)` | System | [PARSE.md] | Culture-aware string to type |
| `TRY_PARSE(string, type)` | System | [TRY_PARSE.md] | PARSE with NULL on failure |
| `HASHBYTES(algorithm, string)` | System | [HASHBYTES.md] | Returns hash of string |
| `NEWID()` | System | [NEWID.md] | Generates a new GUID |
| `NEWSEQUENTIALID()` | System | [NEWSEQUENTIALID.md] | Time-ordered GUID v7 |
| `FORMAT(value, format_string)` | System | [FORMAT.md] | Formats value using string pattern |
| `CHECKSUM(value1, ...)` | System | [CHECKSUM.md] | 64-bit integer hash |
| `BINARY_CHECKSUM(value1, ...)` | System | [BINARY_CHECKSUM.md] | Binary-compatible hash |
| `ENV(variable_name)` | System | [ENV.md] | Host environment variable value |
| `GENERATE_SERIES(start, stop, [step])` | System | [GENERATE_SERIES.md] | Returns table of numbers/dates |
| `ERROR_MESSAGE()` | System | [ERROR_MESSAGE.md] | Error string in CATCH block |
| `ERROR_NUMBER()` | System | [ERROR_NUMBER.md] | Error code in CATCH block |
| `ERROR_SEVERITY()` | System | [ERROR_SEVERITY.md] | Error severity in CATCH block |
| `ERROR_STATE()` | System | [ERROR_STATE.md] | Error state in CATCH block |
| `ERROR_LINE()` | System | [ERROR_LINE.md] | Error line in CATCH block |
| `JSON_VALUE(json, path)` | JSON | [JSON_VALUE.md] | Extracts scalar from JSON |
| `JSON_QUERY(json, path)` | JSON | [JSON_QUERY.md] | Extracts object/array from JSON |
| `JSON_MODIFY(json, path, new_value)` | JSON | [JSON_MODIFY.md] | Updates JSON string |
| `ISJSON(string)` | JSON | [ISJSON.md] | 1 if valid JSON |
| `JSON_EXISTS(json, path)` | JSON | [JSON_EXISTS.md] | 1 if path exists |
| `JSON_OBJECT(key, value, ...)` | JSON | [JSON_OBJECT.md] | Builds JSON object |
| `JSON_ARRAY(value1, ...)` | JSON | [JSON_ARRAY.md] | Builds JSON array |
| `JSON_TABLE(json, path)` | JSON | [JSON_TABLE.md] | Table from JSON |
| `OPENJSON(json, [path])` | JSON | [OPENJSON.md] | SQL Server-style JSON expansion |
| `XMLVALUE(xml, xpath)` | XML | [XMLVALUE.md] | Extracts scalar from XML |
| `XMLEXISTS(xml, xpath)` | XML | [XMLEXISTS.md] | 1 if XPath exists |
| `XMLQUERY(xml, xpath)` | XML | [XMLQUERY.md] | XML fragment |
| `XMLTABLE(xml, xpath)` | XML | [XMLTABLE.md] | Table from XML |
| `XMLELEMENT(name, content)` | XML | [XMLELEMENT.md] | Builds XML element |
| `XMLATTRIBUTES(name, value, ...)` | XML | [XMLATTRIBUTES.md] | XML attributes |
| `XMLFOREST(value1, ...)` | XML | [XMLFOREST.md] | Forest of XML elements |
| `FILE_EXISTS(path)` | File | [FILE_EXISTS.md] | 1 if file exists, 0 otherwise |
| `DIRECTORY_EXISTS(path)` | File | [DIRECTORY_EXISTS.md] | 1 if directory exists, 0 otherwise |
| `FILE_LIST(path, [mask])` | File | [FILE_LIST.md] | Returns table of files in path |
| `REMOTE_FILE_LIST(connection, path)` | File | [REMOTE_FILE_LIST.md] | Table of files on remote connection |
| `DIRECTORY(path)` | File | [DIRECTORY.md] | Returns directory metadata |
| `SUM(expression)` | Aggregate | [SUM.md] | Sum of values |
| `COUNT(expression)` | Aggregate | [COUNT.md] | Count of non-null values |
| `AVG(expression)` | Aggregate | [AVG.md] | Average of values |
| `MAX(expression)` | Aggregate | [MAX.md] | Maximum value |
| `MIN(expression)` | Aggregate | [MIN.md] | Minimum value |
| `MEDIAN(expression)` | Aggregate | [MEDIAN.md] | Median (50th percentile) |
| `VAR(expression)` / `VAR_SAMP` | Aggregate | [VAR.md] | Sample variance |
| `VARP(expression)` / `VAR_POP` | Aggregate | [VARP.md] | Population variance |
| `STDEV(expression)` / `STDDEV` | Aggregate | [STDEV.md] | Sample standard deviation |
| `STDEVP(expression)` | Aggregate | [STDEVP.md] | Population standard deviation |
| `COVAR_SAMP(expr1, expr2)` | Aggregate | [COVAR_SAMP.md] | Sample covariance |
| `COVAR_POP(expr1, expr2)` | Aggregate | [COVAR_POP.md] | Population covariance |
| `CORR(expr1, expr2)` | Aggregate | [CORR.md] | Pearson correlation |
| `LISTAGG(expression, separator)` | Aggregate | [LISTAGG.md] | Concatenates values with separator |
| `STRING_AGG(expression, separator)` | Aggregate | [STRING_AGG.md] | Concatenates strings with separator |
| `ROW_NUMBER()` | Window | [ROW_NUMBER.md] | Sequential row number |
| `RANK()` | Window | [RANK.md] | Rank with gaps |
| `DENSE_RANK()` | Window | [DENSE_RANK.md] | Rank without gaps |
| `LAG(expression, [offset], [default])` | Window | [LAG.md] | Value from N rows before |
| `LEAD(expression, [offset], [default])` | Window | [LEAD.md] | Value from N rows after |
| `NTILE(buckets)` | Window | [NTILE.md] | Bucket number 1-N |
| `PERCENT_RANK()` | Window | [PERCENT_RANK.md] | Relative rank (0-1) |
| `CUME_DIST()` | Window | [CUME_DIST.md] | Cumulative distribution |
| `FIRST_VALUE(expression)` | Window | [FIRST_VALUE.md] | First value in partition |
| `LAST_VALUE(expression)` | Window | [LAST_VALUE.md] | Last value in partition |
| `NTH_VALUE(expression, nth)` | Window | [NTH_VALUE.md] | Nth value in window frame |
| `PERCENTILE_CONT(fraction)` | Window | [PERCENTILE_CONT.md] | Continuous percentile |
| `PERCENTILE_DISC(fraction)` | Window | [PERCENTILE_DISC.md] | Discrete percentile |
| `REGEXP_LIKE(string, pattern)` | Regex | [REGEXP_LIKE.md] | 1 if string matches regex |
| `REGEXP_REPLACE(string, pattern, replacement)` | Regex | [REGEXP_REPLACE.md] | Replace matches in string |
| `REGEXP_SUBSTR(string, pattern)` | Regex | [REGEXP_SUBSTR.md] | Matched substring |
| `REGEXP_INSTR(string, pattern)` | Regex | [REGEXP_INSTR.md] | Position of match |
| `REGEXP_COUNT(string, pattern)` | Regex | [REGEXP_COUNT.md] | Count of matches |
| `REGEXP_MATCHES(string, pattern)` | Regex | [REGEXP_MATCHES.md] | Table of all matches |
| `REGEXP_SPLIT(string, pattern)` | Regex | [REGEXP_SPLIT.md] | Table of split segments |
| `ADD_TO_LIST(list, value)` | List | [ADD_TO_LIST.md] | Appends value to a LIST |
| `SORT_LIST(list)` | List | [SORT_LIST.md] | Returns sorted copy of list |
| `APPEND_TO_LIST(list, value)` | List | [APPEND_TO_LIST.md] | Alias for ADD_TO_LIST |
| `REMOVE_FROM_LIST(list, value)` | List | [REMOVE_FROM_LIST.md] | Removes occurrences from list |
| `GET_TAGS(table, [column])` | Lineage | [GET_TAGS.md] | Returns list of tag names |
| `GET_TAG_VALUE(table, column, tag_name)` | Lineage | [GET_TAG_VALUE.md] | Returns value of specific tag |
| `NORMALIZE(string, [mode])` | Fuzzy | [NORMALIZE.md] | Domain-aware preprocessing |
| `SIMILARITY(string1, string2, [mode])` | Fuzzy | [SIMILARITY.md] | Normalized similarity score (0-1) |
| `LEVENSHTEIN(string1, string2)` | Fuzzy | [LEVENSHTEIN.md] | Raw edit distance |
| `SOUNDEX(string)` | Fuzzy | [SOUNDEX.md] | 4-char phonetic code |
| `METAPHONE(string)` | Fuzzy | [METAPHONE.md] | English phonetic code |
| `DMETAPHONE(string)` | Fuzzy | [DMETAPHONE.md] | Double Metaphone primary code |
| `DMETAPHONE_ALT(string)` | Fuzzy | [DMETAPHONE_ALT.md] | Double Metaphone alternate code |
| `NGRAMS(string, size)` | Fuzzy | [NGRAMS.md] | Table of N-character grams |
| `NGRAM_TOKENS(string)` | Fuzzy | [NGRAM_TOKENS.md] | Table of 3-grams (blocking) |
| `DIFFERENCE(string1, string2)` | Fuzzy | [DIFFERENCE.md] | SOUNDEX difference score (0-4) |

*Note: Over 190 functions are registered. See [Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md) for full signatures. Functions without help file links (XML builders, JSON builders, covariance, lineage helpers) are documented in Standard_Library.md only.*

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
| `NTILE(buckets)` | [NTILE.md] | Divide rows into N buckets |
| `LAG(expression, [offset], [default])` | [LAG.md] | Value from N rows before |
| `LEAD(expression, [offset], [default])` | [LEAD.md] | Value from N rows after |
| `FIRST_VALUE(expression)` | [FIRST_VALUE.md] | First value in window frame |
| `LAST_VALUE(expression)` | [LAST_VALUE.md] | Last value in window frame |
| `NTH_VALUE(expression, nth)` | [NTH_VALUE.md] | Nth value in window frame |
| `PERCENTILE_CONT(fraction)` | [PERCENTILE_CONT.md] | Continuous percentile |
| `PERCENTILE_DISC(fraction)` | [PERCENTILE_DISC.md] | Discrete percentile |

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

### 5.1 System Variables (`@@`)
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
| `RELDATE` | Relative date expression (e.g. 'D-7') | [RelativeDate_Parameters.md] |
| `SENSITIVE` | Masked in output, auto-decrypts `ENC:` | [Grammar.md#L195] |
| `SECRET` | Same as SENSITIVE, purged at session end | [Grammar.md#L213] |
| `MARKDOWN` | Hint for Report Portal rendering | [Grammar.md#L125] |

---

## 6. SET Options (Configuration)

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

## 7. Object Creation Options (WITH Clauses)

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
| `CREATE VISUAL` | Defines a chart or filter | [VISUAL.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/VISUAL.md) |
| `CREATE DATASET` | Defines a data source for visuals | [DATASET.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/DATASET.md) |
| `CREATE PAGE` | Defines a dashboard page layout | [PAGE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/PAGE.md) |
| `CREATE CONTAINER` | Groups visuals in a layout | [CONTAINER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/CONTAINER.md) |
| `CREATE NAVIGATION` | Defines sidebar/top-nav links | [NAVIGATION.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/NAVIGATION.md) |
| `CREATE STYLE` | Defines CSS/Theme overrides | [STYLE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/STYLE.md) |
| `CREATE BUTTON` | Defines a clickable button | [BUTTON.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/BUTTON.md) |
| `ACTIONS` block | Interactive event bindings | [ACTIONS.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/ACTIONS.md) |
| `INTERACTIONS` block | Cross-visual filtering rules | [INTERACTIONS.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Report/INTERACTIONS.md) |

### 8.2 Visual Types
| Type | Category | Help File |
| :--- | :--- | :--- |
| `BAR` / `HBAR` | Chart | [BAR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/BAR.md) / [HBAR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/HBAR.md) |
| `LINE` | Chart | [LINE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/LINE.md) |
| `PIE` / `DONUT` | Chart | [PIE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/PIE.md) / [DONUT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/DONUT.md) |
| `GAUGE` | Chart | [GAUGE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/GAUGE.md) |
| `HEATMAP` | Chart | [HEATMAP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/HEATMAP.md) |
| `SCATTER` | Chart | [SCATTER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/SCATTER.md) |
| `GANTT` | Chart | [GANTT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/GANTT.md) |
| `WATERFALL` | Chart | [WATERFALL.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/WATERFALL.md) |
| `FUNNEL` | Chart | [FUNNEL.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/FUNNEL.md) |
| `BOXPLOT` | Chart | [BOXPLOT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/BOXPLOT.md) |
| `BUBBLE` | Chart | [BUBBLE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/BUBBLE.md) |
| `CANDLESTICK` | Chart | [CANDLESTICK.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/CANDLESTICK.md) |
| `COMBO` | Chart | [COMBO.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/COMBO.md) |
| `TREEMAP` | Chart | [TREEMAP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/TREEMAP.md) |
| `RADAR` | Chart | [RADAR.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/RADAR.md) |
| `SANKEY` | Chart | [SANKEY.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/SANKEY.md) |
| `SUNBURST` | Chart | [SUNBURST.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/SUNBURST.md) |
| `NETWORK` | Chart | [NETWORK.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/NETWORK.md) |
| `TRELLIS` | Chart | [TRELLIS.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/TRELLIS.md) |
| `MATRIX` | Data | [MATRIX.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/MATRIX.md) |
| `TABLE` | Data | [TABLE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/TABLE.md) |
| `CARD` | KPI | [CARD.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/CARD.md) |
| `MAP` | Chart | [MAP.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/MAP.md) |
| `TEXT` | Static | [TEXT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/TEXT.md) |
| `IMAGE` | Static | [IMAGE.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/IMAGE.md) |
| `SLICER` | Filter | [SLICER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/SLICER.md) |
| `DATEPICKER` | Filter | [DATEPICKER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/DATEPICKER.md) |
| `RELDATEPICKER` | Filter | [RELDATEPICKER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/RELDATEPICKER.md) |
| `SEARCH` | Filter | [SEARCH.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/SEARCH.md) |
| `SLIDER` | Filter | [SLIDER.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/SLIDER.md) |
| `MULTISELECT` | Filter | [MULTISELECT.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/MULTISELECT.md) |
| `CHECKBOX` | Control | [CHECKBOX.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/CHECKBOX.md) |
| `TEXTBOX` | Control | [TEXTBOX.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/TEXTBOX.md) |
| `NUMBERBOX` | Control | [NUMBERBOX.md](file:///c:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Core/Resources/Help/Visuals/NUMBERBOX.md) |

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
| `SHOW CONNECTION` | Diagnostics | Lists all active connections |
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
| `DISTINCT` | Returns only unique rows | [Grammar.md] |
| `TOP (n)` | Limits results (MSSQL style) | [Grammar.md] |
| `LIMIT n` | Limits results (Postgres style) | [Grammar.md] |
| `OFFSET n` | Skips first N rows | [Grammar.md] |
| `GROUP BY` | Aggregates rows by column values | [Grammar.md] |
| `HAVING` | Filters aggregated groups | [Grammar.md] |
| `ORDER BY` | Sorts the final result set | [Grammar.md] |
| `ASC` / `DESC` | Sorting direction | [Grammar.md] |
| `ROLLUP` | Grouping set extension for hierarchies | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) |
| `CUBE` | Grouping set extension for all permutations| [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) |
| `GROUPING SETS` | Explicit grouping set list | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) |
| `QUALIFY` | Filters results of window functions | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) |
| `FILTER (WHERE ...)` | Per-aggregate conditional filter | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) |
| `OUTPUT` | Returns modified rows (DML only) | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) |
| `FOR JSON` | Formats output as JSON (PATH/AUTO/RAW) | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) |
| `FOR XML` | Formats output as XML (PATH/AUTO/RAW) | [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) |
| `CASE` | Start of conditional expression | [Grammar.md] |
| `WHEN / THEN` | Conditional branch | [Grammar.md] |
| `ELSE / END` | Fallback and termination of CASE | [Grammar.md] |

---

## 16. Table Operators

Operators that transform the shape of a table in the `FROM` clause.

| Operator | Syntax | Description |
| :--- | :--- | :--- |
| `PIVOT` | `PIVOT ( agg(col) FOR pivot_col IN (...) )` | Rotates rows into columns |
| `UNPIVOT` | `UNPIVOT ( val_col FOR name_col IN (...) )` | Rotates columns into rows |

---

## 17. Metadata & Script Tags

Annotations used for lineage, security, and script behavior.

| Tag | Level | Usage |
| :--- | :--- | :--- |
| `/*@tag: val */` | Row / Column | Lineage and metadata tagging |
| `@tag: val;` | Script Header | Script-level metadata (e.g. `@author: dev`) |
| `ENC:...` | Literal | Prefix for engine-encrypted strings |
| `BANG` / `!` | Session | Prefix for named Environment Sets (e.g. `!PROD`) |


