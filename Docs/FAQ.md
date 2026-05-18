# ETL-SQL FAQ & Troubleshooting Guide

Common questions, gotchas, and their solutions. If you're stuck, start here.

---

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
> Use `SHOW VERSION;` to display version info from within a script session. The current release baseline is **v0.7.0**.

**Q: Where do I start?**
> Read the [User Manual](User_Manual.md) first — it explains the pipeline mental model that everything else builds on. Then work through the [Cookbook](Cookbook.md) for production-ready examples.

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

**Q: What's the difference between `SEND EMAIL` and `SEND_EMAIL`?**
> They do the same thing. ETL-SQL supports two syntax styles for most operations:
> - **SQL style** (preferred): `SEND EMAIL TO '...' FROM '...' SUBJECT '...' BODY '...' AT conn;`
> - **Function style**: `SEND_EMAIL(conn, 'to', 'from', 'subject', 'body');`
>
> SQL style is preferred in new scripts — it's more readable and closer to natural language.  Function style is available to those who feel more comfortable with this style.

**Q: Can I use `WAITFOR (SELECT ...)` to poll until a condition is true?**
> Yes — the `WAITFOR (condition)` form is supported. The engine evaluates the expression repeatedly at a 200ms interval and continues execution as soon as the result is truthy (non-zero, non-empty, or `true`):
> ```sql
> -- Polls every 200ms until the condition returns a non-zero count
> WAITFOR (SELECT COUNT(*) FROM control_db.JobStatus WHERE Status = 'Ready');
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

**Q: Can I use MySQL as a connector?**
> MySQL is not a natively supported connector type. Use `ODBC` with a MySQL ODBC driver instead:
> ```sql
> CREATE CONNECTION mysql_src ON ODBC()
>     WITH(DSN='MySQL_DSN', USER='etl', PASSWORD='...');
> -- or with a driver string
> CREATE CONNECTION mysql_src ON ODBC()
>     WITH(CONNECTION_STRING='Driver={MySQL ODBC 9.0 Driver};Server=host;Database=mydb;User=etl;Password=pwd;');
> ```

**Q: My script uses `PIVOT`. Will it work?**
> Yes, PIVOT/UNPIVOT has been implemented in the engine.

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
> CREATE CONNECTION db ON MSSQL('ENC:U2FsdGVkX1+abc123...');
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
> CREATE CONNECTION db ON MSSQL() WITH(SERVER=@server, DATABASE='Sales', PASSWORD=@pwd);
> ```

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
> Add the override flag as a comment at the top of your script, and ensure the target path is within a registered `ApprovedSafeZone`:
> ```sql
> ### ALLOW_GREATER_THAN_100_FILE
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

## Performance

**Q: My join across a SQL Server table and a CSV file is very slow. How can I speed it up?**
> When you join a file connector source with a SQL connector source, the engine must pull all rows from both sides into memory and join them in-process. For large SQL-side tables, consider pushing a filter to the SQL side first using a `SELECT INTO #temp` with a `WHERE` clause before the join:
> ```sql
> -- Pre-filter on the SQL side (only pulls matching rows)
> SELECT Id, Name FROM prod_db.Customers WHERE Region = 'North' INTO #sql_side;
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
> SHOW PROFILE INTO #perf;
> SELECT * FROM #perf ORDER BY DurationMs DESC LIMIT 10;
> ```

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
> EXEC @sql ON prod_db;
>
> -- Both forms support INTO to capture results in a temp table
> EXEC @sql ON prod_db INTO #results;
> SELECT * FROM #results;
> ```
>
> Dynamic SQL is also parameterized — pass a `List` as a second argument for safe parameterized queries against remote databases:
> ```sql
> DECLARE @params LIST = ('Active', 2026);
> EXEC 'SELECT * FROM dbo.Orders WHERE Status = @p0 AND Year = @p1' ON prod_db WITH PARAMS(@params);
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

*Not finding your answer here? Open a [GitHub Discussion](https://github.com/AmericanSuperstar/ETL-SQL/discussions) or check the [User Manual](User_Manual.md) and [Cookbook](Cookbook.md).*
