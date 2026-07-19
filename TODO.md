# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; completed work belongs in `CHANGELOG.md`,
release notes, or the relevant implementation/design document.

---

## v0.15.0 Release Debt

Findings surfaced during the v0.15.0 release. Full detail in
`docs/architecture/decisions/v0.15.0-flaky-tests.md` and `docs/architecture/decisions/v0.15.0-performance-results.md`.

### Restore the 70% coverage gate

`ci.yml`'s threshold was lowered **70.0 -> 69.5** to ship v0.15.0 (landed at 69.8%). Analysis
from 2026-07-13 found that the v0.15.0 headline feature (`Core.Adaptive.*`) is already well-covered;
the remaining gap is infrastructure coverage.

- [ ] `App.*` runners (`WarmJobRunner`, `EnterpriseEnrollmentManager`, `DatabaseMigrationService`) are
      the biggest untested chunk but hardcode elevation checks, stores, and file I/O. Meaningful tests
      need a testability seam first, not error-path-only tests.
- [ ] Iterate CI-in-the-loop: add tests, push, read the CI coverage percentage (the authoritative
      scope; a local run excluding Portal reports around 50%, not comparable), repeat until >= 70.0,
      then restore the `ci.yml` threshold to **70.0**.

---

## v0.16.0 Pre-Release Evidence

Collect release-suite evidence before publishing v0.16.0. The detailed evidence packet template is
[`docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md`](docs/architecture/decisions/Enterprise_Release_Evidence_Checklist.md).

- [x] Functional fast lane: `.\scripts\test-lane.ps1 -Lane fast -NoRestore`.
      Passed on 2026-07-18 after the warm-runner apphost fix: 5,164 passed, 5 skipped, 0 failed.
- [ ] Full pre-release lane:
      `.\scripts\Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration -IncludeStandardScale -BuildInstallers -Platforms win-x64`.
- [ ] Migration and upgrade evidence: `.\scripts\Test-PreRelease.ps1 -IncludeSlt -Explain`
      plus N to N+1 upgrade-path evidence.
- [x] Enterprise hardening certification on Windows and Linux:
      `.\scripts\Test-EnterpriseHardeningCertification.ps1`.
      Windows passed on 2026-07-18 with run ID `enterprise-20260718-094727`
      (engine 298/298, Portal 61/61); Linux passed on 2026-07-18 with run ID
      `enterprise-20260718-125000` (engine and Portal enterprise slices passed).
- [ ] Recovery drill evidence: `etl-sql admin restore --validate --report recovery-report.json`.
- [ ] HA failure certification: `etl-sql admin ha-soak fault-run` and
      `etl-sql admin ha-soak validate`.
- [x] Scale and performance evidence: `.\scripts\Test-ScaleCertification.ps1 -Tier Smoke`
      passed 13/13 scenarios on 2026-07-18; run Standard tier when advertising scale claims.
- [x] Standalone regression:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~StandaloneRegressionTests --no-restore`
      passed on 2026-07-18.
- [x] Security boundary docs:
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~SecurityBoundaryDocTests --no-restore`
      passed on 2026-07-18.

---

## v0.16.0 Fresh-Eyes Repository Review

Findings from a repository-wide build, static-analysis pass, test run, and targeted review of
security boundaries, identity, execution, storage, connector, Portal, Docker, and frontend code on
2026-07-18. Treat P1 items as v0.16.0 release blockers.

### Security and correctness

- [x] **P1 - Close symlink and reparse-point escapes in artifact and workstation roots.**
      `FileSystemArtifactStorage.Resolve` and `WorkstationWorkspace.ResolveEditablePath` enforce only
      lexical `GetFullPath` containment, so a symlink or junction below an allowed root can redirect
      reads, writes, deletes, moves, or editor saves outside that root. Reuse canonical-path and
      handle-verification protection from the engine filesystem policy, reject unsafe recursive
      traversal, and add Linux symlink plus Windows junction/reparse tests.
      Fixed on 2026-07-18 by canonicalizing storage/workstation roots and resolved paths through
      `SecurityService.ResolvePathSymlinks`, skipping reparse points during workstation recursion, and
      adding directory-symlink escape regressions. Validated with
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter "FullyQualifiedName~FileSystemArtifactStorageTests|FullyQualifiedName~WorkstationEditorTests" --no-restore -m:1`.
- [x] **P1 - Make refresh-token consumption atomic and index token hashes.**
      `AuthController.Refresh` reads an unrevoked token, revokes it, and inserts its successor without
      a transaction, concurrency token, or conditional update. Concurrent requests can therefore mint
      multiple valid successors. Add a unique token-hash index and an atomic compare-and-set rotation;
      prove one-time use with concurrent SQLite and PostgreSQL tests.
      Fixed on 2026-07-18 with conditional database update rotation, SQLite/PostgreSQL unique token-hash
      migrations, and concurrent refresh-token reuse coverage. Validated with
      `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~AuthSessionInvalidationTests" --no-restore -m:1`.
- [x] **P1 - Stop writing generated first-run administrator passwords to persistent logs.**
      `ETL-SQL.Portal/Program.cs` sends the plaintext generated password through structured
      `LogWarning`, exposing a live credential to file sinks and log collectors. Replace this with an
      explicit bootstrap secret or a protected one-time handoff that never enters application logs,
      and update the quick-start documentation.
      Fixed on 2026-07-18 by requiring the explicit first-run bootstrap password and failing closed when
      it is absent; startup now logs only that the account was created. Quick-start, deployment,
      production-readiness, and config-reference docs were updated.
- [x] **P1 - Export complete datasets instead of exporting the preview cache.**
      `DatasetViewerService.PrepareExportAsync` calls `LoadCachedAsync`, which reads only
      `Portal:MaxPreviewRows`; CSV and XLSX exports are silently truncated at the preview limit. Stream
      all Parquet row groups through a cancellation-aware export path and add coverage with more rows
      than the configured preview maximum.
      Fixed on 2026-07-18 by making exports read all dataset rows outside the preview cache and adding
      coverage with more rows than `Portal:MaxPreviewRows`. Validated with
      `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter "FullyQualifiedName~DatasetViewerServiceTests" --no-restore -m:1`.
- [x] **P1 - Bound and invalidate the dataset preview cache.** `DatasetViewerService` caches as many as
      50,000 row dictionaries per dataset in an application-wide `IMemoryCache` with sliding expiry,
      no size accounting, and no invalidation on refresh or replacement. Add a global memory budget and
      entry weights, use dataset version/content identity in keys, and invalidate mutations so active
      users cannot receive stale rows indefinitely.
      Content-identity invalidation was added on 2026-07-18 using dataset version, row count, timestamps,
      at-rest key version, and parquet path in preview cache keys. Completed on 2026-07-18 by moving
      previews into a dedicated size-limited cache with `Portal:Dataset:PreviewCacheMaxRows`, row-count
      entry weights, config docs, and eviction coverage.
- [x] **P1 - Restore actual Docker pause/resume semantics.**
      `DockerContainerManager.PauseContainer` calls `StopAsync` and `ResumeContainer` calls
      `StartAsync`, contradicting the documented CPU-suspension behavior and changing container state.
      Use the provider's pause/unpause API and add integration tests that distinguish paused from
      stopped containers.
      Completed on 2026-07-18 by switching to Docker pause/unpause APIs and adding integration coverage
      that inspects Docker state before pause, after pause, and after resume.
- [x] **P1 - Propagate cancellation through data-source contracts and provider I/O.** Core
      `IDataSource`/`IDatabaseSource` batch, schema, transaction, and raw-SQL methods lack cancellation
      tokens, and SQL/MongoDB implementations call async open/read/write APIs without tokens. Extend the
      contracts and async enumerators, pass execution cancellation to every provider call, and add
      cancellation tests for each connector family so timeout and shutdown requests stop remote I/O.
      Progress on 2026-07-18: added cancellation-aware `IDataSource.ReadBatches`,
      `IDataSource.WriteBatches`, and `IDatabaseSource.ExecuteRawSql` overloads, routed central
      engine source resolution, pushdown, schema DDL, bulk insert, and dynamic EXEC paths through the
      execution token, and added direct `InMemoryDataSource` cancellation coverage. Remaining work:
      provider-native overrides and cancellation tests for SQL, MongoDB, file, REST/API, and graph
      connector families.
      Additional progress on 2026-07-18: added native cancellation-aware read/write/raw-SQL overrides
      for SQLite, SQL Server, PostgreSQL, MySQL/MariaDB, ODBC, and Oracle data sources, including
      provider open/read/write calls where supported, plus a regression test that guards those SQL
      provider overrides. Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native overrides and cancellation tests for MongoDB, file, REST/API,
      and graph connector families.
      Additional progress on 2026-07-18: added native cancellation-aware read/write overrides for
      MongoDB collection reads, schema sampling, collection drops, and inserts. Validated with
      `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native overrides and cancellation tests for file, REST/API, and graph
      connector families.
      Additional progress on 2026-07-18: added native cancellation-aware read/write/raw-SQL overrides
      for Neo4j and cancellation checks around graph cursor and write-batch loops. Validated with
      `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native overrides and cancellation tests for file and REST/API connector
      families.
      Additional progress on 2026-07-18: added native cancellation-aware read/write/raw-SQL overrides
      for REST/API, linked per-request timeouts with execution cancellation, and passed tokens through
      request construction, OAuth token acquisition, HTTP sends, response reads, retry delays, pagination,
      and response-table writes. Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native overrides and cancellation tests for file connector families.
      Additional progress on 2026-07-18: added native cancellation-aware read/write/raw-SQL overrides
      for JSON and XML data sources, passed execution tokens through streaming enumeration, file writes,
      compression/encryption checkpoints, and XML schema discovery, and expanded the connector-family
      override coverage. Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native overrides and cancellation tests for the remaining file connector
      families.
      Additional progress on 2026-07-18: completed native cancellation-aware read/write/raw-SQL overrides
      for Avro, FlatFile, Parquet, and Excel data sources, including custom record-reader cancellation,
      cancellable file/schema reads and writes where supported, async-enumerator disposal, and checkpoints
      around synchronous compression/encryption and workbook/Avro library calls. Validated with
      `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native overrides and cancellation tests for warehouse, messaging, admin,
      and lightweight engine storage connector families.
      Additional progress on 2026-07-18: added native cancellation-aware read/write/raw-SQL overrides
      for BigQuery and Snowflake, passing execution cancellation through retry pipelines, provider query
      execution, Snowflake cursor reads, BigQuery result materialization, and write-batch enumeration.
      Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native overrides and cancellation tests for messaging, admin, and
      lightweight engine storage connector families.
      Additional progress on 2026-07-18: added native cancellation-aware read/write overrides for Kafka,
      SMTP, DIRECTORY, MOCKDB, Portal/Orchestrator admin connection stubs, and lightweight engine
      storage/lineage sources. Kafka now observes cancellation while polling and producing messages,
      SMTP passes cancellation through attachment copies and MailKit calls, DIRECTORY streams enumeration
      with checkpoints, and in-memory virtual sources avoid default wrapper fallbacks. Validated with
      `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: audit and extend schema/introspection, transaction, and admin-operation methods
      where cancellation tokens are still absent or not propagated.
      Additional progress on 2026-07-18: added token-aware contract overloads for schema/introspection,
      existence checks, transaction boundaries, and Portal/Orchestrator admin plan/execute operations;
      routed evaluator transaction management, data-source enlistment, admin delegation, SHOW TABLES/
      COLUMNS, and DML schema lookups through the active execution token. `AppendOnlyColumnDataSource`
      transaction checkpoints now use cancellable semaphore waits and rollback constraint rebuilds.
      Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native schema/introspection overrides where remote catalog APIs still use
      compatibility defaults instead of direct cancellation tokens.
      Additional progress on 2026-07-18: added native cancellation-aware schema/introspection overrides
      for SQLite, SQL Server, and PostgreSQL, passing tokens through provider open, execute, and reader
      loops for table, view, and column discovery. Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1`
      and `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native schema/introspection overrides for the other database, warehouse,
      graph, document, and file connector families.
      Additional progress on 2026-07-18: added native cancellation-aware schema/introspection overrides
      for MySQL/MariaDB, ODBC, and Oracle. MySQL and Oracle pass tokens through async open, execute,
      and reader loops; ODBC uses token-aware connection/query paths where available and explicit
      checkpoints around synchronous driver schema APIs. Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1`
      and `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Remaining work: provider-native schema/introspection overrides for warehouse, graph, document,
      and file connector families.
      Additional progress on 2026-07-18: added native cancellation-aware schema/introspection overrides
      for BigQuery and Snowflake. Snowflake passes tokens through connection, execution, and reader
      loops; BigQuery passes tokens through column discovery queries and checks cancellation while
      materializing table/view listings. Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1`
      and `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
      Completed on 2026-07-18 by adding provider-native schema/introspection cancellation overrides
      for graph, document, REST/API, and file connector families, including Portal designer schema
      discovery call-site propagation and cancellation-aware JSON schema parsing. Validated with
      `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter FullyQualifiedName~DataSourceCancellationTests --no-restore -m:1`.
- [x] **P1 - Repair the warm-runner regression and its failure diagnostics.** The current fast lane
      reproducibly fails `ProcessJobExecutorChaosTests.WarmRunner_ExecutesMultipleJobs_AndClearsActiveProcessTracking`:
      the apphost copied into the test output exits with CLR code `-532462766` because
      `Microsoft.Extensions.DependencyInjection.Abstractions` is missing. Run the test against a
      complete app build/publish output, and include sanitized captured stderr in
      `ProcessJobExecutor` failure results instead of returning only stdout.
      Fixed on 2026-07-18 by resolving the complete app build output for the warm-runner test and
      including sanitized stderr in process failure results. Validated with
      `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --filter "FullyQualifiedName~ProcessJobExecutorChaosTests" --no-restore -m:1`
      and the passing fast lane above.

### Performance, observability, and quality gates

- [x] **P2 - Make shared-storage usage sampling bounded and observable.**
      `PortalStorageUsageSampler` recursively enumerates every dataset and snapshot file every 30
      seconds on every Portal node, cannot cancel an in-progress enumeration, and converts all failures
      to a false zero-byte reading without logging. Use incremental or leader-only sampling with a
      configurable cadence, retain the last successful value, and expose failure/staleness telemetry.
      Completed on 2026-07-18 by adding configurable cadence, timeout, and file-count limits; retaining
      the last successful byte counts on bounded failures; logging sampler failures; and exposing
      staleness plus last-success/last-failure telemetry in admin and Prometheus metrics.
      Validated with `dotnet build ETL-SQL.slnx --no-restore -m:1` and
      `dotnet test tests\ETL-SQL.Portal.Tests\ETL-SQL.Portal.Tests.csproj --filter FullyQualifiedName~OperationalObservabilityTests --no-restore -m:1`.
- [x] **P2 - Close frontend build and lint gaps in CI and pre-release scripts.** The extension lint
      currently reports 50 warnings while still succeeding, and automation installs/builds only
      `src/etl-sql-vscode`; the separate `ui` package is audited but never installed, built, linted, or
      tested. Add clean-install UI gates and make production-code lint warnings fail CI after clearing
      the existing backlog.
      Completed on 2026-07-18 by making production extension lint warnings fail with
      `--max-warnings=0`, clearing production lint warnings, adding UI `npm ci`, lint, and build gates
      to CI plus Windows/Linux pre-release scripts, and making UI lint fail on warnings. Validated with
      `npm run lint` and `npm run compile` in `src/etl-sql-vscode`, plus `npm ci`, `npm run lint`, and
      `npm run build` in `src/etl-sql-vscode/ui`.
- [x] **P2 - Remove role-query N+1 behavior from Portal user lists.** `AdminController.GetUsers` loads
      the entire user table without pagination and both user-list endpoints call
      `UserManager.GetRolesAsync` once per user. Require paging for the unbounded endpoint and batch-load
      role memberships with users/groups in a fixed number of database queries.
      Fixed on 2026-07-18 by capping `/api/admin/users` with page/pageSize parameters and batch-loading
      role and group membership rows for both user-list endpoints.
