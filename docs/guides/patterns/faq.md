# ETL-SQL FAQ & Troubleshooting Guide

Common questions, gotchas, and their solutions. If you're stuck, start here.

---

> **Applies to:** every deployment profile. Portal-specific answers say so.

## General

**Q: What is ETL-SQL?**
> ETL-SQL is a scripting language and engine that lets you move, transform, and clean data across multiple heterogeneous sources — SQL databases, flat files, SFTP servers, REST APIs, cloud storage, and more — using familiar SQL-like syntax. Think of it as SQL with a pipeline superpower.

**Q: What version am I running?**
> Use the `@@VERSION` system variable to get the full engine version string at runtime:
> ```sql
> PRINT @@VERSION;
> -- or capture it
> DECLARE @v STRING = @@VERSION;
> ```
> Query `eng.version` for structured version info within a script session. The current release baseline is **v0.18.0**.

**Q: Where do I start?**
> Read [Getting Started](../onboarding/getting-started.md) first — it explains the pipeline mental model that everything else builds on. Then work through the [Cookbook](../../cookbooks/etl/README.md) for production-ready examples.

---

## Dialect & Syntax

**Q: My `SELECT TOP 10` works fine in SSMS but fails with a lint error in ETL-SQL. Why?**
> ETL-SQL is dialect-aware. `TOP` is a T-SQL keyword not supported by Postgres or Oracle. If your `FROM` clause points to a Postgres connection, the linter will reject `TOP` and tell you to use `LIMIT 10` instead.
>
> **Rule of thumb**: use ETL-SQL engine syntax when writing against `#temp` tables. Use the target database's native dialect only inside `EXECUTE conn BEGIN ... END` blocks, where it's passed verbatim.
>
> ```sql
> -- This works — query is against #temp (engine context)
> SELECT TOP 10 * FROM #data ORDER BY Amount DESC;
>
> -- This works — native Postgres syntax inside EXECUTE block  
> EXECUTE pg_conn BEGIN SELECT * FROM customers LIMIT 10; END;
>
> -- This fails lint — TOP used against a Postgres connection directly
> SELECT TOP 10 * FROM pg_conn.customers;
> ```

**Q: What's the correct way to send email from a script?**
> Use the canonical SQL-style statement:
> ```sql
> SEND EMAIL TO 'user@example.com'
> FROM 'etl@example.com'
> SUBJECT 'Pipeline complete'
> BODY 'The nightly run finished.'
> AT smtp_conn;
> ```
> Legacy `SEND_EMAIL(...)` syntax has been retired.

**Q: Can I use `WAIT UNTIL (SELECT ...)` to poll until a condition is true?**
> Yes — `WAIT UNTIL condition` evaluates the expression repeatedly at a 200ms interval and continues execution as soon as the result is truthy (non-zero, non-empty, or `true`):
> ```sql
> -- Polls every 200ms until the condition returns a non-zero count
> WAIT UNTIL (SELECT COUNT(*) FROM control_db.JobStatus WHERE Status = 'Ready') > 0;
> PRINT 'Condition met — proceeding.';
> ```
>
> If you need a longer poll interval or additional logic between checks, use a `WHILE` loop with `WAITFOR DELAY` inside:
> ```sql
> DECLARE @ready INT = 0;
> WHILE @ready = 0
> BEGIN
>     SET @ready = (SELECT COUNT(*) FROM control_db.JobStatus WHERE Status = 'Ready');
>     IF @ready = 0 WAITFOR DELAY '00:01:00';   -- check every minute
> END
> PRINT 'Condition met — proceeding.';
> ```

**Q: Can I use `GETDATE()` in a query against a Postgres connection?**
> No — `GETDATE()` is T-SQL. Use `NOW()` for Postgres, `SYSDATE` for Oracle. When working in engine context (against a `#temp` table), use the ETL-SQL `GETDATE()` function which is always available regardless of what connections you have open.

**Q: Can I use Firebird as a connector?**
> Firebird is not a natively supported connector type. Use `ODBC` with a Firebird ODBC driver instead:
> ```sql
> CREATE CONNECTION firebird_src AS ODBC()
>     WITH(DSN='Firebird_DSN', USER='etl', PASSWORD='...');
> -- or with a driver string
> CREATE CONNECTION firebird_src AS ODBC()
>     WITH(CONNECTION_STRING='Driver={Firebird/InterBase(r) driver};Dbname=localhost:C:\Data\mydb.fdb;User=SYSDBA;Password=<password>;');
> ```

**Q: Does ETL-SQL support PIVOT and UNPIVOT?**
> Yes, the engine fully supports rotating rows into columns (PIVOT) and columns into rows (UNPIVOT) in engine-side queries against `#temp` tables:
> ```sql
> SELECT Year, [Q1], [Q2], [Q3], [Q4]
> FROM #quarterly_revenue
> PIVOT (
>     SUM(Revenue) FOR Quarter IN ([Q1], [Q2], [Q3], [Q4])
> ) AS PivotTable;
> ```

---

## Connections & Credentials

**Q: What is `ENC:` and how do I encrypt my connection string?**
> `ENC:` is the prefix for an AES-256 encrypted credential. At runtime, the engine decrypts it automatically using the master password you set with `USE PASSWORD`.
>
> **Workflow:**
> ```sql
> -- Step 1: Set the master password for this session
> USE PASSWORD = 'myMasterSecret';
>
> -- Step 2: Wrap any plaintext credential — the engine replaces it with ENC:...
> -- (done automatically by the IDE on save, or by calling EncryptScript from the CLI)
>
> -- Step 3: At runtime, ENC: strings are decrypted transparently
> CREATE CONNECTION db AS MSSQL('ENC:U2FsdGVkX1+abc123...');
> ```
> The IDE will warn you if a script still contains plaintext credentials when you save.

**Q: Why does `CREATE CONNECTION` fail with "authentication conflict"?**
> You've likely combined mutually exclusive options. The most common conflicts:
> - `TRUSTED_CONNECTION=TRUE` + `USER=...` — Windows auth and SQL auth cannot both be set
> - `KEY_FILE='...'` + `PASSWORD='...'` — for SFTP, use one auth method, not both
>
> Run `LINT 'yourscript.etlsql'` to get a specific `ConnectionAuthConflictRule` finding that identifies the exact option pair in conflict.

**Q: How do I connect to the same database with different credentials in different environments?**
> Use `CREATE SETS` to define named environment groups:
> ```sql
> CREATE SETS !DEV  BEGIN @server = 'dev-db',  @pwd = 'devpass'  END
> CREATE SETS !PROD BEGIN @server = 'prod-db', @pwd = 'ENC:U2Fs...' END
>
> USE SETS !DEV;
> CREATE CONNECTION db AS MSSQL(SERVER=@server, DATABASE='Sales', PASSWORD=@pwd);
> ```

---

## High Availability (HA)

**Q: How do I configure ETL-SQL for High Availability (HA) in production?**
> By default, standalone installations run with SQLite and local directories. To scale to a multi-node HA cluster:
> 1. **Shared State:** Configure both Portal and Orchestrator to use PostgreSQL (`Portal:Database:Provider = Postgres` and `Orchestrator:Database:Provider = Postgres`).
> 2. **Shared Storage:** Mount a shared filesystem (like SMB or UNC shares) for report scripts, snapshots, and parquet datasets, and configure the path settings.
> 3. **Shared Key Ring:** Configure a shared path for the ASP.NET Data Protection key ring. This ensures all nodes can decrypt cookies and secure states identically.
> 4. **Session Affinity:** Set up your load balancer with sticky routing bound to the `ETLSQL_PORTAL_AFFINITY` cookie.
>
> For full details, see the [Administrators Guide](../../administration/platform/README.md).

**Q: How do HA nodes avoid duplicate scheduled runs or schema migration conflicts?**
> ETL-SQL uses a database-backed **lease fencing** system. 
> * For scheduled jobs and refreshes, only one node can acquire the execution lease at any given time.
> * For upgrades, the first booting node acquires a schema migration lock, applies migrations forward-only, and releases it, preventing other booting nodes from racing or corrupting the database.

---

## File Operations

**Q: Why does my file operation silently do nothing?**
> The most common cause is not checking whether the source file exists first. Always check before operating:
> ```sql
> IF NOT FILE_EXISTS('C:\Incoming\data.csv')
> BEGIN
>     PRINT 'Source file not found.';
>     RETURN;
> END
> COPY FILE 'C:\Incoming\data.csv' TO 'C:\Archive\data.csv';
> ```

**Q: All my file paths must be absolute — why can't I use relative paths like `data\input.csv`?**
> The Zero-Trust security sandbox enforces absolute paths because relative paths can escape the intended workspace depending on the working directory of the process. Always use full paths: `C:\Data\input.csv` or `/home/etl/data/input.csv`.

**Q: How do I load a very large file (500M rows) without running out of memory?**
> Use `BULK INSERT` — it streams data through the engine in fixed-size batches with O(1) memory usage, never loading the whole file at once:
> ```sql
> BULK INSERT dest_db.Logs
> FROM 'C:\Incoming\huge_file.csv'
> WITH (FORMAT='CSV', FIRSTROW=2, BATCHSIZE=50000, MAXERRORS=10);
> ```
> For an in-memory `SELECT ... INTO #temp`, the entire result set is held in memory. For large datasets, always prefer loading directly into the target database via `BULK INSERT` or chunked `MERGE`.

**Q: `FILE_LIST('C:\Data\*.csv')` isn't working — how do I list files by extension?**
> `FILE_LIST` takes the directory and the glob filter as **two separate arguments**, not a combined glob path:
> ```sql
> -- Wrong:
> DECLARE @files LIST = FILE_LIST('C:\Data\*.csv');
>
> -- Correct:
> DECLARE @files LIST = FILE_LIST('C:\Data', '*.csv');
> ```

---

## Security & Sandbox

**Q: Why can't my script write to another `.etlsql` file?**
> Script Immutability is a Zero-Trust guardrail — the engine cannot write, move, or rename files with logic extensions (`.etlsql`, `.sql`, `.py`, `.js`, `.sh`, `.bat`, `.cmd`). This prevents self-modifying script attacks. Application logic is always human-authored, never engine-generated.

**Q: My script is accessing a shared network drive. Why does it keep throwing `SecurityException`?**
> The sandbox blocks access to drive roots (e.g. `\\server\` directly without a subdirectory), most system directories, and paths containing restricted segments like `.git`, `.ssh`, `.aws`. Ensure your path resolves to a specific subdirectory within an `ApprovedSafeZone`. Contact your ETL-SQL administrator to have the share's UNC path registered as a safe zone.

**Q: I need my script to process more than 100 files. How do I raise the limit?**
> Use `SET ALLOW_FILE_OPERATIONS = <n>` to raise the file operation limit to a specific value, and ensure the target path is within a registered `ApprovedSafeZone`:
> ```sql
> SET ALLOW_FILE_OPERATIONS = 500;
> -- This override only works if the script's working directory is an Approved Safe Zone.
> FOREACH @f IN FILE_LIST('C:\Inbound', '*.csv')
> BEGIN
>     -- process files...
> END
> ```
> Override activations are logged as `Warning`-level audit entries.

**Q: How do I safely test a destructive operation before running it for real?**
> Use `SET WHAT_IF ON` — the engine will process and report on what *would* happen without writing any data:
> ```sql
> SET WHAT_IF ON;
> DELETE FROM prod.Logs WHERE LogDate < '2024-01-01';
> MERGE INTO prod.Customers AS T USING #updates AS S ON T.Id = S.Id
>     WHEN MATCHED THEN UPDATE SET T.Status = 'Archived';
> SET WHAT_IF OFF;
> -- Review the output, then run for real
> ```

---

## Data Lineage & Governance

**Q: How does ETL-SQL track data lineage and tagging?**
> Data lineage tracking is natively built into the engine's query processor:
> * **Pipeline Lineage:** The engine automatically tracks source-to-target dependencies. When a query pulls data from `db_conn.Orders` into `#staging` and then merges it into `warehouse.Sales`, the engine builds a dependency graph.
> * **Metadata Tagging:** You can tag columns or tables with metadata using `INSERT TAG`:
>   ```sql
>   INSERT TAG FOR TABLE #staging COLUMN Email (Sensitive = 'PII', Retention = '7 Years');
>   ```
> Lineage tags automatically flow downstream through `SELECT INTO` and `JOIN` operations.
>
> For more details, see [Lineage.md](../../reference/statements/session-control/lineage.md).

**Q: Can I enforce security policies based on data tags?**
> Yes. The **Governance Core** applies zero-trust policy enforcement at both lint and compile boundaries. If a column is tagged as `PII` or `Restricted`, the linter will block scripts from writing it to insecure destinations (such as SMTP email bodies or raw flat files) unless an explicit audit override is configured.

---

## Performance

**Q: My join across a SQL Server table and a CSV file is very slow. How can I speed it up?**
> When you join a file connector source with a SQL connector source, the engine must pull all rows from both sides into memory and join them in-process. For large SQL-side tables, consider pushing a filter to the SQL side first using a `SELECT INTO #temp` with a `WHERE` clause before the join:
> ```sql
> -- Pre-filter on the SQL side (only pulls matching rows)
> SELECT Id, Name INTO #sql_side FROM prod_db.Customers WHERE Region = 'North';
>
> -- Now join in engine memory — smaller dataset
> SELECT c.Id, c.Name, csv.DiscountCode
> FROM #sql_side AS c
> JOIN csv_conn AS csv ON c.PromoId = csv.Id;
> ```

**Q: How do I find out which statement in my script is the bottleneck?**
> Enable profiling before running your script:
> ```sql
> SET PROFILING ON;
> RUN SCRIPT 'C:\Scripts\my_pipeline.etlsql';
> SET PROFILING OFF;
>
> -- View the 10 slowest statements
> SELECT * INTO #perf FROM eng.profile;
> SELECT * FROM #perf ORDER BY DurationMs DESC LIMIT 10;
> ```

**Q: What is Adaptive Execution and how does it optimize performance?**
> In **v0.15.0**, the engine features a dynamic **Adaptive Execution Controller**. It samples whole-host and process-level resource metrics (CPU %, memory load, disk latency) at every node heartbeat. 
> If the system is under memory pressure, the controller dynamically scales down parallel degree, batch sizes, and prefetch depth. If the system is idle, it dynamically scales them up to maximize throughput.

**Q: How does ETL-SQL process datasets that exceed available physical RAM?**
> The engine relies on an encrypted **Spill-to-Disk** architecture managed by the [MemoryArbiter](../../../src/ETL-SQL.Engine/Engines/MemoryGovernor.cs) and [SpillStore](../../../src/ETL-SQL.Engine/Spill/SpillStore.cs). 
> When active query pipelines (like large sort, join, or aggregation operations) exceed their allocated memory grant, the engine writes intermediate data chunks to encrypted, compressed files on disk (AES-GCM + GZip). Once writing is complete, the engine merges the spilled files in a single pass. This prevents Out of Memory (OOM) crashes and guarantees job completion.

**Q: Why did `PUBLISH BUNDLE` fail on `RUN SCRIPT @path`?**
> Published bundles must know every sub-script at publish time so the Orchestrator can version and store the full dependency graph. Dynamic script paths cannot be packaged safely. Use live mode for those jobs:
>
> ```sql
> CREATE SCHEDULE Daily ON '0 0 * * *' AT TIME ZONE 'UTC';
> CREATE JOB MyJob FOR SCRIPT 'C:\Scripts\my_pipeline.etlsql';
> ALTER JOB MyJob ADD SCHEDULE Daily;
> ```

**Q: Can I recover a script after publishing if I lose the source files?**
> Yes. Use `EXPORT SCRIPT 'orch://bundle@version/main.etlsql' TO 'C:\Recovered\bundle';`. The export recovers script text and relative paths, but it does not decrypt or reveal secrets. Re-enter credentials before running recovered scripts.

---

## Error Handling

**Q: My `CATCH` block runs `ERROR_MESSAGE()` but I also need the line number. How?**
> Use `ERROR_LINE()`, `ERROR_NUMBER()`, and `ERROR_SEVERITY()` — all three are fully implemented and available inside `CATCH` blocks:
> ```sql
> BEGIN CATCH
>     PRINT 'Error at line ' + CAST(ERROR_LINE() AS STRING) + ': ' + ERROR_MESSAGE();
>     PRINT 'Error number: ' + CAST(ERROR_NUMBER() AS STRING) + ', Severity: ' + CAST(ERROR_SEVERITY() AS STRING);
>     THROW;
> END CATCH;
> ```

**Q: How do I raise a custom error that `CATCH` upstream can handle?**
> Use `THROW` — it raises an `ExecutionException` that propagates up through `TRY/CATCH` blocks. Both forms are supported:
> ```sql
> -- Simple form — message only
> IF (SELECT COUNT(*) FROM #staging) = 0
>     THROW 'Staging table is empty — aborting load.';
>
> -- Full form — error number, message, state (T-SQL compatible)
> THROW 50001, 'Staging table is empty — aborting load.', 1;
> ```

---

## Dynamic SQL

**Q: Can I build a SQL string at runtime and execute it?**
> Yes — use `EXEC` with a string expression. ETL-SQL supports two forms:
>
> ```sql
> -- Form 1: Execute locally (runs in engine context — can access #temp tables and @variables)
> DECLARE @sql = 'SELECT COUNT(*) AS Total FROM #staging WHERE Status = ''Active'';';
> EXEC @sql;
>
> -- Form 2: Execute against a remote database (passes SQL verbatim to the remote engine)
> DECLARE @tableName = 'Archive_' + CAST(YEAR(GETDATE()) AS STRING);
> DECLARE @sql = 'SELECT TOP 100 * FROM dbo.' + @tableName + ' ORDER BY Id DESC;';
> EXEC (@sql) AT prod_db;
>
> -- Both forms support INTO to capture results in a temp table
> EXEC (@sql) AT prod_db INTO #results;
> SELECT * FROM #results;
> ```
>
> Dynamic SQL is also parameterized — pass a `List` as a second argument for safe parameterized queries against remote databases:
> ```sql
> DECLARE @params LIST = ('Active', 2026);
> EXEC ('SELECT * FROM dbo.Orders WHERE Status = @p0 AND Year = @p1') AT prod_db WITH (@params);
> ```

---

## Reporting (.rptsql)

**Q: What's the difference between `.etlsql` and `.rptsql`?**
> `.etlsql` files are data pipeline scripts — they move, transform, and load data. `.rptsql` files are Report-SQL scripts — they define dashboards using `CREATE DATASET`, `CREATE VISUAL`, `CREATE PAGE`, and `CREATE NAVIGATION`. The Report-SQL language is a superset of ETL-SQL syntax.

**Q: How do I preview a report without serving it over HTTP?**
> Use the VS Code extension's **Preview Report** button for an interactive local preview. To build static output without starting a preview server, use the report CLI:
> ```bash
> etl-sql-report build MyReport.rptsql --format md
> etl-sql-report build MyReport.rptsql --format pdf
> ```

---

*Not finding your answer here? Open a [GitHub Discussion](https://github.com/etl-sql/ETL-SQL/discussions) or check [Getting Started](../onboarding/getting-started.md) and the [Cookbook](../../cookbooks/etl/README.md).*
