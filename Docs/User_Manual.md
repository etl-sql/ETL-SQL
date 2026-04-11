# ETL-SQL User Manual: Thinking in Pipelines

Welcome to ETL-SQL. This guide is designed to help you transition from thinking in "Single Database SQL" to "Multi-Context Data Flow." It establishes the mental model required to build robust, high-performance ETL pipelines.

---

## 1. The Pipeline Mental Model

The most important concept to master is **Context Awareness**. In a standard SQL environment, your query runs against a single engine. In ETL-SQL, you are the **Conductor** of an orchestra of engines.

### 1.1 Engine Context vs. Remote Context
- **Engine Context**: This is the ETL-SQL "Brain." It manages variables, temp tables, and coordinates the flow.
- **Remote Context**: This is the source/target database (MSSQL, Postgres, Oracle) or file provider (SFTP, FLATFILE).

**The Golden Rule**: Data always flows through the Engine. If you want to move data from Postgres to a CSV, you typically stage it in the Engine's memory first using a `#Temp` table to ensure "Zero-Trust" validation occurs before the write.

---

## 2. Procedural Power & Logic Flow

ETL-SQL is more than just querying; it is a procedural orchestration language.

### 2.1 Blocks & Conditional logic
Use `BEGIN...END` to wrap multiple statements and `IF...ELSE IF...ELSE` to branch your pipeline logic.
```sql
IF (SELECT COUNT(*) FROM src.Inbound) > 0
BEGIN
    PRINT 'Processing new data...';
    RUN SCRIPT 'ingest.etlsql';
END
```

### 2.2 Error Handling (`TRY...CATCH`)
Never let a pipeline fail silently. Wrap your critical tasks in `BEGIN TRY...END TRY` blocks to capture exceptions and trigger notifications or rollbacks.
```sql
BEGIN TRY
    BEGIN TRANSACTION;
    -- Destructive MERGE here
    COMMIT;
END TRY
BEGIN CATCH
    ROLLBACK;
    SEND EMAIL TO 'ops@company.com' SUBJECT 'Logistics Sync Failed' BODY ERROR_MESSAGE();
END CATCH
```

---

## 3. The #Temp Table Workspace

Temporary tables (prefixed with `#`) are the Engine's primary workspace. They represent data held in-memory or in high-speed local storage.

### Why use them?
1. **Decoupling**: Store results from a slow legacy database before joining them with a modern cloud source.
2. **Context Switching**: Perform advanced ETL-SQL functions (like Regex or JSON extraction) on data that came from a database that doesn't support them.
3. **Data Reconciliation**: Stage both a "Source" and "Target" snapshot into temp tables to identify gaps before performing a destructive `MERGE`.

---

## 4. Zero-Trust Security Guardrails

ETL-SQL assumes all data and paths are untrustworthy by default.

### 4.1 Path Resolution
The engine uses `ResolvePath` for every file operation. You cannot access system directories unless explicitly authorized. Use `DIRECTORY` connections to create "Sandboxes" for your scripts.

### 4.2 Credential Masking
Sensitive strings like passwords and API keys should be handled using `ENCRYPTED` variables and `USE PASSWORD`.
```sql
USE PASSWORD = 'masterSecret';
CREATE CONNECTION secure ON MSSQL('ENC:U2FsdGVkX1+...');
```

---

## 5. Operating at Scale

ETL-SQL is built for high-throughput, high-concurrency environments.

### 5.1 Parallelization
Use the `PARALLEL` block to run independent tasks simultaneously. This is ideal for loading multiple dimension tables (Date, Product, Region) in a single window.
```sql
PARALLEL
BEGIN
    SELECT * INTO #DimA FROM src.TableA;
    SELECT * INTO #DimB FROM src.TableB;
END
```

### 5.2 Bulk Loading & Batching
For datasets exceeding 1,000,000 rows, use `BULK INSERT` for O(1) memory overhead. The engine will stream the data in chunks, ensuring it never exhausts system memory regardless of file size.

---
## Next Steps
- **[Cookbook.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Cookbook.md)**: 12+ production recipes for SFTP, SCD Type 2, and IoT Ingestion.
- **[Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md)**: Syntax reference for loops, conditions, and variables.
- **[Standard_Library.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Standard_Library.md)**: Exhaustive function catalog (Date, String, Math, Regex).
- **[Specialized_Operations.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Specialized_Operations.md)**: Reference for Email, File Ops, Lineage, and SSH.
