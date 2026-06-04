# ETL-SQL Development TODO List
## v0.10.0 work
- [x] **Installer parity on Linux and macOS**  The Windows MSI overhaul (Windows-service hosting, working-dir anchoring so logs/db/snapshots land beside the app, JWT secret bootstrap, install-folder security safe zone, pinned service ports 5001/5002, full payload incl. wwwroot, connection URLs surfaced, opt-in data cleanup on uninstall) needs equivalents per platform.
  - Linux `.deb` (`scripts/build_linux_packages.sh`, `src/ETL-SQL.Installer/linux/*.service`): IMPLEMENTED — units' WorkingDirectory points at `…/bin`; postinst generates the JWT secret + sets the install-folder safe zone (python3) and enables/starts the services; prerm stops them; postrm purges data on `apt purge`. **Still needs a real on-Linux install test** (portal serves UI on :5002, services start, dataset writes permitted, purge removes data).
  - macOS `.dmg` (`scripts/build_mac_dmg.sh`): scoped to CLI/TUI-only (decision 2026-06-02) — no portal/orchestrator services. The bundle already ships the binaries; no service/JWT/safe-zone setup needed there.
- [x] **Data-cleanup mechanism that works from every uninstall path**  The MSI's opt-in "delete all data" checkbox only appears in the full uninstall wizard. Provide a cross-platform purge (e.g. an `etl-sql` CLI subcommand / documented script) so users on Windows ARP, Linux, and macOS can wipe reports/db/logs consistently. Ties into the installer-parity item above.
  - DONE — added `etl-sql purge` (with `--dry-run` and `--yes`). `DataPurgeService` resolves the real data locations from `IConfiguration` + the `LocalApplicationData` defaults (logs, Snapshots, Reports, portal/orchestrator DBs incl. -wal/-shm, sessions, portal data dirs), with an unsafe-path guard and per-target failures that never block an uninstall. 12 unit tests; documented in Administrators_Guide §8. Fixed the Linux `.deb` postrm `Reports` omission by hand. **Deferred (under installer-parity):** unify the MSI `CleanData` action + `.deb` postrm to call `etl-sql purge --yes` so the path list has one source of truth.
- [x] **Update 3rd party libraries to latest version**  Update all 3rd party libraries to the latest version. I saw a few outdated ones when I ran the test-prerelease script.
  - DONE — bumped 7 NuGet packages (AWSSDK.S3, Confluent.Kafka, Google.Cloud.BigQuery.V2, Microsoft.OpenApi, MySqlConnector, PgpCore, Swashbuckle.AspNetCore) + inventory; held `SkiaSharp.NativeAssets.Linux` at 3.119.2 to match the managed SkiaSharp pulled by Svg.Skia. npm: in-range updates in both extension roots + `vscode-languageclient` 9→10 (major), which raised `engines.vscode`/`@types/vscode` to `^1.91.0` (drops VS Code <1.91 — note in v0.10.0 changelog) and needed a `LogOutputChannel` fix in extension.ts. Validated: Release build + smoke lane; extension compile/lint/71 unit tests; ui lint/build; 0 vulnerabilities.
- [x] **Add lineage** Databases can store lineage in a variety of ways.  For it to flow all the way to a report we need to make it available that the user can import it into a script that falls outside the traditional ways
  - Add Open Lineage import  CREATE LINEAGE FOR TABLE <table> FROM <markdown>.  I'm mocking this after this SHOW LINEAGE FOR #target_table TO 'lineage_report.md';
  - CREATE TAG FOR TABLE <table> [COLUMN <col>]  I'm mocking this after this SHOW TAGS FOR TABLE <table> [COLUMN <col>]
    This allows the user to loop through add add tags to table if they saved them in a non-standard area

- [x] **Job scheduling verification**  Harden Orchestrator job scheduling with a focused integration verification lane before broader load/chaos testing.
  - DONE — added `JobSchedulingIntegrationTests` with an in-process `WebApplicationFactory` Orchestrator host, real SQLite job store, bounded polling, and passing coverage for success, retry failure, restart/resume, cancellation cleanup, MailPit success/failure email send, sanitized failure text, REST outage, blocked file path, unreachable SMTP, and scheduler fault-tolerance.
  - DONE — extended Docker-backed Orchestrator service coverage to start the real `ETL-SQL.Orchestrator.Service` container with process spawning enabled and a real SQLite job store, then verify scheduled jobs actually execute and write completed `SUCCESS` history, `LastRun`, `NextRun`, and metrics fields.
  - Reuse the existing Orchestrator service fixture and MailPit SMTP fixture where practical; avoid introducing new external services unless a scenario cannot be tested with existing fixtures.
  - DONE — core success path creates a short-interval job, waits for execution, and asserts `SUCCESS`, `LastRun`, `NextRun`, rows/metrics fields, persisted history, and the expected API history output contract.
  - DONE — failure path covers invalid script, unreachable dependency, retry attempts, sanitized error text/no secret leakage, and correct final `NextRun`.
  - DONE — resume/restart behavior persists a due job, recreates the scheduler/service against the same SQLite database, and asserts the job is discovered and executed after startup.
  - DONE — cancellation covers a long-running job, kill endpoint transition out of `RUNNING`, idle scheduler metrics, and no stuck active history row after cancellation.
  - DONE — email behavior covers MailPit success and failure delivery and asserts delivered content does not leak secrets.
  - DONE — dependency outage behavior covers unreachable SMTP, unavailable local HTTP/API source, and missing/blocked file path cases, while proving the scheduler loop continues processing a later successful job.
  - DONE — polling helpers with bounded timeouts were added for history assertions so tests are deterministic and do not rely on fixed long sleeps.
  - Verified: `dotnet test tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj --no-restore --filter FullyQualifiedName~JobSchedulingIntegrationTests` (8 passed) and `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --no-restore --filter FullyQualifiedName~OrchestratorServiceDockerIntegrationTests` (2 passed).
  - Keep this as an integration lane, not a load test. Defer high-concurrency sizing, breaking-point discovery, and long-running chaos scenarios to the separate Orchestrator load testing TODO.

- [x] **Job scheduling load and chaos testing**  Build the follow-on Orchestrator job-scheduler stress suite after correctness verification is complete.
  - Measure high-concurrency scheduling behavior with varied `Jobs:MaxConcurrentJobs`, dense schedules, manual trigger bursts, short/medium/long scripts, retry-heavy jobs, and cancellation under load.
  - Identify breaking points for missed schedule windows, queue drain time, SQLite lock/contention failures, worker starvation, runaway memory/CPU, and stuck active jobs.
  - Add controlled long-running chaos scenarios: service restart during queued/running jobs, dependency outage/recovery windows, process-spawn timeout/kill behavior, and scheduler recovery after abrupt container/service termination.
  - Capture administrator-facing metrics: sustainable jobs/hour, max concurrent running jobs, p50/p95/p99 job latency, missed-run risk, queue depth, active process count, CPU, memory, disk I/O, SQLite contention, and history-query responsiveness.
  - DONE - the checked-in local capacity baseline measures paced no-op trigger breaking points, queue depth, queue drain, HTTP latency, jobs/hour, and SQLite contention with `MaxConcurrentJobs=4`.
  - DONE - added 10K, 50K, and 100K row workload scripts; use 10K rows as the default normal-workload sizing target before publishing operator-facing jobs/hour guidance.
  - DONE - added checked-in workload templates for short/medium/long row-volume jobs, retry/failure jobs, mocked I/O, `PARALLEL`, schedule density, manual trigger bursts, and process-spawning comparisons.
  - DONE - bounded integration coverage verifies due-job fan-in, retries, cancellation, restart recovery for overdue schedules, dependency outages, running-job trigger/disable/delete/kill races, mixed Portal/Orchestrator SQLite writes, and no stuck active/queued work.
  - DONE - process-spawn chaos coverage verifies timeout/kill cleanup and orphan child-process cleanup after abrupt service termination.
  - DONE - capacity harness process telemetry now records per-step process metric maxima when `portal.processId` or `orchestrator.processId` is configured, including Windows working set/CPU/thread/handle counters and Linux `/proc` memory/I/O/CPU counters.
  - Verified: `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --no-restore --filter FullyQualifiedName~ProcessJobExecutorChaosTests` (2 passed).
  - Verified: `dotnet test tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj --no-build --filter FullyQualifiedName~JobSchedulingIntegrationTests` (11 passed; `--no-build` used because an unrelated `ETL_SQL.Tests.App.DependencyInjectionSetup` compile error currently blocks rebuilding the full dependency graph).
  - Verified: `node scripts\test-service-capacity-smoke.mjs`.

- [x] **Subscription verification**  Harden Report Portal subscription delivery with an end-to-end integration verification lane.
  - DONE — `SubscriptionIntegrationTests` create real portal subscriptions, register generated Orchestrator jobs, run them against a real SQLite job store, and verify MailPit delivery for core success paths.
  - DONE — reuse the existing portal integration factory, Orchestrator job store/test helpers, and MailPit SMTP fixture where practical; avoid new infrastructure unless an existing fixture cannot cover the scenario.
  - DONE — cover subscription creation for the supported delivery formats: `PDF`, `CSV`, `Markdown`, and `Link`. Treat `XLSX` as a portal export endpoint concern unless subscription delivery adds it as a format.
  - DONE — attachment formats assert expected MIME type, extension, non-empty content, and report-specific markers where feasible; SMTP attachments now infer MIME type from their file extension.
  - DONE — for `Link`, assert the email body contains the expected report link and no attachment.
  - DONE — cover parameterized subscriptions: save parameters, verify the generated job script contains the expected `DECLARE @param ...` lines, run the job, and verify delivered output reflects the parameter values.
  - DONE — update behavior covers schedule, format, SMTP alias, recipient, active state, parameters, generated script rewrite, disabled job state, and disabled-job re-enable behavior against the Orchestrator job store.
  - DONE — delete behavior covers portal row, generated script, Orchestrator job deletion, and related job history removal.
  - DONE — failure coverage includes missing report script, invalid report script, missing SMTP alias rejection, unreachable SMTP port, blocked attachment path, disabled subscription non-execution, and Orchestrator DB unavailable degraded mode.
  - DONE — failure is visible through subscription history and job history, with sanitized messages/no SMTP password or `ENC:` leakage asserted for controlled failures; real completed outcomes synchronize subscription failure counts, audit entries, and admin usage metrics.
  - DONE — bounded polling helpers exist for MailPit and job-history assertions so tests are deterministic and do not depend on fixed long sleeps.
  - Verified: `dotnet test tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj --no-restore --filter FullyQualifiedName~SubscriptionIntegrationTests` (14 passed).
  - Verified: `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --no-restore --filter FullyQualifiedName~SmtpIntegrationTests` (7 passed).
  - Keep this as a subscription correctness lane, not a portal load/security-permission suite. Defer account visibility scenarios to the Report portal create users TODO and throughput sizing to Portal/Orchestrator load testing TODOs.

- [x] **Report portal create users**  Build a concrete portal user/group/permission verification scenario that can be run manually first and automated later.
  - DONE — repeatable isolated fixture provisions representative local users, business groups, nested Finance/Operations folders, reports, datasets, saved views, subscriptions, alerts, share links, and embed tokens.
  - DONE — local identity scenarios cover Admin, Publisher, Viewer, inactive, must-change-password, revoked-token, no-group, and outsider users. LDAP scenarios remain deferred until an LDAP integration fixture is available.
  - DONE — effective folder/report/dataset permissions are compared against the expected matrix in `tests/ETL-SQL.ReportPortal.Tests/user_permissions_matrix.md`.
  - DONE — report workflows cover folder/report listing, catalog search, direct-ID access, readable and hidden snapshots, refresh, authorized and hidden export, favorites, saved views, alerts, subscriptions, metadata management, viewer publish rejection, publisher root-folder rejection, authenticated share-link resolution, and anonymous embed-token resolution.
  - DONE — dataset ACL scenarios cover explicit public visibility, private viewer/editor access, direct-ID denial, and ACL-management denial.
  - DONE — negative cases cover hidden resources, inactive login, must-change-password blocking, revoked refresh tokens, and non-admin denial for users, groups, SMTP, metrics, audit, audit export, Orchestrator status, and effective-permissions surfaces.
  - DONE — subscription creation now enforces `READ` permission on the report folder so hidden report IDs cannot be subscribed to directly.
  - DONE — audit verification uses API-generated user update/delete, group membership changes, token revocation, folder grant/revoke, report publish/delete, subscription create/update/delete, group delete, and admin audit export operations. Audit exports now record `EXPORT_AUDIT_LOG`.
  - Verified: `dotnet test tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj --no-restore --filter FullyQualifiedName~UserPermissionIntegrationTests` (9 passed).
  - Verified regression lane: `dotnet test tests\ETL-SQL.ReportPortal.Tests\ETL-SQL.ReportPortal.Tests.csproj --no-build --filter FullyQualifiedName~SubscriptionIntegrationTests` (14 passed; `--no-build` used because an unrelated staged documentation-test rename temporarily prevents project compilation).
  - Keep this as a correctness/security scenario suite, not a portal load test. Defer throughput and sizing work to the Portal load testing TODO.

- [x] **Portal and Orchestrator load testing**  Build one repeatable capacity-testing program with separate Portal-user and Orchestrator-job workloads so administrators can size each server from measured baselines.
  - DONE — added dependency-free cross-platform Node harness `scripts/test-service-capacity.mjs`, comparison script `scripts/compare-capacity-results.mjs`, self-contained mock-endpoint smoke test, example workload configuration, results location, and operator guide.
  - DONE — harness supports Portal and Orchestrator workloads, role login, API-key requests, setup/cleanup requests, warmup, stepped concurrency, weighted request mixes, JSON-variable capture/substitution, metrics endpoint sampling, SQLite contention detection, breach criteria, and JSON/Markdown reports.
  - DONE — reference-environment requirements, workload profiles, stepped-load method, breach interpretation, tuning knobs, warning signs, and baseline comparison workflow are documented in `Docs/Operations/Capacity_Testing.md`.
  - DONE — added checked-in workload templates for Portal cache-cold refresh and CSV/XLSX/PDF export traffic, Orchestrator short/medium/long row-volume jobs, retry/failure jobs, mocked I/O, `PARALLEL`, schedule density, and process-spawning comparison.
  - DONE — added `scripts/test-capacity-workload-configs.mjs` so every checked-in capacity workload JSON file is validated by the capacity harness in `--validate-only` mode.
  - DONE — published `capacity-results/reference-local` with the sanitized workload, deterministic report/provisioner, generated JSON/Markdown report, environment details, limitations, and reproduction steps.
  - DONE — published administrator-facing sizing guidance in `Docs/Operations/Capacity_Planning.md`, including starter server profiles, row-volume jobs/hour guidance, split-host signals, and server-admin handoff checklist.
  - Verified: `node scripts/test-service-capacity.mjs --config capacity-results/workload.example.json --validate-only`.
  - Verified: `node scripts/test-service-capacity-smoke.mjs`.
  - Verified: `node scripts\test-capacity-workload-configs.mjs` (6 checked-in workload configurations valid).
  - Keep this separate from the correctness verification items above. Job scheduling, subscription delivery, and portal security scenarios should prove behavior; this item should measure throughput, latency, saturation points, and resource usage under controlled load.
  - Measured local reference run completed with no Portal errors or SQLite contention; Orchestrator queue breached at 80 workers and drained to zero after load.

### Portal and Orchestrator operational-readiness follow-ups

- [x] **Restore a fully green Report Portal regression lane**  Diagnose and fix `PortalIntegrationTests.AdminUsageMetrics_ReturnsViewsRefreshAndSubscriptionFailures`, then run the complete Portal lane successfully before making production-readiness claims.
  - DONE - subscription delivery synchronization now preserves the Portal's persisted failure count when the Orchestrator database is readable but contains no completed history for that subscription. Missing history no longer erases known failures from usage metrics.
  - Verified: `PortalIntegrationTests.AdminUsageMetrics_ReturnsViewsRefreshAndSubscriptionFailures` and `SubscriptionIntegrationTests` pass.
  - Verified: `.\scripts\test-lane.ps1 -Lane portal` (79 Portal tests passed; lineage UI and publish-folder Node checks passed).

- [x] **Expose subscription delivery history and failure diagnosis in the Portal UI**  The user and administrator guides describe subscription history, but the current subscription tables do not provide a History action or enough delivery-failure detail. Add last delivery status/time, failure count, sanitized error detail, and delivery history access for both owners and administrators. Ensure admins can pause/resume or retire failed subscriptions without impersonating the owner.
  - DONE - owner and Admin subscription tables now expose delivery history, failure counts or last successful delivery time, and a shared diagnostics modal with attempt status, time, duration, rows, and sanitized error detail.
  - DONE - Admins can pause/resume and delete subscriptions directly from the Admin subscription table.
  - DONE - extracted the shared renderer into `wwwroot/js/subscription-history-ui.js`, added a UI sandbox story, a Node renderer test, and operator documentation.
  - Verified: `.\scripts\test-lane.ps1 -Lane portal` (79 Portal tests passed; lineage UI, publish-folder, and subscription-history UI checks passed).
  - Manual visual verification remains pending because the local UI sandbox server could not be started during this run.

- [x] **Add deterministic Portal and Orchestrator concurrency-race verification**  Extend the correctness suites with bounded scenarios for updating/deleting a subscription while it fires, disabling/deleting/triggering a running job, multiple jobs becoming due together, concurrent report refresh/export, permission changes during active sessions, and mixed Portal/Orchestrator SQLite writes. Keep this separate from throughput sizing so failures identify correctness races rather than capacity limits.
  - DONE - added a bounded scheduler fan-in scenario proving six jobs due together complete successfully, persist `LastRun`/`NextRun`, and drain active/queued metrics.
  - DONE - added a subscription update-during-execution scenario proving the active attempt completes while future schedule, recipient, generated script, and Orchestrator job configuration are updated.
  - DONE - added subscription delete-during-execution cleanup and running-job trigger/disable/kill scenarios that assert no stuck active work remains.
  - DONE - extended the existing concurrent report read and duplicate-refresh scenario to verify CSV, XLSX, and PDF exports remain available from the last complete snapshot while a refresh is active.
  - DONE - added an active-session permission scenario proving an already-issued token immediately reflects group membership removal and restoration.
  - DONE - added running-job delete verification and a bounded mixed Portal/Orchestrator SQLite write scenario that preserves concurrent subscription rows, generated scripts, job definitions, and active job history.
  - Verified: `JobSchedulingIntegrationTests` (11 passed), `SubscriptionIntegrationTests` (17 passed), `UserPermissionIntegrationTests` (10 passed), `PortalIntegrationTests` (45 passed), and `.\scripts\test-lane.ps1 -Lane portal` (80 Portal tests plus lineage UI, publish-folder, and subscription-history UI checks). SMTP-backed suites were run sequentially because they share the fixed MailPit test container.

- [x] **Improve Portal administration for larger user and subscription catalogs**  Add search, filtering, pagination, and practical bulk operations for users, groups, memberships, and subscriptions so administrators can operate dozens or hundreds of accounts without scanning full in-memory tables or repeating one-row actions.
  - DONE - added backward-compatible paged catalog APIs for users, groups, group members, and Admin subscriptions with server-side search and relevant status/provider/role/group/format filters.
  - DONE - added bulk user activation, group deletion with explicit cascade semantics, group membership add/remove, and subscription pause/resume APIs.
  - DONE - updated the Admin UI with compact search/filter controls, page-local selection, pagination, bulk actions, searchable multi-user membership assignment, a reusable catalog UI helper, a UI sandbox story, and operator documentation.
  - Verified: focused `AdminCatalogs_FilterPageAndBulkMutateUsersGroupsMembersAndSubscriptions`, `PortalIntegrationTests` (46 passed), and `.\scripts\test-lane.ps1 -Lane portal` (81 Portal tests plus lineage UI, publish-folder, subscription-history UI, and admin-catalog UI checks).
  - Manual visual verification remains pending because the local browser connection could not be established during this run.

- [x] **Add enterprise identity integration verification**  Create an LDAP integration fixture and verify login, group mapping, inactive/removed users, permission changes, token/session behavior, and local-admin recovery before claiming enterprise identity readiness.
  - DONE - expanded the real OpenLDAP Testcontainers fixture into a lifecycle scenario covering login, auto-provisioning, role/group mapping, directory group removal, permission loss for already-issued tokens, directory user deletion, local-admin recovery, Portal account deactivation, and refresh/access token rejection.
  - DONE - JWT validation now rejects disabled or deleted Portal users on every authenticated request, closing the gap where an already-issued access token remained usable after administrative deactivation.
  - DONE - documented the operational boundary that directory deletion blocks new LDAP logins but requires Portal account deactivation to terminate existing sessions, and documented the need for a tested local recovery Admin.
  - Verified: `PortalLdapIntegrationTests` (real OpenLDAP lifecycle passed), `LdapAuthTests` (4 passed), `.\scripts\test-lane.ps1 -Lane portal` (81 Portal tests plus UI checks), and `.\scripts\test-lane.ps1 -Lane integration` (99 engine integration tests and 29 Report Portal integration tests passed).

- [x] **Publish measured Portal and Orchestrator capacity baselines**  Complete the existing Portal and Orchestrator load-testing TODO by running fixed reference environments, storing sanitized reports, identifying sustained breach points, and publishing conservative concurrent-user and jobs/hour guidance.
  - DONE - published `capacity-results/reference-local` with the sanitized workload, deterministic report/provisioner, generated JSON/Markdown report, environment details, limitations, and reproduction steps.
  - DONE - recommended 20 simultaneously active Portal users for the lightweight mixed workload and approximately 47,000 lightweight no-op jobs/hour with a 20% margin below the highest no-queue Orchestrator step.
  - Verified: Portal remained error-free through 120 workers; Orchestrator first breached the queue threshold at 80 workers, and `/metrics` returned `active_jobs=0` and `queued_jobs=0` after the run.

- [x] **Fix Report Portal snapshot manifests for remote Orchestrator execution**  Portal report execution through `HttpJobChannelClient` currently records a `ReportSnapshots` row after the remote job completes, but the Orchestrator does not write or return the manifest file that snapshot-manifest and export endpoints require.
  - DONE - completed remote report jobs return their serialized manifest only to job-status requests with a valid non-empty Orchestrator API key; unauthenticated status responses never include report data.
  - DONE - Portal sends its configured API key, atomically persists the returned manifest under its own guarded `SnapshotDirectory`, and retains shared-filesystem compatibility during upgrades.
  - DONE - documented that separate-host deployments do not require shared snapshot storage but do require matching API keys.
  - Verified: separate Portal and Orchestrator test hosts cover execute, refresh, snapshot manifest, CSV/XLSX/PDF export, Portal-owned snapshot persistence, and authenticated-only manifest transport.

- [x] **Add some fuzzy matching samples**  Our matching joins, and functions haven't really been used.  Thinking we can add a few samples.
  - DONE - added self-contained samples for fuzzy matching functions, `FUZZY JOIN`/`LEFT FUZZY JOIN` candidate selection, and a real-world customer entity-resolution workflow.
  - DONE - indexed the new scripts in `Docs/Sample_Guide.md`.
  - Verified: each new sample executes successfully through `dotnet run --no-build --project src\ETL-SQL.App -- run <sample> --silent`.

- [x] **Register or remove the documented DIFFERENCE function**  `src/ETL-SQL.Core/Resources/Help/Functions/DIFFERENCE.md` and related help links describe `DIFFERENCE(s1, s2)`, but the engine currently rejects it as an unknown function. Add the implementation and tests, or remove the stale documentation and links.
  - DONE - implemented `DIFFERENCE(s1, s2)` in `FuzzyFunctions.cs` (0-4 Soundex similarity, position-by-position match of the two 4-char codes; NULL if either arg NULL), registered with help, and added to the `LanguageMetadata` fuzzy list. 4 unit tests; enriched DIFFERENCE.md help + added the row to Standard_Library §16.4.

- [x] **Add some cookbook recipes**  Its been a while since we added some recipes to either the regular and reporting cookbooks.  Thinking fuzzy matching, some of the new report types, lineage, tags, orchestrator and portal examples.  We also need a way to check these queries that they work.  I think we have a script that looks through documentation to check them let's make sure it works for cookbook items.
  - DONE - added regular cookbook recipes for curated lineage and tags, fuzzy entity resolution, remote Orchestrator scheduling, and script-first Report Portal catalog deployment.
  - DONE - added a report cookbook recipe combining the newer `SANKEY`, `SUNBURST`, and `NETWORK` visuals.
  - DONE - `CookbookVerificationTests` extracts every SQL/ETL-SQL/Report-SQL fenced block from both cookbooks and requires each recipe to parse without errors.
  - Verified: `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --no-restore --filter "FullyQualifiedName~CookbookVerificationTests" -m:1` (41 passed).

- [x] **Report portal publish report create folder**  When publishing a report if you forget to create a folder you have to cancel out and do the steps all over, can we add a create new link right in the folder dropdown.  Also I had to refresh the page after creating the folder to get it to show up so we need that to not happen either.
  - DONE — extracted the publish-form folder logic into `wwwroot/js/publish-folders.js` (flatten tree, fresh populate, inline create) with a Node unit test (`scripts/test-publish-folders.mjs`, run in the Node UI lane). admin.html's publish form now has a "+ New folder" inline create (name + parent) and always fetches a fresh list, so new folders appear without a page reload and nested folders are selectable. Verified: both reported behaviors are covered by the passing unit test (incl. an end-to-end create→re-populate→auto-select assertion) and the create handler re-populates with the new folder selected. A live visual smoke in the running portal is the only remaining manual confirmation.

### v0.9.0 code-review follow-ups (deferred from the release gate)

_Performance:_
- [x] **Chart SSR concurrency (V8 engine pool)**  `EChartsSsrRenderer` serializes every chart render through one process-wide V8 engine behind a single lock, so concurrent PDF/export requests with many charts fully serialize on it. Replace the single shared engine with a small pool (or per-request engine) so chart rendering can parallelize.
  - DONE — Refactored EChartsSsrRenderer.cs to use a ConcurrentQueue of PooledEngine instances managed by a SemaphoreSlim capacity constraint. Each pooled instance tracks its own registered maps in a local HashSet. Added a multi-threaded parallel execution unit test inside EChartsSsrTests.cs which verified correct concurrent execution. All 3200+ fast tests pass.
- [x] **XLSX export streaming**  `DatasetViewerService.ExportXlsxAsync` / `DatasetController` buffer the whole workbook in memory (`LoadCachedAsync` → `OrderBy().ToList()` → `Materialize` → `MemoryStream` → `ToArray()`), risking OOM on large datasets; the CSV path already streams to `Response.Body`. Stream the XLSX write to the response and drop the full materialization. (CancellationToken is already wired through `XlsxWriter`.)
  - DONE — Changed `XlsxWriter.Materialize` to yield-return rows lazily. Changed `DatasetViewerService` to prepare and filter data on the main thread, returning lazy `IEnumerable` to avoid memory-buffering list copies for exports. Refactored `DatasetController` to stream both CSV and XLSX exports to the client via `System.IO.Pipelines.Pipe` in a thread-safe manner, bypassing MVC content negotiation formatters and eliminating 406 NotAcceptable errors. Added integration tests in `DatasetControllerTests.cs` covering CSV and XLSX streaming exports.
- [x] **Catalog metadata import off the hot path**  With `LINEAGE_IMPORT_CATALOG` on, each distinct source table's first `SELECT … INTO` blocks on ~3 live-DB metadata round-trips in `SelectStatementHandler.EnsureCatalogMetadataImportedAsync`. Per-session deduped, but consider prefetching/batching or moving it off the statement-execution path.
  - DONE - catalog metadata import now dedupes source tables under a lock and starts distinct table imports concurrently, reducing the hot-path wait for multi-source statements while preserving best-effort session-level idempotency.
  - DONE - lineage writes from concurrent catalog imports are serialized through a record gate so metadata recording remains consistent.
  - DONE - added a blocking provider regression test that only completes when multiple source-table metadata imports are started concurrently.
  - Verified: `dotnet test tests\ETL-SQL.Tests\ETL-SQL.Tests.csproj --no-restore --filter FullyQualifiedName~CatalogMetadataImportTests` (12 passed).

_(The cleanup/maintainability items from the v0.9.0 review — PDF/Markdown renderer dedup + TEXT-content drift, connector exception-wrapping dedup, and XLSX export double-selection/name-dedup — were completed during the v0.9.0 release wrap-up.)_
