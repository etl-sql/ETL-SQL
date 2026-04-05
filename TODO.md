
## Engine Enhancements
NOTE: CREATE A NEW GIT BRANCH BEFORE STARTING THIS WORK
1. This works amazing running one script at a time.  In the real world there will be multiple scripts running at a time.  I am starting to think we need a separation of responsibilities.  
  - TUI: This is the console interface which is now ui edit.  This is the full featured editor.  
  
  - VS Code Extension: This is the same as the TUI but with the VS Code interface.  Both should have the same features.  VS Code will have better visualizations and should be the preferred interface for most users.  The TUI is good for quick edits and running scripts or troubleshooting on server.  But if someone prefers the TUI they can use it.  
  
  - Script Executer: This runs the scripts.  It manages everything around the script execution.  This is ETL-SQL.exe does right now.  It is a command line tool that can be run by itself or called by the Orchestrator (NEW).
        I am rethinking these needs do we need this complexity?  Just let them die and rerun them.
        ** Do not include for now until a need arises **
       - Need a way to pause a job at a certain point and resume it later.
       - Need a way to resume a job after a failure.
       - Need a way to resume a job after a system restart.
       - Need a way to resume a job after a network failure.
       ** TUI should be removed from this exe and made its own exe for keeping this lean? **
       - Has the TUI as a frontend, and a simple ui for basic load script and run.
         - Should TUI be removed from this exe and made its own exe for keeping this lean?

  - Orchestrator(NEW): This is the brains of the operation.  
     - It will be responsible for running scripts in parallel and managing the results.  It will need to spin up multiple script executers to run the scripts in parallel.  
     - It will need to keep track of workstation resources so it doesn't overwhelm the system.  
        - May main idea of being able to pause and resume jobs was so the orchestrator could adjust to system resources as needed but now I'm thinking it will work itself out.  Thinking Orchestrator will just spin up script executers as needed and let them die when they are done or kill them if there is too many running and system is getting bogged down and retry when resources are available.
     - Owns the JOBS and the JOBS HISTORY.
     - Job HIstory is currently being maintained by a SQL lite db in ETL-SQL this will need to be moved to the Orchestrator.
     - Responsible for running the jobs that have been scheduled.    
     - Meant to be always up and running.  A service

#### Item 1 — Separation of Responsibilities (TUI / VSCode / Script Executor / Orchestrator)
*** HOLD DO NOT IMPLEMENT ***
**Strong Agree.** This is the right direction and it's a well-known pattern in data engineering (think: Airflow, Azure Data Factory). Here's my honest take:

**What's good:** The separation is clean and logical. Script Executor as a standalone is a very good idea — it lets you deploy it as a daemon/service, call it from CI/CD pipelines, and keeps it lean.

**What you need to think about:**
- **Serialization:** For pause/resume and failure recovery the Script Executor needs to be able to serialize its full state (variables, data source positions, current statement index) to disk. This is non-trivial if you're mid-loop or mid-transaction. Consider checkpoints at statement boundaries only.
- **Job state store:** The Orchestrator owning job history implies a database (SQLite at minimum for local, SQL Server for enterprise). This is a significant dependency.
- **Communication protocol:** How does the Orchestrator talk to Script Executors? Process spawning is simplest. gRPC or named pipes would be more enterprise-grade. Named pipes on Windows are easiest with low overhead.
- **Should TUI be separate?** Yes, I'd remove it from the Script Executor exe. Keep the Script Executor a pure headless CLI. The TUI as a separate exe makes it optional and keeps both binaries lean.

**Risk:** This is a medium-large refactor. Create the branch, extract `IScriptExecutor` interface first, then implement it in its own project. Don't rewrite — extract.

---
2. What if two massive 50 Million row tables are joined together?  This will create a 2.5 Quadrillion row table.  This will not work.  We need to be able to handle this. 
#### Item 2 — Joining two 50M row tables (2.5 Quadrillion row problem)

**This is the most critical scalability concern you have.** 50M × 50M is not a query — it's a catastrophe. Here's the reality:

**What you're doing now:** In-memory joins. At 50M rows each with even minimal data (100 bytes/row), one table is 5GB. You can't buffer that.

**What you need:**
1. **Sort-Merge Join:** Sort both sides on the join key first (can be done in streaming passes with external sort), then merge — O(n log n) rather than O(n²). This is what databases do.
2. **Hash Join with partitioned spill-to-disk:** Build a hash table from the smaller side, partition and spill overflow to temp files. This is what ETL tools do.
3. **Push the join down:** If both tables are on the same connection (same SQL Server, same Postgres), compile the whole JOIN to SQL and let the DB do it. You already have pushdown logic — extend it to cover JOINs.

**Practical recommendation:** For now, invest in **SQL pushdown for same-connection JOINs** first. This is the 80% case and requires minimal new code. Then add hash join with spill-to-disk for cross-connection scenarios. The external join engine (`ExternalJoinEngine.cs`) you already have — make sure it's actually being used for large joins.

---

#### Item 3 — Cluster / Distributed Execution
*** HOLD DO NOT IMPLEMENT ***
**This is a big vision and absolutely achievable, but needs careful thought:**

**What distributed ETL means:** You need to partition data across nodes (sharding by key range or hash), send partial results to each node, then gather/aggregate. This is essentially what Spark, Flink, and SSIS all do.

**Practical path forward:**
1. **Phase 1 (Near term):** The Orchestrator from Item 1 running multiple Script Executors on the **same machine** in parallel with resource management. This alone will 10x throughput for CPU-bound work.
2. **Phase 2 (Medium term):** Remote Script Executors on other machines. Each machine runs the ETL-SQL.exe Script Executor as a service. The Orchestrator sends job definitions (serialized scripts + parameters) via gRPC/HTTP. Results stream back.
3. **Phase 3 (Long term):** Data partitioning. For a JOIN across two 50M row tables on different systems, the Orchestrator assigns ranges to different nodes: Node 1 handles rows 1–12.5M, Node 2 handles 12.5M–25M, etc. This requires partitionable data sources.

**The honest truth:** Phase 3 is a 6-12 month project and essentially means building a simplified Spark. Make sure there's a real use case driving it before going there. Phase 1 and 2 will handle 95% of real enterprise scenarios.

---

 ## Bugs
 
---

## Code Quality & Technical Debt

### SRP Violations
**CQ-1.** **`Evaluator.cs`** (~673 LOC) holds too many responsibilities: procedure/function execution, pipeline utilities, metrics dispatch, and output capture all live inline alongside the core statement-dispatch loop.
   - [x] Extract `EvaluateUserDefinedFunction` + `EvaluateProcedure` → `Services/ProcedureExecutor.cs`
   - [x] Extract `AlignColumns` + `InterceptProgress` + `EvaluateForClause` → `Services/BatchPipelineHelper.cs`
   - [ ] **HOLD** Extract `IScriptExecutor` interface once Orchestrator work begins (see Engine Enhancements Item 1).

**CQ-2.** **`TextDocumentHandler.cs`** (940 LOC) — Handles text sync, hover, definition, completion (364+ lines), signature help, formatting, metadata discovery, connection registration, lineage, linting, and diagnostic publishing. Should be split into: `AnalysisCoordinator`, `CompletionHandler`, `HoverProvider`, `DefinitionProvider`.

**CQ-3.** **`Ast.cs`** (1975 LOC) — Defines 200+ AST node classes AND implements `ToSql()` serialization, `GetSourceTables()`, `GetSourceColumns()`, visitor logic, and SQL formatting inline on every node. Extract `ToSql()` to a separate `SqlFormatter` visitor.

**CQ-4.** **`StatementParser.cs`** (1904 LOC) — Parses all 40+ statement types in one class. Each statement family should have its own partial class or sub-parser.

**CQ-5.** **`ConsoleEditor.cs`** (~423 LOC) mixes rendering, input handling, and file I/O. Consider separating `EditorRenderer` from orchestration.

### Logging Gaps
**CQ-17.** **No structured logging** — `Logger.Verbose(string)` discards context. Move to structured Serilog calls for queryable logs.
**CQ-18.** **No correlation/session IDs** — Parallel script runs produce interleaved log lines with no attribution. Add a per-`Evaluator` `SessionId` attached to every log line.

### Testing Gaps
**CQ-19.** **No concurrent Evaluator tests** — Add tests that `Fork()` two evaluators simultaneously and verify no state leakage.
**CQ-20.** **No spill-to-disk activation tests** — `ExternalJoinEngine` is untested at threshold boundaries.
**CQ-21.** **Unknown coverage %** — Add Coverlet + ReportGenerator to CI.

### Minor Issues
**CQ-22.** [x] `ExternalJoinEngine` hardcoded partition count — exposed as `private const int PARTITION_COUNT = 32`.
**CQ-23.** [x] `SubqueryCache` can grow unbounded — Fixed: `ExpressionEvaluator` only caches when `Count < 1000`.
**CQ-24.** No retry logic for transient DB failures in connectors — consider Polly.
**CQ-25.** [x] Temp-file cleanup pattern duplicated across 5+ connector files — Fixed: extracted to `TempFileHelper.SafeDelete(path)` (see CQ-G).

---

## Syntax Additions and Improvements

1. [x] USE PASSWORD = 'password';
  As the user types the password it should be ******** shown instead of the actual characters.  This is the password required to open the script and is the password used in encrypting the connection strings.  This is not the password for decryption of files, password for SFTP/FTP, or any other connection with an explicit password.  This just encrypts the sensitive data in the script file.

2. [x] To see password the user must type SET SHOW_PASSWORD ON; and then USE PASSWORD = 'password'; and it will be written in plain text. This is the password required to open the script and is the password used in encrypting the connection strings.  SET SHOW_PASSWORD OFF; will turn off the password visibility, this is the default.

3. **DO NOT IMPLEMENT THIS YET**  SET WHAT_IF ON;  This statement will not write anything back to databases, files or any other destination.  It will not send files, create files, directories, or anything else.  It will just run the script and show the user what it would do by writing this out to the messages tab and the results tab.  This is useful for testing scripts before running them in production.  SET WHAT_IF OFF; will turn off the what if mode, this is default.

4. **DO NOT IMPLEMENT THIS YET** CREATE SSH_KEY_PAIR(<bits>, <password>, <path>);  This will create a public/private key pair for encrypting and decrypting data.  The password is used to encrypt the private key file.  The bits is the number of bits to use for the key pair.  The path is the path to the directory where the key pair will be stored.  The default path is the same directory as the script file.
---

5. Need to add GROUP BY GROUPING SETS, ROLLUP, and CUBE.  Need to update the Docs\ETL_SQL_Language_Reference.md to reflect the changes.

6. File Encryption FLATFILE, EXCEL, JSON, XML, PARQUET, AVRO.  Need to add ENCRYPTION algorithm which would be the same as the HASHBYTES funtion has.  Also need to add the ability to encrypt with a SSH key pair and passphrase.  For LINTING when the user has ENCRYPT=ON then they must have a password or a SSH key pair and passphrase.  If they don't have one then linting should fail.

I added these to the documentation already for FLATFILE:
- **ALGORITHM**: `MD5`, `SHA1`, `SHA2_256`, `SHA2_512` — algorithm to use for encryption/decryption. (Default: `SHA2_256`)
- **KEYFILE**: Path to the private key file for public-key authentication. (Required if ENCRYPT=ON)
- **PASSPHRASE**: The passphrase for the private key file (if any). (Required if ENCRYPT=ON)
Please update ETL_SQL_Language_Reference.md with these options for the other file types as well after implementation.

7. Allow CREATE CONNECTION to build a connection string based on options or use a fully completed connection string like we currently have.  CREATE CONNECTION MSSQL(<server>) WITH(USER='user', PASSWORD='password', DATABASE='database');  This would build the connection string for you.  The connection string would be built in the same way as the other connection types.

DATABASE: The name of the specific database (e.g., Database=myDataBase).
TRUSTED_CONNECTION: Set to True if using your Windows login (no password needed).
USER_ID: Your SQL username (if not using Windows login).
PASSWORD: Your SQL password (if not using Windows login).
USE_SSL: Set to True (standard for modern security).
TRUST_SERVER_CERTIFICATE: Set to True (common for local testing to avoid SSL errors).

LINTER should check if both TRUSTED_CONNECTION and USER_ID/PASSWORD are set.  If both are set then linting should fail.

We'll need to do the same for ORACLE and POSTGRES as well.  Please update ETL_SQL_Language_Reference.md with the options for MSSQL,ORACLE and POSTGRES.

8. Double check ALTER CONNECTION syntax and implementation.  I don't think we have that yet.  I think we only have CREATE CONNECTION and DROP CONNECTION.  We should also have CREATE OR ALTER CONNECTION.  ALTER CONNECTION can anything like connection string or path or just add/remove/change options.  CREATE OR ALTER CONNECTION will create if the connection doesn't exist or alter it if it does.  But it must contain everything.  If the connection exists it will be rebuilt with what was provided and does not hang on to any previous options.

9. In Docs\ETL_SQL_Language_Reference.md the section Supported Data Types should break down each data type so the user knows how they are used.

10. Need to add the more advanced LIKE options and update the Docs\ETL_SQL_Language_Reference.md to reflect the changes.


## VS CODE Bugs/Improvements
1. [x] LINEAGE gets lost when using a EXECUTE block.  Well need to read the pushdown SQL statement as best we can for the hover LINEAGE and then capture the actual LINEAGE after execute when we know exactly what came back from the PUSHDOWN.  The goal would be to track this back and say id came from MSSQL dbo.Employee's table.

2. [x] Running a script ad-hoc with run selection does now work. Lets develop a test where we run piece by piece.

### Analysis: Why This Test Won't Work (Review Only)

The test script exercises a "session-on-disk" pattern: run chunks of SQL one at a time, each in a **new process**, but expect the in-memory state created by earlier chunks (Docker containers, connections named `m`, temp table `#emp`, variables like `@conn`) to still exist in later chunks. This is fundamentally at odds with the current architecture. Here are the specific failure points:

---

#### Bug A: `@conn` Variable Is Lost Between Runs (Every `FROM 2nd` Run Onward)

**Code location:** `EngineRunner.cs` line 178 — `await using var evaluator = ...`

Each "Run Selection" spawns a fresh OS process (`cp.spawn()` in `extension.ts` line 298). The `Evaluator` is obtained from DI as **Transient** (`DependencyInjectionSetup.cs` line 116), meaning each process gets a completely empty variable scope. When the 2nd run tries to use `EXECUTE m ...`, the variable `m` (the connection name) was never declared — it was declared as `@conn` in run 1, which is now dead.

**Worse:** The 12th run tries `CREATE CONNECTION m ON MSSQL(@conn)` where `@conn` is the variable that stored the Docker connection string. `@conn` doesn't exist in that new process, so this will throw `Undeclared: @conn`.

**Fix approach:** A `USE SETS` or environment-variable mechanism would need to persist critical variables across runs. Alternatively the 12th run would need to re-declare `@conn` without Docker (from, say, `DECLARE @conn varchar(500) = 'Server=localhost,...'`).

---

#### Bug B: The Named Connection `m` Is Lost Between Runs

**Code location:** `Evaluator._connections` is an in-memory `ConcurrentDictionary` on the `Evaluator` instance.

Connections (`CREATE CONNECTION m ON MSSQL(...)`) are stored entirely in the `Evaluator._connections` dictionary which lives only for the lifetime of that one process execution. The 2nd run (`EXECUTE m BEGIN...END`) launches a new process with an empty connections dictionary — `m` does not exist, so this throws `Connection not found: m`.

**Fix approach:** A persistent connection registry (SQLite or a temp JSON file per session) written by `CREATE CONNECTION` and read at startup could bridge this. The VS Code extension already has a `ConnectionsProvider` that stores named connections, but those are for the language server hover only — not injected into the engine at run time.

---

#### Bug C: Temp Table `#emp` Is Lost Between Runs

**Code location:** `Evaluator._connections` — temp tables are also stored here as `InMemoryDataSource` entries under their `#name` key.

The 4th run (`SELECT * FROM #emp`) and every subsequent run that references `#emp` will fail with a table-not-found error because the in-memory data source is gone when the process exits. The 10th run (`SELECT * FROM #emp` after Docker is closed) is specifically designed to prove that `#emp` *survives* even after Docker goes away — but it won't, because `#emp` only ever lived in that run's memory.

**Fix approach:** Temp tables that need to survive across selections would need to be serialized to disk (e.g., parquet or CSV) and re-loaded at startup. A `--session-file` flag could point the engine at a folder of serialized temp tables.

---

#### Bug D: Docker Container State Is Partially Handled (But `LastConnectionString` Is Still Lost)

**Code location:** `DockerContainerManager.cs` — `_activeContainers` and `_connectionStrings` are `static` fields (lines 20-21).

This is actually the *best* case: because these dictionaries are `static`, if the Docker container is still running and the *same process image* is used (which it is, since `ETL-SQL.exe` is spawned each time), the `GetExistingContainerConnectionString` fallback (line 120) can re-attach to the container by looking it up via the Docker API by its deterministic name (`etlsql_mssql_server_2022-latest`).

**However:** `LastConnectionString` is an **instance** field (line 22), not static. So `DOCKER.CONNECTION_STRING` will return `null` in any new process even if the container is still running. The 13th run (`USE DOCKER(...)`) will try to *start a new container* with the same name, hit a "name already in use" conflict, and will fall back to `GetExistingContainerConnectionString`. This fallback *should work* but adds latency and creates a misleading log message ("Docker container created" vs. "Re-attached to existing").

**Fix approach:** Either make `LastConnectionString` also static, or better, have `DOCKER.CONNECTION_STRING` always call `GetConnectionString("DOCKER")` which does use the static dictionary (it does for named aliases but the bare `DOCKER` fallback hits `LastConnectionString` first — see `ExpressionEvaluator.cs` line 324-325).

---

#### Bug E: `EXECUTE m SELECT * INTO #emp FROM m.dbo.Employee` Is Ambiguous

**Code location:** `StatementParser.cs` line 512-519 — "Single statement remote execution: EXECUTE c CREATE TABLE..."

The 3rd run uses `EXECUTE m` followed by a `SELECT ... INTO #emp` statement. The parser in `ParseExecute()` detects that the next token (`SELECT`) is a statement-start token, wraps it in an `ExecuteRemoteBlockStatement`, and sends it to `ExecuteRemoteBlockStatementHandler`. That handler (line 30) calls `context.CompileQuery(innerStmt, db.Dialect)` and executes the compiled SQL on the remote connection.

The `SELECT * INTO #emp FROM m.dbo.Employee` will compile to something like `SELECT * INTO #emp FROM m.dbo.Employee` in MSSQL dialect — but `#emp` is a MSSQL **local temp table** on the remote SQL Server, not the ETL-SQL in-memory table. The result goes nowhere in ETL-SQL's local `_connections` dictionary. So `SELECT * FROM #emp` in run 4 will fail with "table #emp not found."

**Fix approach:** `EXECUTE m SELECT * INTO #emp FROM ...` should be interpreted as: run a SELECT on `m` and stuff the results into a *local* ETL-SQL temp table named `#emp`. This path needs to be parsed as an `ExecutePushdownStatement` with `IntoTable = #emp`, not as a remote `ExecuteRemoteBlockStatement`. The parser would need a lookahead to detect the `INTO #temp` pattern before deciding how to route it.

---

#### Bug F: `SELECT * FROM m.dbo.Employee` After Docker Close Will Fail for the Right Reason, But With a Bad Error Message

**Code location:** `DataSourceManager.cs` (handles cross-connection `conn.table` resolution) / `Evaluator.DisposeAsync()` line 601 — `_connections.Clear()`.

When `DOCKER CLOSE; DROP CONNECTION m;` runs (run 9), `DockerContainerManager.CloseContainers()` stops and disposes the container and clears the static dictionaries. Then run 11's `SELECT * FROM m.dbo.Employee` should fail with a meaningful error. The error message comes from the connection not being in `_connections`, which is correct — but the message may read as "table not found" or "connection not found" depending on how `DataSourceManager` resolves `m.dbo.Employee`.

**Fix approach:** Ensure `DataSourceManager.ResolveDataSourceAsync` emits a clear "Connection 'm' does not exist" error rather than a generic table-not-found.

---

### Summary of What Needs to Change

| Bug | Root Cause | Required Fix |
|-----|-----------|-------------|
| A | Variables are in-process only | Persist key variables across runs via a session file or `USE SETS` |
| B | Named connections are in-process only | Persist connection definitions across runs (write/read from disk) |
| C | Temp tables are in-process only | Serialize temp tables to disk between runs |
| D | `LastConnectionString` is an instance field | Make it resolve from the static dictionary instead |
| E | `EXECUTE m SELECT INTO #emp` routes incorrectly | Parser lookahead to distinguish local-INTO from remote-INTO |
| F | Disconnect error message is unclear | Improve error message in `DataSourceManager` for missing connections |


1st run this:
```sql
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');
DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
CREATE CONNECTION m ON MSSQL(@conn);
```
This should create the Docker container of MSSQL and create a connection to it named m.
Messages should give us feedback saying Docker container created and connection m created.

2nd run this:
```sql
EXECUTE m
BEGIN
    CREATE TABLE dbo.Employee (
        id INT PRIMARY KEY,
        [name] NVARCHAR(100)
    );
    INSERT INTO dbo.Employee (id, [name]) VALUES (1, 'Alice'), (2, 'Bob'), (3, 'John');   
END
```
This should create the table dbo.Employee in the Docker container and insert 3 rows into it.  Messages should give us feedback saying table dbo.Employee created and 3 rows inserted.


3rd run this:
```sql
EXECUTE m
SELECT * INTO #emp FROM m.dbo.Employee;
```
This should create the temp table #emp in the Docker container and insert 3 rows into it.  Messages should give us feedback saying temp table #emp created and 3 rows inserted.

4th run this:
```sql
SELECT * FROM #emp;
```
This should return the 3 rows from the temp table #emp in the results tab.

5th run this:
```sql
SELECT * FROM m.dbo.Employee;
```
This should return the 3 rows from the table ms sql Docker container table dbo.Employee in the results tab.

6th run this:
```sql
LINEAGE(#emp, id); -- should include dbo.Employee.id
```
This should return the lineage of the temp table #emp id column with rows showing it came from dbo.Employee.id and next went to #emp.id

7th run this:
```sql
LINEAGE(#emp, name); -- should include dbo.Employee.name
```
This should return the lineage of the temp table #emp name column with rows showing it came from dbo.Employee.name and next went to #emp.name

8th run this:
```sql
SELECT * FROM #emp;
```
This should return the 3 rows from the temp table #emp in the results tab.

9th run this:
```sql
DOCKER CLOSE;
DROP CONNECTION m;
```
This should close the Docker container and drop the connection m.  Messages should give us feedback saying Docker container closed and connection m dropped.

10th run this:
```sql
SELECT * FROM #emp;
```
This should return the 3 rows from the temp table #emp in the results tab.

11th run this:
```sql
SELECT * FROM m.dbo.Employee; -- Should fail gracefully saying connection m does not exist
```
This should fail gracefully saying connection m does not exist.

12th run this:
```sql
CREATE CONNECTION m ON MSSQL(@conn); -- Should fail gracefully saying DOCKER is closed
```
This should fail gracefully saying DOCKER is closed.

13th run this:
```sql
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');
DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
CREATE CONNECTION m ON MSSQL(@conn);
```
This should create the Docker container of MSSQL and create a connection to it named m.
Messages should give us feedback saying Docker container created and connection m created.

14th run this:
```sql
EXECUTE m
BEGIN
    CREATE TABLE dbo.Employee (
        id INT PRIMARY KEY,
        [name] NVARCHAR(100)
    );
    INSERT INTO dbo.Employee (id, [name]) VALUES (1, 'Mike'), (2, 'Steve'), (3, 'Angus');   
END
```
This should create the table dbo.Employee in the Docker container and insert 3 rows into it.  Messages should give us feedback saying table dbo.Employee created and 3 rows inserted.  There should not already be a table dbo.Employee in the Docker container.  Since it was closed this should be a clean slate.

15th run this:
```sql
SELECT * FROM #emp WHERE id = 1; -- Should return Alice, not Mike since we haven't updated the temp table yet
```
This should return 1 row with id = 1 and name = Alice.  This should not return Mike since we haven't updated the temp table yet.

16th run this:
```sql
SELECT * INTO #emp FROM m.dbo.Employee;
SELECT * FROM #emp WHERE id = 1; -- Should return Mike, not Alice since we updated the temp table
```
This should return 1 row with id = 1 and name = Mike.  This should return Mike since we updated the temp table.

17th run this:
```sql
LINEAGE(#emp, id); -- should include dbo.Employee.id
```
This should return the lineage of the temp table #emp id column with rows showing it came from dbo.Employee.id and next went to #emp.id

18th run this:
```sql
DOCKER CLOSE;
DROP CONNECTION m;
```
This should close the Docker container and drop the connection m.  Messages should give us feedback saying Docker container closed and connection m dropped.
