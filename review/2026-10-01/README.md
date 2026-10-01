# Code review — 2026-10-01

**17 findings: 8 P0, 7 P1, and 2 P2.** Sixteen findings have executable reproductions. The Excel performance finding is based on direct inspection of the read path.

This is the review snapshot before repairs. Current implementation status and after-fix evidence are in [FIXES.md](FIXES.md).

Reviewed commit: `978ad6341b994ee8f6458a15515bc04d55b97014`.

This review covers correctness, security, performance, and logging across the main engine and host boundaries. Production source was unchanged at review time. The review files include an isolated reproduction project and evidence; the project is not added to the solution or normal test lanes.

Severity follows [AGENTS.md §18](../../AGENTS.md#18-triage--defect-principles--a-wrong-answer-outranks-a-crash): wrong answers and silent data corruption are P0 even with narrow triggers. P1 covers credential exposure and serious failures in supported operations. P2 covers cancellation handling and avoidable resource growth.

| ID | Severity | Finding | Evidence |
| :--- | :--- | :--- | :--- |
| R01 | P0 | Hash join drops matches that the default comparison considers equal | LOOP returns one row; HASH returns zero |
| R02 | P0 | Inner transaction commit makes remote writes survive outer rollback | SQLite retains the inserted row |
| R03 | P0 | Remote UPDATE and DELETE bypass transaction enlistment | Both mutations survive rollback |
| R04 | P0 | History persistence failure replays an already successful job | Executor runs twice after one history failure |
| R05 | P0 | SharePoint and Active Directory repeat rows across batches | Two SharePoint rows become five |
| R06 | P0 | SharePoint list reads ignore continuation links | Second page is missing |
| R07 | P0 | SharePoint verbose OData responses silently return no rows | `d.results` fixture returns an empty result |
| R08 | P0 | Studio save clears newer unsaved edits and deletes their recovery draft | Dirty flag becomes false; draft is removed |
| R09 | P1 | Remote UPDATE emits parameter names providers do not bind | SQLite rejects missing `@up0` and `@up1` |
| R10 | P1 | REST redirects forward credentials to another port on the same host | Target receives the Bearer token |
| R11 | P1 | REST pagination forwards credentials to another host | Linked target receives the Bearer token |
| R12 | P1 | REST request timeout stops protecting reads after response headers | A one-second timeout leaves the body read pending after two seconds |
| R13 | P1 | Shared Studio saves use a tenant-prefixed key in tenant-scoped storage | Saving an existing script throws a legacy-collision exception |
| R14 | P1 | Connector exception wrapper retains unsanitized provider exceptions | `ToString()` exposes a synthetic password |
| R15 | P1 | Engine structured logs ignore sensitive placeholder names | `{Password}` exposes an unregistered synthetic value |
| R16 | P2 | REST caller cancellation becomes a connector failure | Expected cancellation exception becomes `ExecutionException` |
| R17 | P2 | Excel batch reads materialize every worksheet before applying sheet/range selection | `AsDataSet()` executes before the first batch |

## R01 — P0: Hash join changes equality semantics

Location: [JoinEngine.cs:918](../../src/ETL-SQL.Engine/Engines/JoinEngine.cs#L918), [JoinEngine.cs:1221](../../src/ETL-SQL.Engine/Engines/JoinEngine.cs#L1221), and [CompoundKey.cs:79](../../src/ETL-SQL.Core/Data/CompoundKey.cs#L79).

Hash joins build and probe a dictionary using `CompoundKey`. String keys keep their case, and key equality uses `object.Equals`. The engine's default string comparison is case insensitive ([EvaluationUtils.cs:72](../../src/ETL-SQL.Core/Data/EvaluationUtils.cs#L72)). Consequently, changing the join strategy changes the result for identical inputs.

**Reproduction:** `HashJoinMustAgreeWithLoopJoinUnderDefaultCaseInsensitiveComparison` in [CorrectnessReproductions.cs](repro/CorrectnessReproductions.cs) joins `alpha` to `ALPHA`. LOOP returns one row; HASH returns none. This silently drops records from downstream transformations.

**Fix direction:** use a join key comparer whose equality and hashing match the active comparison setting. Apply it consistently to hash construction and probing, including spill partitioning. Check NULL and coercion semantics at the same boundary. The reproduced case is the in-memory hash path; external variants were inspected but were not executed in this review.

## R02 — P0: Nested COMMIT commits the physical transaction early

Location: [TransactionManager.cs:107](../../src/ETL-SQL.Engine/TransactionManager.cs#L107).

`CommitTransaction` calls `CommitAsync` on the current snapshot's enlisted sources before decrementing the nesting count. It then discards that snapshot even when an outer transaction remains. This violates the documented contract that an inner COMMIT only decrements `@@TRANCOUNT`, while ROLLBACK rolls back the outermost transaction ([transaction reference](../../docs/reference/statements/session-control/transaction.md)).

**Reproduction:** `NestedCommitMustRemainRollbackableByTheOuterTransaction` starts two transaction levels, inserts into a real temporary SQLite database, commits the inner level, and rolls back the outer level. The database contains two rows instead of its original one.

**Fix direction:** retain remote enlistments for the full transaction and commit providers only when the nesting count reaches zero. Outer rollback must still own every enlisted provider after an inner commit.

## R03 — P0: UPDATE and DELETE execute outside the declared transaction

Location: [UpdateStatementHandler.cs:30](../../src/ETL-SQL.Engine/Handlers/UpdateStatementHandler.cs#L30) and [DeleteStatementHandler.cs:29](../../src/ETL-SQL.Engine/Handlers/DeleteStatementHandler.cs#L29).

Both handlers retrieve the connection directly and call `ExecuteRawSql`. They bypass the resolver that enlists transactional sources ([DataSourceManager.cs:398](../../src/ETL-SQL.Engine/Services/DataSourceManager.cs#L398)). BEGIN does not eagerly start transactions on these database connections. If no preceding statement has enlisted the connection, the mutation autocommits despite the script's transaction guard.

**Reproduction:** `MutationAsTheFirstTransactionalOperationMustBeRollbackable` runs two cases against a real temporary SQLite database. `BEGIN; UPDATE ... SET amount = id; ROLLBACK;` leaves amount `1` instead of `10`. `BEGIN; DELETE ... WHERE id = 1; ROLLBACK;` leaves zero rows instead of one. The UPDATE uses a column expression so R09 cannot mask this defect.

**Fix direction:** resolve/enlist the target before executing a mutation, using the shared transaction-aware path. Add regressions for the first statement after BEGIN, without relying on a prior read or insert to establish the transaction.

## R04 — P0: Failed history logging duplicates successful work

Location: [SchedulerService.cs:706](../../src/ETL-SQL.Orchestrator/Scheduling/SchedulerService.cs#L706) and [SchedulerService.cs:744](../../src/ETL-SQL.Orchestrator/Scheduling/SchedulerService.cs#L744).

After the executor reports success, history, quality metrics, statement metrics, and resume metadata are persisted inside the same try block as execution. A persistence exception enters the execution-failure catch, records FAILURE, replaces the successful result with a retryable failure, and runs the script again. A history outage can duplicate committed external writes. The error log also falsely labels the incident as an execution failure.

**Reproduction:** `HistoryFailureMustNotReplayAnAlreadySuccessfulScript` in [SchedulerReproductions.cs](repro/SchedulerReproductions.cs) makes an executor append one committed-output marker and report success. The first SUCCESS history write fails once; later writes succeed. With one allowed retry, the output contains two committed markers. The real scheduler and throttle execute; the executor/history boundaries are mocked.

**Fix direction:** preserve the completed execution outcome independently of evidence persistence. Log persistence failure separately, and retry the evidence write without replaying the script. Apply the same separation to all post-execution metric and checkpoint persistence.

## R05 — P0: Batch rollover copies rows instead of just the schema

Location: [SharePointConnector.cs:550](../../src/ETL-SQL.Connectors.Cloud/SharePoint/SharePointConnector.cs#L550) and [ActiveDirectoryConnector.cs:342](../../src/ETL-SQL.Connectors.Remote/ActiveDirectory/ActiveDirectoryConnector.cs#L342).

After yielding a full batch, both connectors call `table.Clone()`. ETL-SQL's clone copies every row ([DataModel.cs:792](../../src/ETL-SQL.Core/Data/DataModel.cs#L792)). Subsequent batches retain all previously returned rows, exceed the requested size, and are yielded again. This both corrupts output and creates cumulative copying and allocation costs. Once the threshold is reached, each subsequent row can yield the growing accumulated result.

**Reproduction:** `BatchesMustNotIncludePreviouslyReturnedRows` in [SharePointReproductions.cs](repro/SharePointReproductions.cs) supplies rows `[1, 2]` with batch size one. The connector returns `[1, 1, 2, 1, 2]`. The Active Directory site uses the identical rollover pattern and clone implementation; it was verified by inspection, without a live LDAP server.

**Fix direction:** create an empty table with the existing schema after each yield. Do not change the semantics of `DataTable.Clone`, which other callers may need. Add coverage for exact batch boundaries and multiple batches in both connectors.

## R06 — P0: SharePoint list reads stop at the first page

Location: [SharePointConnector.cs:505](../../src/ETL-SQL.Connectors.Cloud/SharePoint/SharePointConnector.cs#L505).

The reader performs one GET and enumerates its result. It never follows `odata.nextLink`, `@odata.nextLink`, or verbose `d.__next`. A paginated list is reported as a successful, partial extraction without a truncation indicator.

**Reproduction:** `ListReadMustFollowTheServerContinuation` supplies first-page row `1` with a continuation URL whose response contains row `2`. The output is `[1]`, with no second request. A batch size of 100 isolates this from R05.

**Fix direction:** enumerate every continuation with cancellation, bounded loop detection, and per-request egress authorization. Preserve the credential boundary when following a link; R11 shows why URL authorization alone is insufficient.

## R07 — P0: Verbose SharePoint responses become empty successful reads

Location: [SharePointConnector.cs:515](../../src/ETL-SQL.Connectors.Cloud/SharePoint/SharePointConnector.cs#L515).

The reader accepts a top-level `value` or `d`, but only processes it if that property is an array. In verbose OData, `d` is an object containing a `results` array. The reader skips that object and returns zero rows without an error. The connector does not explicitly constrain its JSON response to the one envelope it can read.

**Reproduction:** `VerboseODataListReadMustReturnItsResults` supplies `{"d":{"results":[{"Id":1}]}}`. The output is empty instead of one row.

**Fix direction:** support the documented OData envelopes or explicitly negotiate and validate one supported format. An unsupported response shape must fail clearly instead of appearing to be an empty list. Unwrap its continuation alongside its rows.

## R08 — P0: A pending Studio save discards the state of later edits

Location: [studio-file-commands.ts:304](../../src/ETL-SQL.ReportRuntime/Resources/TypeScript/designer/studio-file-commands.ts#L304).

`performSave` captures the document, awaits the save callback, then unconditionally clears `isDirty` and removes its recovery draft. The user can edit that document while the request is pending. When the older save completes, the newer text has never been persisted, but close/recovery behavior treats it as saved. If the returned state contains content, `Object.assign` can also overwrite the newer buffer state.

**Reproduction:** [studio-save-repro.mjs](repro/studio-save-repro.mjs) imports the actual compiled runtime module. It saves `SELECT 1;`, edits the document to `SELECT 2;` while the callback is pending, and then resolves the callback. The document still contains `SELECT 2;`, but its dirty flag is false and its recovery draft has been removed. See [Node evidence](evidence/studio-save-node.tap).

**Fix direction:** associate completion with the submitted document revision/content. Only clear dirty state and remove the draft if the current buffer still matches that saved revision. Preserve newer text when applying returned metadata. Cover typing, tab switching, and multiple pending saves.

## R09 — P1: UPDATE parameters do not match the provider contract

Location: [UpdateStatementHandler.cs:44](../../src/ETL-SQL.Engine/Handlers/UpdateStatementHandler.cs#L44) and [SqliteDataSource.cs:478](../../src/ETL-SQL.Connectors.Databases/Sqlite/SqliteDataSource.cs#L478).

The UPDATE handler renames compiled parameters to `@up0`, `@up1`, etc., then passes only their values to `ExecuteRawSql`. Providers reconstruct names as `@p0`, `@p1`, etc. The SQL and bound parameter collection disagree, so an ordinary UPDATE containing literals fails. PostgreSQL also reconstructs the canonical `pN` names; the executed reproduction is SQLite.

**Reproduction:** `SqliteUpdateWithLiteralsMustBindItsParameters` executes a guarded UPDATE setting amount to `20` where id is `1`. SQLite throws because `@up0` and `@up1` are unbound.

**Fix direction:** keep SQL generation and binding under one canonical parameter contract. Avoid naive substring replacement when combining expressions, which can confuse names such as `@p1` and `@p10`.

## R10 — P1: Same-host redirects leak credentials across ports

Location: [RestDataSource.cs:1466](../../src/ETL-SQL.Connectors/Rest/RestDataSource.cs#L1466).

`ShouldStripCredentialsOnRedirect` checks hostname changes and HTTPS-to-HTTP downgrades, but ignores ports. A redirect to another service on the same hostname keeps Authorization, Cookie, and other sensitive headers. Allowing a destination through egress policy does not authorize disclosure of the source service's credentials.

**Reproduction:** `RedirectToAnotherPortMustNotReceiveCredentials` in [RestReproductions.cs](repro/RestReproductions.cs) redirects between two local HTTP servers on different ports. The target receives `Authorization: Bearer synthetic-review-token`.

**Fix direction:** compare the complete origin: scheme, host, and effective port. Strip origin-bound credentials on every origin change unless explicit credential-sharing policy authorizes that destination.

## R11 — P1: Pagination links reinject credentials for a different host

Location: [RestDataSource.cs:516](../../src/ETL-SQL.Connectors/Rest/RestDataSource.cs#L516), [RestDataSource.cs:599](../../src/ETL-SQL.Connectors/Rest/RestDataSource.cs#L599), and [RestDataSource.cs:639](../../src/ETL-SQL.Connectors/Rest/RestDataSource.cs#L639).

A server-provided next URL is checked for egress access, then used in the next `BuildRequestAsync` call. That call adds the connector's configured authentication again. The redirect credential-stripping path never runs. A malicious or compromised allowed API can direct pagination to another allowed host and receive its credentials there. Both JSON next-URL pagination and Link-header pagination share this behavior.

**Reproduction:** `PaginationLinkToAnotherHostMustNotReceiveCredentials` follows a Link header from `127.0.0.1` to `localhost`. The second server receives the source connector's Bearer token. This is a controlled local-server test using a synthetic credential.

**Fix direction:** carry authentication policy through pagination and enforce the original credential origin before building the request. Reject or strip credentials on origin changes and protocol downgrades. Regression coverage should exercise both continuation mechanisms.

## R12 — P1: Request timeout expires at headers, not at the end of the read

Location: [RestDataSource.cs:427](../../src/ETL-SQL.Connectors/Rest/RestDataSource.cs#L427) and [RestDataSource.cs:462](../../src/ETL-SQL.Connectors/Rest/RestDataSource.cs#L462).

In the nonpaginated path, the timeout CTS surrounds a `ResponseHeadersRead` send. It is disposed when the headers arrive. Body extraction uses the caller token instead. An endpoint can promptly return headers and then hold the read indefinitely, despite `TIMEOUT_SECONDS`. This can occupy a worker or throttle slot until an independent outer cancellation fires.

**Reproduction:** `BodyReadMustRemainUnderTheConfiguredRequestTimeout` returns headers immediately and holds the body behind a test gate. With a one-second request timeout, the reader remains pending after two seconds. The gate is released during cleanup; the test does not leave a stalled server or job running.

**Fix direction:** keep the per-request timeout alive through the response-body read and parsing. Propagate its linked token into the extractor, and dispose the response and request for success, retries, and failures.

## R13 — P1: Shared Studio saves use the wrong logical artifact key

Location: [ReportScriptSaveService.cs:61](../../src/ETL-SQL.Portal/Services/ReportScriptSaveService.cs#L61).

Studio creation resolves paths under the tenant root ([StudioController.cs:136](../../src/ETL-SQL.Portal/Controllers/StudioController.cs#L136)). Save calls the non-tenant overload of `ToScriptKey`, producing `tenant-alpha/studio/item.etlsql` instead of `studio/item.etlsql`. Tenant-scoped storage already adds the tenant prefix. With an existing script, its collision guard sees that incorrectly supplied key as an unscoped artifact and throws. Without the collision, the path would be double-prefixed.

**Reproduction:** `SharedStudioSaveMustUseTheTenantRelativeScriptKey` in [PortalReproductions.cs](repro/PortalReproductions.cs) uses a verified tenant, real in-memory SQLite catalog, and tenant-scoped artifact storage. Saving an existing pipeline throws `InvalidOperationException` at the artifact existence check instead of returning Saved.

**Fix direction:** use the tenant-aware path guard with the catalog's verified tenant. Keep all keys passed to scoped storage tenant-relative. Add an end-to-end create/open/save/reopen case with shared tenancy enabled. The reproduction shows a failed save, not a cross-tenant authorization bypass.

## R14 — P1: Sanitized connector errors still contain the raw provider exception

Location: [ConnectorExceptionWrapper.cs:32](../../src/ETL-SQL.Connectors.Common/ConnectorExceptionWrapper.cs#L32).

The wrapper sanitizes the outer message but chains the original provider exception. `InnerException` and `ToString()` retain its credentials, connection details, and paths. This directly contradicts [connector standard Rule 5](../../docs/architecture/standards/connectors-standards.md#rule-5-all-provider-exceptions-must-be-wrapped), which prohibits chaining the unsanitized inner exception.

**Reproduction:** `ConnectorBoundaryMustNotRetainRawProviderCredentials` in [LoggingReproductions.cs](repro/LoggingReproductions.cs) wraps an exception containing `Password=synthetic-review-password`. The returned exception's string contains the password.

**Fix direction:** remove the raw inner exception or replace it with a recursively sanitized diagnostic representation. Preserve only safe provider codes and correlation data. The repository's main `LoggerService` does recursively redact exception objects; this finding concerns the connector boundary and callers/sinks that inspect the escaped exception directly. No real credential was used or exposed during the review.

## R15 — P1: Engine log redaction ignores the meaning of named properties

Location: [LoggerService.cs:108](../../src/ETL-SQL.Infrastructure.Logging/LoggerService.cs#L108).

The engine logger redacts each argument without its template-property name. An unregistered password string is therefore passed through even when the property is explicitly named `Password`. The Microsoft logging bridge already masks sensitive property names ([LoggerService.cs:149](../../src/ETL-SQL.Infrastructure.Logging/LoggerService.cs#L149)), so the two logging entry points enforce different security rules. The unsafe value reaches Serilog sinks and the UI message callback.

**Reproduction:** `StructuredEngineLogMustMaskArgumentsNamedPassword` captures `OnMessage` after `Info("Authentication failed for {Password}", "synthetic-review-only-value")`. The value appears unmasked. This demonstrates a redaction boundary defect; this review did not find a current engine call site using that exact password template.

**Fix direction:** parse named template properties and mask sensitive arguments before formatting or forwarding to sinks. Keep the existing runtime-secret and recursive object redaction as additional protection. Test both engine and host entry points with secrets that were never entered into the runtime registry.

## R16 — P2: REST cancellation is converted into an ordinary execution error

Location: [RestDataSource.cs:449](../../src/ETL-SQL.Connectors/Rest/RestDataSource.cs#L449) and [RestDataSource.cs:540](../../src/ETL-SQL.Connectors/Rest/RestDataSource.cs#L540).

The broad catch includes `OperationCanceledException`. It creates an `HttpRequestException`, which the connector boundary then wraps in `ExecutionException`. Callers can no longer distinguish a user cancellation from an API failure by exception type, affecting status reporting and failure handling. With retries configured, the same catch also enters retry handling before the caller token stops the delay.

**Reproduction:** `CancellingBeforeHeadersMustPreserveOperationCanceledException` cancels a request waiting for headers. It receives `ExecutionException` instead of a cancellation exception.

**Fix direction:** let caller-requested cancellation escape unchanged. Classify a request timeout separately when only the timeout token fired. Cover both header and body cancellation.

## R17 — P2: Excel batching does not bound input memory

Location: [ExcelDataSource.cs:100](../../src/ETL-SQL.Connectors.Files/Excel/ExcelDataSource.cs#L100).

`ReadBatchesCore` calls `reader.AsDataSet()` with no worksheet filter before selecting the requested sheet and applying RANGE. Every worksheet is fully materialized before the first batch. Selecting a small range in one sheet still pays for unrelated sheets. Cancellation is checked only after this synchronous conversion. The comment explains the lack of an async library API, but does not require full workbook materialization.

**Evidence:** direct read-path inspection. Input memory and time-to-first-batch grow with the whole workbook, independent of the requested batch size. No large-workbook benchmark or OOM test was run, and no numerical memory estimate is claimed.

**Fix direction:** iterate the selected worksheet with the reader's row API, skipping unrelated sheets and applying the range while reading. Yield bounded batches and check cancellation between synchronous reads. Schema/column discovery has additional `AsDataSet()` calls that should be reviewed alongside this change.

## Validation and reproduction

| Check | Result |
| :--- | :--- |
| Existing engine tests: transaction hardening, joins, safe paths, security hardening, connection security | 111 passed, 0 failed, 0 skipped |
| Existing Portal tests: security headers, Studio recovery drafts, capability assignment, tenant artifact storage, auth session invalidation | 24 passed, 0 failed, 0 skipped |
| Browser type gate | Passed, zero findings |
| Browser lint gate | Passed, zero findings |
| Shared asset sync check | Passed, no drift |
| Review .NET reproductions | 18 cases: 16 failed as expected, 2 passed |
| Review Node reproduction | 1 failed as expected |

The failing review tests assert the required behavior. They are intentional evidence of existing defects, not a changed product test suite. The two passing cases check basic SEMI/ANTI join cardinality with duplicate right-hand keys; that candidate was excluded from the findings.

Captured evidence: [.NET reproduction results](evidence/review-reproductions.trx), [Portal focused results](evidence/portal-focused.trx), and [Node reproduction output](evidence/studio-save-node.tap). The earlier 111-test engine run and browser gates are recorded in the review session; no TRX was requested for that earlier engine run.

Run the review reproductions from the repository root after restore:

```powershell
dotnet test .\review\2026-10-01\repro\ETL-SQL.Review.Tests.csproj --no-restore -p:RuntimeIdentifiers= --logger "trx;LogFileName=review-reproductions.trx" --results-directory .\review\2026-10-01\evidence
node --test .\review\2026-10-01\repro\studio-save-repro.mjs
```

This environment used cached packages for restore because NuGet network access was unavailable:

```powershell
dotnet restore .\review\2026-10-01\repro\ETL-SQL.Review.Tests.csproj -p:RuntimeIdentifiers= -p:NuGetAudit=false -p:RestoreSources="$env:USERPROFILE\.nuget\packages"
```

The restore overrides apply only to this review run. No product configuration, package version, analyzer setting, or gate was changed. No current dependency vulnerability audit was performed.

Existing focused test commands used:

```powershell
dotnet test .\tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --no-restore --filter "FullyQualifiedName~TransactionHardeningTests|FullyQualifiedName~JoinTests|FullyQualifiedName~SafePathTests|FullyQualifiedName~SecurityHardeningTests|FullyQualifiedName~ConnectionSecurityTests" --logger "console;verbosity=minimal"
dotnet test .\tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --no-restore -p:RuntimeIdentifiers= --filter "FullyQualifiedName~SecurityHeadersTests|FullyQualifiedName~StudioRecoveryDraftTests|FullyQualifiedName~StudioCapabilityAssignmentTests|FullyQualifiedName~PortalTenantArtifactStorageTests|FullyQualifiedName~AuthSessionInvalidationTests" --logger "trx;LogFileName=portal-focused.trx" --results-directory .\review\2026-10-01\evidence
node .\scripts\typecheck-browser.mjs
node .\scripts\lint-browser.mjs
node .\scripts\sync-assets.js -Check
```

## Coverage and limits

The source inventory contains 31 C# projects and roughly 2,000 C#/TypeScript/JavaScript files. This was a repository-wide risk review with detailed tracing and reproduction of selected paths. It was not a line-by-line audit of every file or a security certification. Absence of another finding in an area does not establish that the area is defect-free.

| Area inspected | Review focus | Validation limits |
| :--- | :--- | :--- |
| Parser, AST, expression evaluation, query planning, joins, spill keys, transactions, mutation handlers | Equality consistency, remote/engine boundaries, enlistment, rollback | Executed in-memory joins and SQLite transactions; no full SLT, fuzz, or spill lane |
| Core security and connector authorization | Path resolution, symlinks, file policy, HTTP egress/DNS policy, secret redaction, crypto boundaries | Selected existing security tests passed; no adversarial OS/filesystem matrix |
| Relational and file connectors | Parameter binding, transaction state, stream/batch lifecycle, JSON/XML handling, Excel materialization | Real SQLite; no live PostgreSQL, SQL Server, warehouse, or large-file benchmark |
| Remote, cloud, and messaging connectors | Authentication forwarding, continuation handling, SFTP host verification, LDAP batches, Kafka cleanup/configuration | Controlled REST servers and SharePoint handler fixtures; no live provider accounts or LDAP server |
| Portal | JWT/auth sessions, object/folder authorization, tenant catalog/storage, Studio save/recovery, audit, execution/session lifecycles | Focused Portal suite and service reproduction; no full browser workflow or deployment penetration test |
| Orchestrator and service host | Retry classification, history/metrics persistence, leases/fencing, throttle and cancellation | Actual scheduler/throttle with mocked execution/history; no multi-node failover or Docker lifecycle lane |
| Gateway and sandbox paths | Framing/result bounds, ledger handling, process lifetime and cancellation | Static inspection; no hostile-tenant boundary test |
| Reporting and report hosting | HTML sanitization, runtime actions, dashboard/session lifecycle | Static inspection plus browser gates; no visual/report export suite |
| Shared browser runtime and Studio | Document state, save/draft completion, typed API boundaries, DOM/CSP handling | Actual save module reproduction; no Playwright run |
| VS Code, language server, workstation editor, CLI/TUI | Webview messages/CSP, analysis lifecycle, draft/source revision handling, error paths | Selected static inspection; extension build/unit suite and desktop interaction tests were not run |

Long-running performance, Docker integration, SLT, randomized fuzz, and real-browser lanes were not run. Cross-platform behavior was inspected from Windows; Linux/macOS execution was not performed. License/CVE verification, load testing, and live external-system failure injection remain outside this review's evidence.
