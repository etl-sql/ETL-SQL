
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

### Critical Bugs (Production Risk)
**CQ-A.** [x] **`WindowEngine.cs` — 13 `.Result` blocking calls in async sort predicates (deadlock risk)** — Fixed: sort keys pre-evaluated into `List<(Row, object?[])>` before sorting; no `.Result` calls remain.

**CQ-B.** [x] **`SchedulerService.cs:35` — Fire-and-forget `Task.Run` without error handling** — Fixed: task stored and `.ContinueWith(OnlyOnFaulted)` logs unhandled exceptions.

### Silent Exception Swallowing (23+ instances)
**CQ-C.** [x] **`SuggestionProviders.cs`** — Empty `catch {}` blocks in autocomplete. Fixed: no empty catches remain.

**CQ-D.** [x] **`ETLSuggestEngine.cs:254`** — Empty `catch {}` in suggestion logic. Fixed: no empty catches remain.

**CQ-E.** [x] **`ConsoleEditor.cs:100`** — Empty `catch {}` swallows `Console.Clear()` errors. Fixed: logs at Verbose.

**CQ-F.** [x] **`EvaluationUtils.cs:28`** — Empty `catch {}` swallows unspecified core exception. Fixed: logs at Verbose.

**CQ-G.** [x] **Connector temp-file cleanup** — Fixed: all 10 locations across FlatFile/Excel/Json/Xml connectors use `TempFileHelper.SafeDelete(path)`.

**CQ-H.** [x] **`OracleConnector.cs:65`** — Empty `catch {}` swallows connection error. Fixed: logs at Verbose.

### SRP Violations
**CQ-1.** **`Evaluator.cs`** (~673 LOC) holds too many responsibilities: procedure/function execution, pipeline utilities, metrics dispatch, and output capture all live inline alongside the core statement-dispatch loop.
   - [x] Extract `EvaluateUserDefinedFunction` + `EvaluateProcedure` → `Services/ProcedureExecutor.cs`
   - [x] Extract `AlignColumns` + `InterceptProgress` + `EvaluateForClause` → `Services/BatchPipelineHelper.cs`
   - [ ] **HOLD** Extract `IScriptExecutor` interface once Orchestrator work begins (see Engine Enhancements Item 1).

**CQ-2.** **`TextDocumentHandler.cs`** (940 LOC) — Handles text sync, hover, definition, completion (364+ lines), signature help, formatting, metadata discovery, connection registration, lineage, linting, and diagnostic publishing. Should be split into: `AnalysisCoordinator`, `CompletionHandler`, `HoverProvider`, `DefinitionProvider`.

**CQ-3.** **`Ast.cs`** (1975 LOC) — Defines 200+ AST node classes AND implements `ToSql()` serialization, `GetSourceTables()`, `GetSourceColumns()`, visitor logic, and SQL formatting inline on every node. Extract `ToSql()` to a separate `SqlFormatter` visitor.

**CQ-4.** **`StatementParser.cs`** (1904 LOC) — Parses all 40+ statement types in one class. Each statement family should have its own partial class or sub-parser.

**CQ-5.** **`ConsoleEditor.cs`** (~423 LOC) mixes rendering, input handling, and file I/O. Consider separating `EditorRenderer` from orchestration.

### Specific Code Issues
**CQ-6.** [x] **`InsertStatementHandler:127`** — `new ExecutePushdownStatementHandler()` bypasses DI. Fixed: inject via constructor.
**CQ-7.** [x] **Silent `catch {}` in `CreateConnectionStatementHandler`** — Fixed: logs at Verbose.
**CQ-8.** [x] **`SelectStatementHandler`** — `LastResult` capped at 50,000 rows; true counts preserved in `TotalRowsMatched`/`RowsProcessed`.
**CQ-9.** **`SelectStatementHandler` ORDER BY** — No hyper-scale mitigation. Aggregation checks `>100,000` rows and switches to `ExternalAggregateEngine`, but ORDER BY buffers the entire result set unconditionally. Add the same threshold check.
**CQ-10.** **DI violations in `SelectStatementHandler`** — Lines 31–33 lazy-init `JoinEngine`, `AggregateEngine`, `WindowEngine` with `new`. Lines 453, 567, 580, 594 create `ExternalAggregateEngine` and `InMemoryDataSource` directly. Should be injected or factory-created.
**CQ-11.** [x] **Linter rule registration hardcoded** — Fixed: `LintStatementHandler` uses `Assembly.GetTypes()` reflection to discover and register all `ILintRule` implementations automatically.

### Thread Safety
**CQ-12.** [x] **`Evaluator.IsVerbose` setter** — Fixed: no longer writes to global `Logger.IsVerbose`.
**CQ-13.** **`Stack<Row> _outerRowStack`** — Not thread-safe. Must be addressed before multi-script work begins (Engine Item 1).
**CQ-14.** [x] **`DockerContainerManager.cs:17-18`** — Fixed: uses `ConcurrentDictionary` for `_activeContainers` and `_connectionStrings`.
**CQ-15.** [x] **`MetadataManager.cs`** — Fixed: all `List<ConnectionInfo>` accesses are guarded with `lock (list)`; reads and writes are consistent.
**CQ-16.** [x] **`Evaluator.cs`** — Fixed: uses private `_lastResultSetsLock` and `_messagesLock` objects instead of locking on public properties.

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

1. USE PASSWORD = 'password';
  As the user types the password it should be ******** shown instead of the actual characters.  This is the password required to open the script and is the password used in encrypting the connection strings.  This is not the password for decryption of files, password for SFTP/FTP, or any other connection with an explicit password.  This just encrypts the sensitive data in the script file.

2. To see password the user must type SET SHOW_PASSWORD ON; and then USE PASSWORD = 'password'; and it will be written in plain text. This is the password required to open the script and is the password used in encrypting the connection strings.  SET SHOW_PASSWORD OFF; will turn off the password visibility, this is the default.

[x] 3. New variable data types:  CHAR (fixed length), BIT (0/1), BOOLEAN (TRUE/FALSE) alias BOOL, TINYINT (0-255), BIGINT, TIME, JSON, XML, TEXT alias to VARCHAR(MAX), VARBINARY, IMAGE alias to VARBINARY(MAX), BLOB alias to VARBINARY(MAX), LOB alias to VARBINARY(MAX), UNIQUEIDENTIFIER, VECTOR, PATH (variant of string that holds a file path, path is checked to exist), ENCRYPTED (variant of string that holds an encrypted value, the value is encrypted with the password used to open the script).

[x] 4. Variables can be declared in a batch.  DECARE @v1 int, @v2 varchar(100), @v3 boolean;  No need to repeat the DECARE keyword for each variable.
[x] 5. ALTER CONNECTION and CREATE OR ALTER CONNECTION.  These will be used to update connection strings and other connection properties.  The connection string will be encrypted with the password used to open the script.  CREATE OR ALTER will CREATE if the connection does not exist, and ALTER if it does exist.  ALTER will update the connection string and other connection properties.  The connection string will be encrypted with the password used to open the script.

6. FLATFILE, JSON, XML, EXCEL, FTP, SFTP, EMAIL.  The PASS option is an alias for PASSWORD.  Both words are accepted but PASSWORD is the preferred term.  These passwords are encrypted with the password used to open the script.  The password is not shown when the script is opened, even if SET SHOW_PASSWORD is ON.  They will show ENC: .... just like when a connection string is encrypted.  These passwords will not be encrypted if the user explicitly uses the ENCRYPT=OFF option.  Then they will be shown as plain text and saved as plain text.  This is the same behavior as connection strings.

[x] 7. Check to make sure TOP(100) is the same as TOP 100 the user can use either syntax.

[x] 8. ORDER BY is missing from the Querying & Filtering in the ETL_SQL_Language_Reference.md file.

[x] 9. Column & Table Metadata Tags are written twice in the ETL_SQL_Language_Reference.md file.  Lets create a Lineage section and move the Column & Table Metadata Tags to that section.

[x] 10. Need to add REGEXP_LIKE, REGEXP_SUBSTR, REGEXP_REPLACE, REGEXP_INSTR, REGEXP_COUNT, REGEXP_MATCHES, REGEXP_SPLIT_TO_TABLE to the engine.

[x] 11. Missing some string functions: STUFF, STRING_ESCAPE, STRING_SPLIT, ASCII, CHAR, FORMAT, PATINDEX, STR, QUOTENAME, TRANSLATE, UNICODE, DATALENGTH, TO_STR, REPLICATE, TRY_CAST

[x] 12. Missing JSON and XML functions.  

[x] 13. Missing some math functions: SIN, COS, TAN, ASIN, ACOS, ATAN, ATAN2, ROUND, FLOOR, SIGN

[x] 14. Is NEWID ftc4122 compliant?  We want to be up to the latest version.  UUID v7.

[x] 15. Missing some aggregate functions/window functions: CUME_DIST, DENSE_RANK, NTH_VALUE, PERCENT_RANK, RANK, PERCENTILE_CONT, PERCENTILE_DISC

[x] 16. EXECUTE and EXEC are the same.  EXECUTE is the preferred term.

[x] 17. Variants of EXECUTE to be more explicit when necessary.  This is the same as SELECT INTO it just more clear of what is pushed down to the source system.  May be necessary for queries with source system temp tables and variables.  This will push down anything in the BEGIN END block to the source system and return the results to the ETL-SQL engine temp table.  If the user explicitly creates the temp table then the data returned from the source system must match number of columns and data types of the temp table or it will fail.

I would like to also be able to use the INSERT INTO syntax. so we can do a more direct column mapping.  But the same holds true what comes out of the source system must match the columns and data types of the temp table or it will fail.
```sql
EXECUTE <connection_name> INTO #<temp_table_name>
BEGIN
    SELECT * FROM <table_name> WHERE <column_name> = <value>
END

-- Explicit
CREATE TABLE #temp (col1 int, col2 int);
INSERT INTO #temp(col1, col2)
EXECUTE <connection_name>
BEGIN
    SELECT * FROM <table_name> WHERE <column_name> = <value>
END
```
[x] 18. FOR and FOREACH loop the @i and @val does not have to be declared.  They are declared automatically when used.  FOR @i = 1 TO 10 BEGIN ... END  FOREACH @val IN (1,2,3,4,5) BEGIN ... END

[x] 19. LINT should check Database connections against its keywords to make sure they are valid.  For Example Oracle should fail if the user tries to use TOP in the query.  

[x] 20. Need to add a WAITFOR DELAY '00:00:00' to the engine.  This will be used to pause the script for a specified amount of time.

21. Add CREATE SETS !<name> and DROP SETS [IF EXISTS] !<name> command.  These are used to set one or more variables to specific values.  Useful for setting production, test, dev, or other environments setups but may have other uses outside of just DevOps.  Then the user can use USE SETS !<name> to switch between the different environments.  The user can also add SET WITH_PROMPT ON; this will stop the script and the user must explicitly say yes to move forward.  This is to prevent the user from accidentally running a script in the wrong environment.  This only happens when the user is manually executing the script.  If the script is ran as a job or executed from the command line it will not prompt the user.
```sql
DECLARE @connection_name1 varchar(100), @connection_name2 varchar(100);
CREATE SETS !PROD_ENV
BEGIN
    @connection_name1 = 'my_connection1',
    @connection_name2 = 'my_connection2';
    SET WITH_PROMPT ON;
END
CREATE SETS !DEV_ENV
BEGIN
    @connection_name1 = 'my_connection1',
    @connection_name2 = 'my_connection2';
END
CREATE SETS !TEST_ENV
BEGIN
    @connection_name1 = 'my_connection1',
    @connection_name2 = 'my_connection2';
END

USE SETS !PROD_ENV;

``` 
22. **DO NOT IMPLEMENT THIS YET**  SET WHAT_IF ON;  This statement will not write anything back to databases, files or any other destination.  It will not send files, create files, directories, or anything else.  It will just run the script and show the user what it would do by writing this out to the messages tab and the results tab.  This is useful for testing scripts before running them in production.  SET WHAT_IF OFF; will turn off the what if mode, this is default.

23. **DO NOT IMPLEMENT THIS YET** CREATE SSH_KEY_PAIR(<bits>, <password>, <path>);  This will create a public/private key pair for encrypting and decrypting data.  The password is used to encrypt the private key file.  The bits is the number of bits to use for the key pair.  The path is the path to the directory where the key pair will be stored.  The default path is the same directory as the script file.
---

[x] 24. Continuing with item 17 I would also need a way to pass a variable to the source system.  This would be done by adding WITH(<variable_name>, <variable_name>, ...)  The variables will then be injected into the query into the placeholders ? or ?1, ?2, ?3, etc.  If ? is used the order of the variables will be the order of the placeholders in the query.  The placeholders will be replaced in order.  If ?1, ?2, ?3, etc. is used the placeholders will be replaced by their position.  ?1 will be replaced by the first variable, ?2 will be replaced by the second variable, etc.

```sql
DECLARE @id int = 1;
DECLARE @name varchar(100) = 'John Doe';
DECLARE @email varchar(100) = [EMAIL_ADDRESS]';

EXECUTE <connection_name> INTO #temp WITH(@id, @name, @email)
BEGIN
    SELECT * FROM <table_name> WHERE <column_name> = ? AND <column_name> = ? AND <column_name> = ?
END

-- OR

EXECUTE <connection_name> INTO #temp WITH(@id, @name, @email)
BEGIN
    SELECT * FROM <table_name> WHERE <column_name> = ?1 AND <column_name> = ?2 AND <column_name> = ?3;
END

```

[x] 25. This is a follow-up to 17.  First we need to remove FILE as an alias to FLATFILE.  It started as a good idea but it is causing confusion.  Lets remove it from all tests, scripts, and documentation.  Then we'll move on to 26.
[x] 26. This is a follow-up to 25 and related to 17.  Because we are doing pushdowns it can be confusing if the connection is a FLATFILE, EXCEL, JSON, XML, etc.  We need a way for the user to run a query directly against the source system similar to how do this for the databases.  So when we have a connection that is a file and not a database the default name for the "table" will be FILE.  So the user can do this:

```sql
CREATE CONNECTION c ON FLATFILE('C:\Users\chuck\scratch\ETL-SQL\TestData\test_categories.csv');

EXECUTE c INTO #temp
BEGIN
    SELECT * FROM FILE WHERE <column_name> = <value>
    --
    DELETE FROM FILE WHERE <column_name> = <value>
    --
    INSERT INTO FILE (col1, col2, col3) VALUES (1, 2, 3)
    --
    UPDATE FILE SET <column_name> = <value> WHERE <column_name> = <value>
END
```

This keeps consistency with the database connections and allows the user to run queries directly against the source system similar to how do this for the databases.  Likewise if the user would like to do the shorthand
```sql
CREATE CONNECTION c ON FLATFILE('C:\Users\chuck\scratch\ETL-SQL\TestData\test_categories.csv');


    SELECT * FROM c.FILE WHERE <column_name> = <value>
    --
    DELETE FROM c.FILE WHERE <column_name> = <value>
    --
    INSERT INTO c.FILE (col1, col2, col3) VALUES (1, 2, 3)
    --
    UPDATE c.FILE SET <column_name> = <value> WHERE <column_name> = <value>

    -- Or continue to use

    SELECT * FROM c WHERE <column_name> = <value>
    --
    DELETE FROM c WHERE <column_name> = <value>
    --
    INSERT INTO c (col1, col2, col3) VALUES (1, 2, 3)
    --
    UPDATE c SET <column_name> = <value> WHERE <column_name> = <value>

```
[x] 27. We should also add some test files that are xlsx so we can test functionality against an excel, json, and xml file, etc.  Also some bigger sample files.  These go in the TestData folder, please follow the standard naming convention.

[x] 28. Parser:  Parser has its own Keywords list.  LanguageMetadata has its own Keywords list.  Each Connection has it own syntax list.  Here is what I want we need to consolidate these down into single sources of truth.  Problems to solve.  Connection syntax was designed to allow for pushdown operations and to know the syntax allowed by that source.  This would prevent issues like using TOP in Oracle.  To me the Connection syntax should just contain a diff off the standard keywords that every other database uses.  Flat file, Excel, JSON, XML, ect would also have their own syntax but it would be very simple SELECT, FROM, WHERE, AND, OR, INSERT, UPDATE, DELETE, etc.  Can we find a way to continue the functionality of connection syntax knowing their own specific keywords without creating the huge list of repeated keywords throughout the code?

**Design notes (brainstorm):**

**Clarifications:**
- `FILE` is NOT a connector-type keyword. It is the reserved default table name inside any file-based connector block (FLATFILE, EXCEL, JSON, XML). It should be removed from `LanguageMetadata.Keywords` as a connector-type identifier and treated as a reserved table name instead — similar to how `DUAL` works in Oracle.
- "Plugin" terminology is dead. All references to `GetAllPluginKeywords()`, `GetAllPluginFunctions()`, `GetAllPluginOptionValues()` in `DatabaseConnectors.cs` and `SuggestionProviders.cs` must be renamed to `GetAllConnectorKeywords()`, `GetAllConnectorFunctions()`, `GetAllConnectorOptionValues()`.

**Proposed Model: Baseline + Diff**

`LanguageMetadata` becomes the single canonical source of truth for the ETL-SQL language baseline. `Parser.IsKeyword()` stops maintaining its own HashSet and derives from `LanguageMetadata` at startup instead. Each connector Syntax class becomes a diff only:

```csharp
public interface IConnectorSyntax
{
    IReadOnlySet<string> Additions  { get; }  // dialect-specific keywords/functions not in baseline
    IReadOnlySet<string> Exclusions { get; }  // baseline keywords this connector does NOT support
    IReadOnlySet<string> GetEffectiveKeywords(); // baseline + Additions - Exclusions (default impl in base)
}
```

Example diffs (very short after stripping the shared baseline):
- `SqlServerSyntax.Additions`  = { TOP, NOLOCK, ISNULL, CHARINDEX, CONVERT, GETDATE }; `Exclusions` = { LIMIT, OFFSET }
- `OracleSyntax.Additions`     = { ROWNUM, CONNECT_BY, SYSDATE, NVL, NVL2, DECODE, FETCH }; `Exclusions` = { TOP, LIMIT, ISNULL }
- `PostgresSyntax.Additions`   = { LIMIT, OFFSET, NOW, STRPOS }; `Exclusions` = { TOP, ISNULL, CONVERT }
- File connectors (FLATFILE, EXCEL, JSON, XML) get minimal `Additions = {}` and a large `Exclusions` list — naturally documents what file connectors support.

**Side benefit:** Once Exclusions exist, Syntax 19 (dialect keyword linting) is nearly free — linter checks if a used keyword is in the target connector's Exclusions set.

**LanguageMetadata cleanup needed before implementation:**
- Move connector-type names (FLATFILE, CSV, EXCEL, JSON, XML) out of the misc `Keywords` bucket into their own `ConnectorTypes` category
- Remove `FILE` from keyword lists entirely — it's a reserved table name, not a keyword
- Verify `DmlKeywords`, `DdlKeywords`, `ControlFlowKeywords` etc. are complete so the baseline is solid before connectors diff against it

**Files to change:**
- `src/ETL-SQL.Core/Common/LanguageMetadata.cs` — cleanup + add `ConnectorTypes` category, remove `FILE`
- `src/ETL-SQL.Core/Parser/Parser.cs` — replace `IsKeyword()` HashSet with computed set from `LanguageMetadata`
- `src/ETL-SQL.Core/Data/DatabaseConnectors.cs` — define `IConnectorSyntax`, rename plugin→connector methods
- `src/ETL-SQL.App/UI/SuggestionProviders.cs` — rename plugin→connector call sites
- Each `*Syntax.cs` in `src/ETL-SQL.Connectors/` — strip baseline words, keep only diffs

29. Tags should be able to be used to in CREATE TABLE.  So the user can define the columns as its created.

## VS CODE Bugs/Improvements
1. Linter errors should stop executing and bounce to the Messages tabs showing the errors
``` sql
    DECLARE @id int;
          ,@name varchar(100);
```
The linter already knows this is an error but its not red so that should be step 1.  Step 2 if the user does execute it should jump to the Messages tab with the error.

2. Using the same example above, on error the Executing message with the cancel button is showing but the query has stopped.  Cancel does nothing when clicked.

[x] 3. Need a comment/uncomment keyboard shortcut.  Need this for both ui edit and vs code.  This will comment or uncomment out the selected

4. When running DOCKER in vs code, first I ran this code:
```sql
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');

-- 2. Define a connection using the dynamic connection string
DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
CREATE CONNECTION m ON MSSQL(@conn);
```
Next I selected this code and ran run with selection
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
I noticed the container was closed and it sat at the executing message, cancel didn't work and there was no error messages.

[x] 5. This statement should have worked but did not.  Error message: LSP: Syntax error in Parser: Only native EXECUTE ... BEGIN ... END blocks are supported as an INSERT source. at line 24, col 1 at 24:1.  I'm guessing we forgot to implement INTO #temp for this kind of EXECUTE block.  Fixed: parser and handler both support EXECUTE (@stmt) AT connection INTO #temp. Tests added to RemoteExecuteTests.cs.
```sql
USE DOCKER('mcr.microsoft.com/mssql/server:2022-latest');

-- 2. Define a connection using the dynamic connection string
DECLARE @conn varchar(500) = DOCKER.CONNECTION_STRING;
CREATE CONNECTION m ON MSSQL(@conn);

EXECUTE m
BEGIN
    CREATE TABLE dbo.Employee (
        id INT PRIMARY KEY,
        [name] NVARCHAR(100)
    );
    INSERT INTO dbo.Employee (id, [name]) VALUES (1, 'Alice'), (2, 'Bob'), (3, 'John');   
END

DECLARE @id INT = 1
       ,@name varchar(50) = 'John'
       ,@stmt varchar(2000)
SET @stmt = 'SELECT t.id, t.[name] FROM dbo.Employee AS t WHERE t.id > ' + @id + ' AND t.[name] = ''' + @name + ''';';
SELECT @stmt;

EXECUTE (
    @stmt
) AT m INTO #emp;

SELECT * FROM #emp;
```

6. LINEAGE gets lost when using a EXECUTE block.  Well need to read the pushdown SQL statement as best we can for the hover LINEAGE and then capture the actual LINEAGE after execute when we know exactly what came back from the PUSHDOWN.  The goal would be to track this back and say id came from MSSQL dbo.Employee's table