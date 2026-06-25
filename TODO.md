# ETL-SQL Development TODO List

Use this list to track active-release bugs, features, hardening tasks, and verification work.
Future-version planning belongs in `ROADMAP.md`; move a roadmap phase here only when work on that
release begins.

---

## Active Sprint (v0.12.0 Stabilization & Release Gates)
*Establishes a stable language contract, unified open-source licensing, distribution trust, and final release gates. Focuses strictly on stabilization and security; no new features.*

- [ ] **Phase 1: Language & Manifest Freeze**
  - **Sr. Developer:** Publish the canonical language grammar, connector options reference, and standard library docs.
  - **Sr. Developer:** Define a strict deprecation policy for syntax and options.
  - **Sr. Developer:** Implement script compatibility test corpus and a migration-linter.
  - [x] **Gemini:** Implement `SHOW VERSION` and machine-readable compatibility diagnostics.
- [ ] **Phase 2: Licensing & Contribution Policies**
  - [x] **Gemini:** Apply the **Apache-2.0 License** consistently across all projects, extension manifests, and installers.
  - **Sr. Developer:** Establish the **Developer Certificate of Origin (DCO)** for external code contributions.
- [ ] **Phase 3: Distribution Trust**
  - [x] **Gemini:** Automate build workflows to generate SHA-256 checksums and an SBOM (Software Bill of Materials).
  - [x] **Gemini:** Retain test and certification reports in public release assets.
  - [x] **Gemini:** Implement cache-busting asset fingerprinting (inject hashes into JS/CSS URLs) in the Report Portal to prevent outdated client-side assets after upgrades.
- [ ] **Phase 4: Release Gates**
  - **Sr. Developer:** Verify that a clean script-to-scheduled-production workflow completes successfully without manual intervention.
  - **Sr. Developer:** Ensure zero credentials leak in logs, bundles, or debug dumps.
  - **Sr. Developer:** Reconcile OIDC/LDAP configurations with standard documentation libraries.
  - **Sr. Developer:** Implement automatic diagnostic redaction in `etl-sql admin support-bundle` to automatically strip query parameters, private table data, and personal data (PII) before export.

## Core Project Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **Performance Audit Fixes**
  - [x] **Sr. Developer:** Convert `CryptoUtils.EncryptFileWithSsh`/`DecryptFileWithSsh` and `MachineBoundCrypto.EncryptFile`/`DecryptFile` to async/streaming paths; add authenticated encryption for the SSH file envelope.
  - [ ] **Gemini:** Move synchronous file and directory operations out of the constructors of `SqliteSessionMetadataStore` and `SnippetLibrary`. *(Sr. review: still incomplete.)*
  - [ ] **Gemini:** Refactor `AliasScanner` regex matches to use modern `[GeneratedRegex]` source generators and explicit regex timeouts. *(Sr. review: still incomplete.)*
- [ ] **Security Audit Fixes**
  - [x] **Sr. Developer:** Replace hardcoded test credentials in `DockerContainerManager` (e.g. `postgres`, `mysql`, `Password123!`) with dynamically generated secure secrets or configuration overrides.
  - [x] **Sr. Developer:** Add zero-trust validation checks or path-resolution to file cache access in `MetadataManager`.
- [ ] **Bug & Concurrency Fixes**
  - [x] **Sr. Developer:** Refactor `LineageTracker.GlobalMetadata` to use a thread-safe structure or safe mutation API to prevent exceptions during parallel query executions.
  - [x] **Gemini:** Safely handle and dispose of `IContainer` instances in `DockerContainerManager.StartContainer` on startup exception failures.
  - [x] **Gemini:** Add `CancellationToken` support and propagation to all `DockerContainerManager` container controls.
  - [x] **Gemini:** Wrap SQLite queries in `SqliteSessionMetadataStore` to catch database errors and wrap them in sanitized `ExecutionException`s to prevent path/schema leakage.
- [ ] **Code Styling & Logging Cleanup**
  - [x] **Gemini:** Convert curly-brace block-scoped namespaces in Core files to file-scoped namespaces.
  - [x] **Gemini:** Update `DockerContainerManager` to use structured `Info`/`Warning`/`Debug` logging instead of `_logger.WriteLine("...", ConsoleColor)`.

## Engine Project Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **Performance Audit Fixes**
  - [x] **Gemini:** Implement chunk cleanup for leaked `.tmp` files inside `ExternalSortEngine`, `ExternalAggregateEngine`, `ExternalWindowEngine`, and `ExternalJoinEngine` using `try/finally` blocks and `DeleteChunk`.
  - [x] **Gemini:** Configure `FileStream` with `useAsync: true` in `SpillStore.cs` to enable real asynchronous disk I/O.
  - [x] **Gemini:** Convert synchronous file and directory operations inside `FileSystemService` and `AlterPortalSubscriptionHandler` to async overloads.
  - [x] **Gemini:** Refactor encryption IV reading in `SecureSpillReader` and `ArrowSpillReader` to run asynchronously outside constructors.
- [ ] **Security Audit Fixes**
  - [x] **Sr. Developer:** Add zero-trust validation checks or path-resolution to subscription script updates in `AlterPortalSubscriptionHandler`.
  - [x] **Sr. Developer:** Add safe boundary path checks for local job state `.etlstate` files generated in `Evaluator.cs`.
- [ ] **Bug & Concurrency Fixes**
  - [x] **Sr. Developer:** Decouple interactive `PasswordPrompt` from the Engine core by defining an `IPasswordPromptProvider` interface to avoid blocking service runs.
  - [x] **Sr. Developer:** Propagate `CancellationToken` through `DataSourceManager` connection and query resolutions.
- [ ] **Code Styling & Logging Cleanup**
  - [x] **Gemini:** Convert curly-brace block-scoped namespaces in Engine files (Evaluator, Handlers, Engines, Services) to file-scoped namespaces.
  - [x] **Sr. Developer:** Refactor stdout presentation dependencies (`AnsiConsole.Write`) in `ExplainStatementHandler`, `GenerateJwtSecretStatementHandler`, and `ResultFormatter` to cleanly separate the Engine and presentation layers.

## Analysis Project Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **Performance Audit Fixes**
  - [x] **Gemini:** Cache the discovered list of `ILintRule` types inside a static readonly field in `LinterFactory.cs` to avoid repetitive reflection scans.
  - [x] **Sr. Developer:** Execute independent lint rules concurrently using `Task.WhenAll` in `Linter.AnalyzeAsync` to lower analysis latency.
  - [x] **Gemini:** Convert synchronous file check (`File.Exists`) inside `SchemaValidationRule` to a background task or run asynchronously.
- [ ] **Security Audit Fixes**
  - [x] **Sr. Developer:** Add zero-trust validation checks or path-resolution to schema/file checks in `SchemaValidationRule` to prevent probing restricted path existences.
- [ ] **Bug & Concurrency Fixes**
  - [x] **Gemini:** Capture and log exceptions from filesystem/IO checks in `SchemaValidationRule` instead of silently swallowing them.
- [ ] **Code Styling & Logging Cleanup**
  - [x] **Gemini:** Convert curly-brace block-scoped namespaces in Analysis files (Linter, Lineage, Rules) to file-scoped namespaces.
  - [x] **Sr. Developer:** Inject `ILogger` into the linting pipeline to enable diagnostic logging during script checks.

## Connectors Project Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **Performance Audit Fixes**
  - [x] **Sr. Developer:** Convert `Renci.SshNet.PrivateKeyFile` synchronous load operations in `SftpConnector` to async.
  - [x] **Gemini:** Refactor synchronous `cmd.ExecuteReader` calls inside `OdbcDataSource.cs` to use async overloads.
- [ ] **Security Audit Fixes**
  - [x] **Sr. Developer:** Ensure raw connection strings constructed in `ConnectionStringBuilder` do not log passwords or tokens.
- [ ] **Bug & Concurrency Fixes**
  - [x] **Gemini:** Handle potential `NullReferenceException` in `AzureBlobConnector.cs` when retrieving container lists with empty prefix.

## Orchestrator Project Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **Performance Audit Fixes**
  - [x] **Sr. Developer:** Implement parallel validation of active jobs in `SchedulerService` using a pool thread to avoid loop delays.
- [ ] **Bug & Concurrency Fixes**
  - [x] **Gemini:** Safely wait for the background task to cancel in `SchedulerService.Stop` instead of abandoning it instantly.
  - [x] **Gemini:** Wrap telemetry updates in `NodeHeartbeatService` with atomic operations to prevent concurrency conflicts.

## Report Portal Project Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **Security Audit Fixes**
  - [x] **Sr. Developer:** Verify that path guard checks in `PortalPathGuard` prevent relative path escape (`../`) consistently across Linux and Windows.
- [ ] **Bug & Concurrency Fixes**
  - [x] **Gemini:** Fix potential connection leak in `PortalDbContext` if dynamic connection strings are re-initialized in rapid succession.

## Language Server Project Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **Performance Audit Fixes**
  - [x] **Gemini:** Add debounce logic to `TextDocumentHandler` requests to prevent high CPU load on typing.
- [ ] **Bug & Concurrency Fixes**
  - [x] **Sr. Developer:** Handle concurrency exceptions when multiple text documents are opened simultaneously in `DocumentStateStore`.

## Remaining Projects Code Audit Tasks (v0.12.0 Stabilization)
- [ ] **TUI Project Audit Findings**
  - **Gemini:** Synchronous file I/O operations (`_fs.ReadAllLines`, `_fs.WriteAllText`) are called inside asynchronous methods (`LoadAsync` and `SaveAsync` in `EditorFileHandler.cs`), causing thread blocking.
  - **Sr. Developer:** Key bindings and layout rendering checks are performed synchronously on every frame, which can cause UI lag in larger consoles.
- [ ] **Report Builder & CLI Project Audit Findings**
  - **Gemini:** Synchronous directory creation (`Directory.CreateDirectory`) and file existence checks are performed inside asynchronous statement execution paths in `ExportReportStatementHandler.cs`.
  - **Gemini:** The CLI tool `Program.cs` performs synchronous file writes (`File.WriteAllText`) and deletions (`File.Delete`) during batch reports generation and cleanup.
- [ ] **Report Player & Hosting Project Audit Findings**
  - **Gemini:** `DashboardService.cs` contains synchronous cancellation calls (`_refreshCts?.Cancel()`) inside the async `DisposeAsync()` method.
  - [x] **Sr. Developer:** `ReportPlayer/Program.cs` contains synchronous file reads (`File.ReadAllText`) inside high-frequency minimal API endpoint routes, which block thread pool threads during server requests.
- [ ] **Reporting Project Audit Findings**
  - [x] **Sr. Developer:** `PdfExporter.cs` performs synchronous file writes and reads (`File.WriteAllBytes`, `File.ReadAllBytes`) when building PDF documents, instead of streaming asynchronously.
  - **Gemini:** `SnapshotStore.cs` performs synchronous file moves (`File.Move`) and synchronous deletions (`File.Delete`) inside async save/read workflows.
- [ ] **ETL-SQL.App Project Audit Findings**
  - **Gemini:** CLI startup logic performs synchronous file checking and console standard output writes before bootstrapping is complete.
  - [x] **Sr. Developer:** Init scaffolding (`InitScaffolder.cs`) and backup/restore services (`BackupRestoreService.cs`) perform synchronous directory creation and local file compression/decompression operations.
- [ ] **ETL-SQL.Orchestrator.Service Project Audit Findings**
  - [x] **Sr. Developer:** OrchestratorHostedService uses synchronous blocking lifecycle hooks on worker registration.
  - **Gemini:** Job API routing uses synchronous configuration parameter lookup.
- [ ] **ETL-SQL.ReportPortal.Data & Migrations Project Audit Findings**
  - [x] **Sr. Developer:** Dynamic connection configuration database migrations run synchronously on Startup. *(Sr. review: verified startup uses `MigrateAsync`.)*
  - **Sr. Developer:** Query compilation profiles do not support async initialization.
- [ ] **ETL-SQL.ReportRuntime Project Audit Findings**
  - **Gemini:** Visual resource JS/CSS sync logic (`sync-assets.js`) performs multiple synchronous file checks.
- [ ] **ETL-SQL.Installer Project Audit Findings**
  - **Gemini:** WiX configuration paths are hardcoded to historic version paths and require script updates during active release packaging.
