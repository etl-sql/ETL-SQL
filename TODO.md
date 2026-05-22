# ETL-SQL Development
## Bugs
### VS Code
- [ ] **Not seeing EXPLAIN showing** When running EXPLAIN on a query I would expect to see results.
- [ ] **Performance tab scrollbar** The performance tab does not have a scrollbar so often can't see half of what is being shown without increasing the height of the panel.
- [x] **Orchestrator and ReportPortal queries not working** C:\Users\chuck\scratch\ETL-SQL\samples\integration\setup_orchestrator.etlsql message says: Connection 'portal' does not support native SQL pushdown.
- [ ] **Linter message** On file: :\Users\chuck\scratch\ETL-SQL\samples\integration\setup_orchestrator.etlsql getting Syntactic check of pushdown block failed. This may be due to native SQL syntax or a syntax error: Unexpected token type SEMICOLON (';') at start of statement at line 1, col 111
- [ ] **When saving this script it should require a password**  Script clearly has a plain text password, on save it should ask for a master password.
- [ ] **Reduce fonts in help popups** The help fonts are too big when compared to the rest of the text see screenshot: C:\Users\chuck\scratch\ETL-SQL\brain\Screenshot 2026-05-10 155716.png

### General
- [x] **Implement Remote Orchestrator & Report Portal Connectors** The `ORCHESTRATOR` and `REPORTPORTAL` connection types are documented in the grammar but are missing from the C# engine execution layer. This blocks the entire remote administrative ecosystem. The following specific statement handlers need to be implemented:
  - **User & Group Management**: `CREATE/ALTER/DROP USER`, `CREATE/DROP GROUP`, `ADD USER ... TO GROUP`, `DISCONNECT USER`, `REVOKE TOKENS`, `SHOW USERS`, `SHOW ACTIVE SESSIONS`
  - **Folder & Permissions**: `CREATE/ALTER/DROP FOLDER`, `GRANT`, `REVOKE`
  - **Report Lifecycle**: `PUBLISH REPORT`, `ALTER REPORT`, `DROP REPORT`, `ALTER DATASET`, `DROP DATASET`, `SHOW REPORTS`
  - **Refresh & Snapshot Management**: `CREATE/DROP REFRESH JOB`, `REFRESH REPORT`, `REFRESH DATASET`, `REBUILD/DROP SNAPSHOT`, `RESTART/SHUTDOWN PORTAL`
Must be included in the documentation. `C:\Users\chuck\scratch\ETL-SQL\Docs\Syntax_Index.md`, `C:\Users\chuck\scratch\ETL-SQL\Docs\Reference\Data_Connectors.md`
- [x] **Linter error for non-created files SELECT** You can create a connection to a file that does not exist its how we put data into it.  But if the user tries to do a SELECT from it the query runs like nothing happened.  Trying to find the right balance where its OK to do an insert into a non-existing files (that's how its created), but you can't do a select/update/delete
- [ ] **Is SLT corpus complete** It seems like its only SELECT queries but I thought there was a lot more of them.  Can we validate we have a complete SLT test suite.
- [x] **SHOW JOBS/ SHOW JOB HISTORY not working** Fixed: `OrchestratorDataSource` now handles both statements inside `EXECUTE orch BEGIN...END` blocks via the REST API. Added `AT <connName>` syntax for standalone use: `SHOW JOBS AT orch`, `SHOW JOB HISTORY [JobName] AT orch`. Added `GET /api/history` endpoint to the Orchestrator service for all-job history queries.
- [x] **API_KEY needs to be set as sensitive**  API_KEY should work just like password and be encrypted and masked.
- [X] **Need to improve the messaging**  When running a select, insert, update, delete, show the messages should show the number or rows returned.  When create/alter/create or alter/drop it should show the objects created.
- [x] **Disabled jobs still show**  Disabled jobs should still show up in SHOW JOBS.  We just need to add a column Enable (1=yes, 0=no)
- [x] **Orchestrator job error**  Fixed: published `orch://` bundle script paths are virtual paths and are no longer used as filesystem base paths during `ResolvePath`; relative paths inside published scripts now resolve from the orchestrator working directory instead of throwing `Basepath argument is not fully qualified`.  The source code is here: C:\Users\chuck\scratch\ETL-SQL\samples\integration\setup_orchestrator.etlsql
- [x] **Disable/Enable AT**  Cannot run ENABLE JOB <name> AT <connection> same for DISABLE JOB.  Getting this error: [94760039] [PARSER Error] Unexpected token type AT ('AT') at start of statement at line 1, col 29 at line 1, col 29
- [x] **Need SHOW BUNDLES command**  SHOW BUNDLES should be an alias of SHOW PUBLISHED BUNDLES.  Since the other SHOW BUNDLE ... don't include the word PUBLISHED it may be confusing so we'll do a SHOW BUNDLES to be consistent with other commands like SHOW JOBS, SHOW CONNECTIONS,...
- [ ] **SHOW PUBLISHED BUNDLES returns nothing** Nothing is returned no rows even though I know one was published
- [ ] **Newer syntax not colored** Newer syntax words like PUBLISHED BUNDLE don't have color in TUI or VS Code

### Report Portal
- [x] **Orchestrator in portal show failed**  The portal shows the number of jobs failed but doesn't give you any way to figure out which ones.  Can we make those metrics clickable?
- [x] **Third Party notice less pronounced**  Right now 3rd party notice is a button like Reports, and Admin.  Let's reduce it to a hyperlink in the bottom left hand corner.
- [x] **Reporting menu items**  Reporting menu items favorites, recently viewed, my subscription disappear when Orchestrator or Admin is selected.  Make it more apparent they belong in reporting.  Clicking Reports flies them out of the right so you know when you click Admin they'll be sucked back into the Report button.
- [x] **Report portal branding** Provide a way for an admin to add branding to report portal.  Usually involves a company image and some branding text.  Thinking bottom left for the text and image.  We could also set a toolbar image size that would display next to the users?
- [ ] **Implement Report Portal Connector — Extended Admin Scripting (v1.1)** The following portal admin statements are parsed and have AST nodes but have no connector handler (same engine gap as the primary connector TODO above). They all have corresponding REST API endpoints in the portal. Implement them as part of v1.1 once the core connector is stable:
  - **Sharing & Embedding**: `CREATE SHARE LINK FOR REPORT`, `SHOW SHARE LINKS FOR REPORT`, `REVOKE SHARE LINK`, `CREATE EMBED TOKEN FOR REPORT`, `SHOW EMBED TOKENS FOR REPORT`, `REVOKE EMBED TOKEN`
  - **Saved Views**: `CREATE SAVED VIEW`, `SHOW SAVED VIEWS FOR REPORT`, `DROP SAVED VIEW`
  - **Alerts**: `CREATE ALERT`, `SHOW ALERTS FOR REPORT`, `DROP ALERT`
  - **Subscriptions (remote)**: `CREATE SUBSCRIPTION`, `ALTER SUBSCRIPTION`, `DROP SUBSCRIPTION` via HTTP REST (current handlers use local SQLite — needs remote connector path)
  - **Catalog & Discovery**: `SHOW CATALOG SEARCH`, `SHOW EFFECTIVE PERMISSIONS FOR USER`, `SHOW PORTAL USAGE METRICS`
  - **Report Utilities**: `FAVORITE REPORT`, `UNFAVORITE REPORT`, `VALIDATE REPORT SCRIPT`, `SHOW REPORT HISTORY`, `SHOW REPORT DEPENDENCIES`
  - **Session control**: `DISCONNECT USER`, `SHOW ACTIVE SESSIONS` (requires portal-side endpoint additions)
  - **Service control**: `RESTART PORTAL`, `SHUTDOWN PORTAL` (requires portal-side endpoint additions)
  - [x] **Make it more obvious that subscriptions, favorites, and recently reviewed are apart of the report button.  Its an odd transition to click Orchestrator or Admin and have those close down.
  
### TUI

## Codebase Review Findings
- [ ] **Unwrapped Database/Provider Exceptions in Connectors**
  The following connectors do not catch and wrap provider-specific exceptions (e.g., `SqlException`, `NpgsqlException`, `OracleException`, `OdbcException`, `FtpException`, `SshException`, `HttpRequestException`, `RequestFailedException`) in `ExecutionException` before crossing the connector boundary:
    - **SQL Server** ([SqlServerDataSource.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/SqlServer/SqlServerDataSource.cs))
    - **PostgreSQL** ([PostgresDataSource.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/Postgres/PostgresDataSource.cs))
    - **Oracle** ([OracleDataSource.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/Oracle/OracleDataSource.cs))
    - **ODBC** ([OdbcDataSource.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/Odbc/OdbcDataSource.cs))
    - **FTP** ([FtpConnector.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/FtpConnector.cs))
    - **SFTP** ([SftpConnector.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/SftpConnector.cs))
    - **REST** ([RestConnector.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/Rest/))
    - **Azure Blob** ([AzureBlobConnector.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Connectors/AzureBlobConnector.cs))
  This violates Section 8 of the developer guardrails in `AGENTS.md`.
- [ ] **Blocking Semaphore Slim Wait in SpillStore**
  In `SpillStore.EnsureInitialized` ([SpillStore.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Spill/SpillStore.cs#L74)), the semaphore is waited on synchronously using `_initLock.Wait()`. This blocking call inside synchronous property accessors like `RootPath` can lead to thread-pool starvation when executing in critical async context paths.
- [ ] **Swallowed Exception in PortalBrandingSettingsService Constructor**
  In `PortalBrandingSettingsService.cs` ([PortalBrandingSettingsService.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.ReportPortal/Services/PortalBrandingSettingsService.cs#L33)), the constructor catches all exceptions thrown during file reading or deserialization of `portal-branding.json` and completely swallows them (`catch { }`), hiding underlying file access or formatting corruption issues.
- [ ] **Missing Zero-Trust Path Resolution in CreateDatasetStatementHandler**
  In `CreateDatasetStatementHandler.WriteSidecarScript` ([CreateDatasetStatementHandler.cs](file:///C:/Users/chuck/scratch/ETL-SQL/src/ETL-SQL.Engine/Handlers/CreateDatasetStatementHandler.cs#L272)), `File.WriteAllText(sidecarPath, ...)` is called directly without passing `sidecarPath` through `context.ResolvePath()`. This bypasses the Zero-Trust security boundary mandate in Section 8 of `AGENTS.md`.
