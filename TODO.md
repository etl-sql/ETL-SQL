# ETL-SQL Development TODO List

## VS Code Extension Audit (June 2026 Fresh Eyes Review)
### Single Responsibility Principle (SRP)
- [ ] **extension.ts bloat**: Refactor `extension.ts` to separate concern groups. It spans over 1,000 lines handling command registration, workspace events, process warmth, configuration checks, and Unix permission adjustments. These should be split into modules (e.g. `permissions.ts`, `terminalCommandBuilder.ts`, `cleanupService.ts`).
- [ ] **WelcomeView path resolution coupling**: Decouple `WelcomeView.ts` from direct knowledge of local/online files. The path resolver logic should be moved into a shared helper module.

### Logging
- [ ] **Webview logger interfaces**: Webviews (e.g. `ResultsPanel`, `ReportPreviewPanel`) write directly to browser console (`console.error`, `console.warn`). They should post messages back to the extension host to write to the unified `ETL-SQL` output channel for consolidated developer diagnostics.
- [ ] **Silent warmup failures**: Warmup process failures in `extension.ts` (`warmupRepl`) are caught and silenced. While intentional for happy-path user experience, recording warning telemetry in the output channel would greatly simplify troubleshooting of environment-related launch issues.

### Security
- [x] **Cryptographic nonces in Webviews**: `resultsPanel.ts` and `sidebarProvider.ts` use `Math.random()` to generate nonces. While not highly sensitive, they should align with the standard in `reportPreviewPanel.ts` and `reportDesignerPanel.ts` which use `crypto.randomBytes(16).toString('base64url')` to prevent potential predictable-generator collisions.
- [ ] **Unprotected globalState store**: Storing connections in globalState (`etlsql.connections`) is currently unused but left in code. If global connection storage is reintroduced, it must use the VS Code `SecretStorage` API to protect credential values rather than simple global state JSON strings.

### Performance
- [x] **Webview HTML loading cache**: Both `resultsPanel.ts` and `sidebarProvider.ts` synchronously read `index.html` from disk (`fs.readFileSync(...)`) on every webview resolution. Caching this string in memory after the first read will improve panel loading and UI render responsiveness.
- [x] **Warmup concurrency lock**: Warmup and execute requests do not share a state lock. If a user quickly presses execute while warmup is starting, it may result in duplicate process spawn attempts.

### Linting
- [x] **Clean remaining ESLint warnings**: 9 warnings exist in the workspace (inactive variable/exception parameters in `connectionsProvider.ts`, `sidebarProvider.ts`, and test files). These should be fixed to maintain a strictly zero-warning lint build.

## Full C# Engine & Connectors Audit (June 2026 Fresh Eyes Review)
### Single Responsibility Principle (SRP)
- [ ] **Evaluator class orchestrator bloat**: `src/ETL-SQL.Engine/Evaluator.cs` spans 1,584 lines, performing too many distinct roles. It manages script execution state (variables, temp tables), parses commands, evaluates expressions, coordinates statement handler routing, performs data validation (`IDataValidator`), and manages disk spilling (`ISpillable`). Recommend refactoring execution state management, expression evaluation, and spilling logic into separate, focused classes.
- [ ] **ReportsController service bloat**: `src/ETL-SQL.ReportPortal/Controllers/ReportsController.cs` has 1,772 lines. It conflates controller/routing logic with folder ACL/permission management, SQL execution tracking, page construction, caching/synchronization, metadata catalogs, and lineage extraction queries. Recommend decomposing these operations into dedicated domain services (e.g., `FolderPermissionService`, `ReportService`, `LineageQueryService`).

### Logging
- [ ] **Trace output in shared libraries**: Standardize all logging to use the `ILogger` interface injected via DI or obtained from `IExecutionContext`. Ensure no raw `Console.WriteLine` calls remain in class libraries (e.g., check `ResultFormatter.cs`, `EngineLogger.cs`).
- [ ] **Warmup & connection telemetry gaps**: Telemetry for connector initialization or session warmup failures is caught silently to prioritize happy-path UX. Recording warning logs/telemetry to a diagnostic sink is critical for troubleshooting deployment issues.

### Security
- [ ] **File-type verification bypass in FileTransferStatementHandler**: While wildcard file transfers validate file extensions using `SecurityService.ValidateFileType(localFile)` to block prohibited types (like `.exe`, `.bat`), single-file `SEND` and `RECEIVE` (upload/download) operations only call `ValidateWriteAccess(localPath)` (for Receive) or check if the file exists (for Send) but bypass `ValidateFileType`. This creates a vulnerability where sensitive or executable files could be transferred directly.
- [ ] **Unsanitized database exception leaks**: Metadata catalog providers (`SqlServerCatalogProvider.cs`, `PostgresCatalogProvider.cs`, `MySqlCatalogProvider.cs`) execute raw SQL queries against system catalogs. If database exceptions occur, they propagate the raw database provider exceptions (such as `SqlException` or `NpgsqlException`) directly to the caller, violating the Exception Boundary Rule. All provider exceptions must be caught and wrapped in `ExecutionException` with sanitized messages.

### Performance
- [ ] **Sync-over-async blocking threads**:
  - `src/ETL-SQL.ReportPortal/Controllers/ExecutionController.cs:L319` makes a synchronous `.Wait()` call on `audit.LogAsync(...).Wait()`. This blocks ASP.NET Core request threads, creating a high risk of thread pool starvation and deadlocks.
  - `src/ETL-SQL.Core/Data/DataModel.cs:L495` calls `AddRowAsync(row).GetAwaiter().GetResult()` inside the obsolete synchronous `AddRow` wrapper.
- [ ] **Synchronous blocking calls in SftpConnector**:
  - `src/ETL-SQL.Connectors/SftpConnector.cs` invokes synchronous SSH.NET methods like `Client.Connect()`, `Client.ListDirectory()`, `Client.Exists()`, and `Client.DeleteFile()` directly within async execution paths (such as `EnsureConnected()` inside `ListFilesCoreAsync`) without wrapping them in `Task.Run` or leveraging async equivalents. This stalls the async enumeration threads during network-bound calls.

### Cross-Platform & Environment Gotchas (Mac/Linux)
- [ ] **Missing ClearScript V8 native packages for macOS / ARM64**: In `Directory.Packages.props`, only the `win-x64` and `linux-x64` native runtime dependencies of `Microsoft.ClearScript.V8` are included. Running server-side ECharts rendering on macOS (Intel/Apple Silicon) or ARM-based Linux containers will crash with native library loading failures. Need to add `Microsoft.ClearScript.V8.Native.osx-x64`, `Microsoft.ClearScript.V8.Native.osx-arm64`, and `Microsoft.ClearScript.V8.Native.linux-arm64` to complete cross-platform runtime support.
- [ ] **Path separator normalization issues**: In case-sensitive Unix systems, paths resolved across different connectors (e.g. Sftp, FlatFile, Excel) must ensure proper backslash-to-slash character translation and case consistency.

