
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


---

## Doc Review: README.md vs ETL_SQL_Language_Reference.md

Items below were found by comparing both documents. These are things present in one document but missing or inconsistent with the other.

### ❌ README Features Not Documented in Language Reference

**DR-1. Console Editor (`ui edit`) Command** — *Pending*  
The README describes a full terminal-based editor launched via `dotnet run -- --ui edit MyScript.etlsql`, including shortcuts like `F5`, `Shift+F5`, `Ctrl+I`, `F1`. The language reference has no section for how to launch or use the console editor. Consider adding a "Getting Started / Running Scripts" section to the reference doc.

**DR-2. VS Code Extension** — *Pending*  
The README mentions a dedicated VS Code language server extension. The language reference has no mention of it at all — not even a pointer to where to install it.

**[x] DR-3. `SEND_EMAIL` Syntax**  
Added a full dedicated `SEND_EMAIL` section to the language reference with all clauses (`CC`, `BCC`, `ATTACH`, `AT`), full syntax block, and a complete example.

**[x] DR-4. `RECEIVE_FILE` Command**  
Added `SEND_FILE` and `RECEIVE_FILE` syntax with examples in a new "File Transfer" section in the language reference.

**[x] DR-5. `CREATE JOB` Full Syntax**  
Replaced the one-liner with a full `CREATE JOB` section covering `ON SCHEDULE EVERY <n> SECONDS/MINUTES/HOURS/DAYS [AT 'HH:MM'] AS <stmt>` syntax. Added `DROP JOB` and examples. Moved `SHOW JOB HISTORY` next to it.

**[x] DR-6. `LINEAGE()` in README**  
Updated README Observability bullet to include a `LINEAGE(#result) TO 'report.md';` example.

**DR-7. Native SQL Pushdown Guide** — *Pending — needs content decision*  
README claims automatic pushdown of joins/filters to source databases. The language reference has no guide explaining when pushdown is triggered, how to force it, or how to prevent it. The `EXECUTE...BEGIN...END` walkthrough touches it informally but there's no clear guide. Add a "Performance / Pushdown" section explaining the rules.

---

### ⚠️ Language Reference Items That Should Be Reviewed for Accuracy

**[x] DR-8. `SEND_FILE` / `RECEIVE_FILE` / `REMOTE_FILE_LIST` Syntax**  
Replaced loose `GET_FILE`/`PUT_FILE` examples with proper `SEND_FILE`, `RECEIVE_FILE` sections. Added `REMOTE_FILE_LIST(conn, path)` as a documented table-valued function with examples.

**[x] DR-9. `WAITFOR TIME` Variant**  
Verified: only `WAITFOR DELAY` is implemented in the parser. Removed `WAITFOR TIME` from the docs and added a `[!NOTE]` callout explaining the limitation.

**[x] DR-10. `FOR`/`FOREACH` Loop — README Automation Table**  
Added `CREATE SETS` and `USE SETS` to the README command tables (Scripting row).

**[x] DR-11. `USE SETS` / `CREATE SETS` — Not in README**  
Added a Scripting row to the README command table covering `CREATE PROCEDURE`, `CREATE FUNCTION`, `CREATE SETS`, and `USE SETS`.

**[x] DR-12. `LINT` Statement in README**  
Updated README Observability bullet to include `LINT 'script.etlsql';` syntax.

**[x] DR-13. `HELP CONNECTION <type>` in Language Reference**  
Expanded into a full `HELP CONNECTION <type>` section with examples under Utility Commands.

**DR-14. Encryption Options (ALGORITHM/KEYFILE/PASSPHRASE) for EXCEL/JSON/XML** — *Pending implementation (see TODO item 6)*  
Once the encryption options are implemented for non-FLATFILE connectors, add `ALGORITHM`, `KEYFILE`, and `PASSPHRASE` to EXCEL, JSON, XML connector docs.

**[x] DR-15. `MOCKDB` Connector**  
Decision: **Document it.** Added a full `MOCKDB` section to the language reference between ORACLE and PARQUET connectors. Includes: syntax, pre-populated table reference table, behavior notes, `[!NOTE]` "testing only" callout, and two complete usage examples (`SELECT` with JOIN and `EXECUTE...BEGIN...END`).

**[x] DR-16. `REMOTE_FILE_LIST` Syntax**  
Documented as a table-valued function `REMOTE_FILE_LIST(conn_name [, 'path'])` with SELECT examples in the File & Directory Introspection section.

**[x] DR-17. `SHOW COLUMNS` Syntax**  
Verified parser does not support SHOW COLUMNS as a batch SQL statement (only console/VSCode UI). Added a `[!NOTE]` callout to the Introspection section and corrected the syntax to `SHOW COLUMNS <connection>.<table>`.

**[x] DR-18. `EXPLAIN` Example**  
Expanded `EXPLAIN` from a one-liner to a full section with a multi-table JOIN example.

**[x] DR-19. `USE PASSWORD` Statement**  
Added a full `USE PASSWORD` section under Variable Management with syntax, behavior explanation, and example.

**[x] DR-20. `SET SHOW_PASSWORD ON/OFF`**  
Added `SET SHOW_PASSWORD` section next to `USE PASSWORD` with syntax and example.
