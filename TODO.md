# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active Sprint (v0.12.0 Stabilization & Release Gates)
*Establishes a stable language contract, unified open-source licensing, distribution trust, and final release gates. Focuses strictly on stabilization and security; no new features.*

- [x] **Phase 1: Language & Manifest Freeze**
  - [x] **Sr. Developer:** Publish the canonical language grammar, connector options reference, and standard library docs. *(Verified `Docs/Reference/Grammar.md`, `Data_Connectors.md`, `Standard_Library.md`, and the authoritative reference map.)*
  - [x] **Sr. Developer:** Define a strict deprecation policy for syntax and options. *(Documented in `Docs/Standards/Breaking_Change_Standards.md` and linked from the reference map.)*
  - [x] **Sr. Developer:** Implement script compatibility test corpus and a migration-linter. *(Added parser corpus coverage and stable `ETLSQL-MIG001` migration diagnostic for deprecated `FILE` connections.)*
  - [x] **Gemini:** Implement `SHOW VERSION` and machine-readable compatibility diagnostics.
- [x] **Phase 2: Licensing & Contribution Policies**
  - [x] **Gemini:** Apply the **Apache-2.0 License** consistently across all projects, extension manifests, and installers.
  - [x] **Sr. Developer:** Establish the **Developer Certificate of Origin (DCO)** for external code contributions. *(Policy documented in `CONTRIBUTING.md`; PR template now requires commit sign-off confirmation.)*
- [x] **Phase 3: Distribution Trust**
  - [x] **Gemini:** Automate build workflows to generate SHA-256 checksums and an SBOM (Software Bill of Materials).
  - [x] **Gemini:** Retain test and certification reports in public release assets.
  - [x] **Gemini:** Implement cache-busting asset fingerprinting (inject hashes into JS/CSS URLs) in the Report Portal to prevent outdated client-side assets after upgrades.
- [x] **Phase 4: Release Gates**
  - [x] **Sr. Developer:** Verify that a clean script-to-scheduled-production workflow completes successfully without manual intervention. *(Added and passed an HTTP-level configuration export test that emits a parseable scheduled-production bootstrap with a target Orchestrator alias.)*
  - [x] **Sr. Developer:** Ensure zero credentials leak in logs, bundles, or debug dumps. *(Verified configuration export secret exclusion, support-bundle diagnostic redaction, and credential-leak hardening tests.)*
  - [x] **Sr. Developer:** Reconcile OIDC/LDAP configurations with standard documentation libraries. *(Aligned `PortalConfig` OIDC/LDAP options with `Docs/Reference/Settings.md` and `Docs/ReportPortal_Administrators_Guide.md`.)*
  - [x] **Sr. Developer:** Implement automatic diagnostic redaction in `etl-sql admin support-bundle` to automatically strip query parameters, private table data, and personal data (PII) before export.

## Core Project Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **Performance Audit Fixes**
  - [x] **Sr. Developer:** Convert `CryptoUtils.EncryptFileWithSsh`/`DecryptFileWithSsh` and `MachineBoundCrypto.EncryptFile`/`DecryptFile` to async/streaming paths; add authenticated encryption for the SSH file envelope.
  - [x] **Gemini:** Move synchronous file and directory operations out of the constructors of `SqliteSessionMetadataStore` and `SnippetLibrary`.
  - [x] **Gemini:** Refactor `AliasScanner` regex matches to use modern `[GeneratedRegex]` source generators and explicit regex timeouts.

## Remaining Projects Code Audit Tasks (v0.12.0 Stabilization)
- [x] **TUI Project Audit Findings**
  - [x] **Gemini:** Synchronous file I/O operations (`_fs.ReadAllLines`, `_fs.WriteAllText`) are called inside asynchronous methods (`LoadAsync` and `SaveAsync` in `EditorFileHandler.cs`), causing thread blocking.
  - [x] **Sr. Developer:** Key bindings and layout rendering checks are performed synchronously on every frame, which can cause UI lag in larger consoles.
- [x] **Report Builder & CLI Project Audit Findings**
  - [x] **Gemini:** Synchronous directory creation (`Directory.CreateDirectory`) and file existence checks are performed inside asynchronous statement execution paths in `ExportReportStatementHandler.cs`.
  - [x] **Gemini:** The CLI tool `Program.cs` performs synchronous file writes (`File.WriteAllText`) and deletions (`File.Delete`) during batch reports generation and cleanup.
- [x] **Report Player & Hosting Project Audit Findings**
  - [x] **Gemini:** `DashboardService.cs` contains synchronous cancellation calls (`_refreshCts?.Cancel()`) inside the async `DisposeAsync()` method.
  - [x] **Sr. Developer:** `ReportPlayer/Program.cs` contains synchronous file reads (`File.ReadAllText`) inside high-frequency minimal API endpoint routes, which block thread pool threads during server requests.
- [x] **Reporting Project Audit Findings**
  - [x] **Sr. Developer:** `PdfExporter.cs` performs synchronous file writes and reads (`File.WriteAllBytes`, `File.ReadAllBytes`) when building PDF documents, instead of streaming asynchronously.
  - [x] **Gemini:** `SnapshotStore.cs` performs synchronous file moves (`File.Move`) and synchronous deletions (`File.Delete`) inside async save/read workflows.
- [x] **ETL-SQL.App Project Audit Findings**
  - [x] **Gemini:** CLI startup logic performs synchronous file checking and console standard output writes before bootstrapping is complete.
  - [x] **Sr. Developer:** Init scaffolding (`InitScaffolder.cs`) and backup/restore services (`BackupRestoreService.cs`) perform synchronous directory creation and local file compression/decompression operations.
- [x] **ETL-SQL.Orchestrator.Service Project Audit Findings**
  - [x] **Sr. Developer:** OrchestratorHostedService uses synchronous blocking lifecycle hooks on worker registration.
  - [x] **Gemini:** Job API routing uses synchronous configuration parameter lookup.
- [x] **ETL-SQL.ReportPortal.Data & Migrations Project Audit Findings**
  - [x] **Sr. Developer:** Dynamic connection configuration database migrations run synchronously on Startup.
  - [x] **Sr. Developer:** Query compilation profiles do not support async initialization.
- [x] **ETL-SQL.ReportRuntime Project Audit Findings**
  - [x] **Gemini:** Visual resource JS/CSS sync logic (`sync-assets.js`) performs multiple synchronous file checks.
- [x] **ETL-SQL.Installer Project Audit Findings**
  - [x] **Gemini:** WiX configuration paths are hardcoded to historic version paths and require script updates during active release packaging.

## Future Performance & Scalability Enhancements
- [ ] **Orchestrator Concurrency Notification** — Transition from a polling loop (500ms) in `JobThrottle` to database-driven event notifications (e.g., PostgreSQL `LISTEN`/`NOTIFY` or Redis pub/sub) to reduce latency and read amplification.
- [ ] **Postgres HA Transition Verification** — Document and verify lock concurrency behavior and latency under high volume when migrating from SQLite to PostgreSQL in clustered HA deployments.
- [ ] **Process Pooling for Out-of-Process Execution** — Implement a warm runner process pool in `ProcessJobExecutor` to avoid OS process startup, CLR initialization, and JIT compilation overhead for out-of-process job execution.
- [ ] **Resource Profiling per Execution** — Enhance the portal execution log to record the peak memory and execution CPU usage of reports (from engine telemetry) so admins can identify and optimize resource-heavy scripts.
- [ ] **Historical Load Profiling** — Expose simple aggregated metrics (e.g., average queue length and execution counts per hour) to help admins decide on schedule shifts for heavy subscriptions.
- [ ] **Apache Arrow Snapshot Format** — Hybrid `.etlsnap` storage now writes large visual row sets as `tables/*.arrow` IPC entries beside lightweight `layout.json`, then rehydrates them for existing portal readers. Remaining work: expose Arrow table entries to the report player and integrate browser-side lazy loading instead of returning fully rehydrated JSON.
- [x] **Encrypted & Compressed Snapshots** — Secure dashboard snapshot packages on disk (`Snapshots` area) by writing `.etlsnap` encrypted ZIP containers with the portal's `Dataset:AtRestKey`; startup migration converts and deletes legacy plaintext `.snapshot.json` artifacts.
- [ ] **Application-Layer PII Encryption** — Implement application-layer column encryption (using EF Core Value Converters and .NET Data Protection keys) for sensitive PII fields (like user email addresses) in local SQLite databases to protect user data at rest without database-level overhead or dependency complications.
