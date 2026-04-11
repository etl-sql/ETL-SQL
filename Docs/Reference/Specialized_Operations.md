# ETL-SQL Specialized Operations & Automation

This document provides a comprehensive technical reference for the non-SQL orchestration and automation features of ETL-SQL, including filesystem management, remote transfers, email, and diagnostics.

---

## 1. File & Directory Operations

ETL-SQL provides a unified interface for local filesystem management. All paths are resolved via the `SecurityService` for safety.

### 1.1 Local File Operations
Supports both SQL-style and function-style syntax.
- **`COPY FILE <src> TO <dest> [WITH(OVERWRITE=ON|OFF)]`**
- **`MOVE FILE <src> TO <dest> [WITH(OVERWRITE=ON|OFF)]`**
- **`DELETE FILE <path>`**
- **`RENAME FILE <path> TO <new_name>`**
- **`COMPRESS FILE <src> TO <dest.zip>`**
- **`ENCRYPT FILE <src> TO <dest> PASSWORD('<pwd>')`**

### 1.2 Local Directory Operations
- **`CREATE DIRECTORY <path>`**
- **`DELETE DIRECTORY <path>`** (Requires directory to be empty).
- **`DELETE DIRECTORY_CONTENTS <path> [WITH(RECURSIVE=ON|OFF)]`**
- **`COPY DIRECTORY <src> TO <dest>`**

---

## 2. Remote Orchestration

Coordinate data movement between the local machine and remote connectors (SFTP, FTP, Azure Blob).

### 2.1 File Transfers
- **`SEND FILE '<local>' TO '<remote>' AT <connection_name>`**: Uploads a file.
- **`RECEIVE FILE FROM '<remote>' TO '<local>' AT <connection_name>`**: Downloads a file.

### 2.2 Remote Introspection
- **`REMOTE_FILE_LIST(@connection, [@path])`** -> Returns a metadata table (`Name`, `Path`, `Size`, `LastModified`).

---

## 3. Automated Email (`SEND EMAIL`)

Sends high-fidelity emails using a configured `SMTP` connection. Supports attachments and flexible formatting.

**Syntax:**
```sql
SEND EMAIL 
    TO 'admin@company.com'
    FROM 'etl@company.com' -- Defaults to connection setting
    SUBJECT 'Job Failure: Nightly Load'
    BODY 'The incremental load failed at step 4. See attached log.'
    ATTACH 'C:\Logs\nightly_error.log'
    AT mailer_conn;
```

---

## 4. Lineage & Metadata

ETL-SQL tracks the ancestry of every data point. Metadata tags (`/* @d: description */`) are automatically propagated through joins and transformations.

### 4.1 Reporting
- **`LINEAGE(<target_table> [, <column>]) [TO '<file.md>']`**
  - Exports a Mermaid.js diagram and detailed audit log.
- **`SELECT * FROM LINEAGE(<target_table>)`**: Queries the audit trail as a table.

### 4.2 Metadata Functions
- **`GET_TAGS(@table, [@column])`**: Returns a `LIST` of active metadata tag names.
- **`GET_TAG_VALUE(@table, @column, @tag)`**: Returns the specific value of a tag.

---

## 5. Security & Infrastructure

### 5.1 SSH Key Management
Generates cryptographic key pairs for SFTP and file encryption.
- **`CREATE SSH_KEY_PAIR '<dir>' [WITH(BITS=4096, ALGORITHM='RSA', PASSPHRASE='...')];`**

### 5.2 Docker Lifecycle
Manage containerized databases for temporary ETL staging.
- **`USE DOCKER('<image>') AS <alias>`**
- **`<alias> STOP | START | CLOSE`**

### 5.3 Job Scheduling (`CREATE JOB`)
Orchestrates script execution on a recurring schedule (supported in Service mode).
- **`CREATE JOB 'NightlySync' EXECUTE 'sync.etlsql' AT '00:00:00' EVERY 1 DAY;`**
- **`SHOW JOBS`** / **`DROP JOB <name>`**.
- **`START JOB <name>`** / **`STOP JOB <name>`**.

---

## 6. Diagnostics & Profiling

Optimize script performance and validate logic.

- **`EXPLAIN <query>`**: Shows the internal join and stream strategy.
- **`LINT ['path']`**: Statically analyzes code for security and performance risks.
- **`SET PROFILING ON | OFF`**: Enables metric tracking.
- **`SHOW PROFILE [INTO #temp]`**: Displays execution times for all recent statements.

---
*Refer to [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) for syntax rules and [Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md) for the function catalog.*
