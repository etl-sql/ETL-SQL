# ETL-SQL: AI Agent Instruction Manual

Welcome, Agent. You are assisting in the development and operation of **ETL-SQL**, a hybrid engine that executes SQL-like syntax against diverse data sources (SQL, NoSQL, FlatFiles) with an emphasis on portability and "Zero-Trust" security.

**This file is the single standard for every agent working in this repo** — Claude, Codex, and Gemini all follow it. Harness-specific notes live in the sibling file for that tool (e.g. `CLAUDE.md`) and must never restate or contradict anything here.

| Section | Use it for |
| :--- | :--- |
| [1](#1-the-mental-model)–[5](#5-script-composition) | Writing `.etlsql` / `.rptsql` scripts |
| [6](#6-documentation-routing)–[7](#7-documentation-stewardship-rules) | Finding and writing documentation |
| [8](#8-code-principles-by-surface)–[10](#10-engine-architecture-patterns) | Modifying C#, HTML, JavaScript, TypeScript, and VS Code source |
| [11](#11-third-party-dependency-policy)–[19](#19-cross-platform-line-endings--pre-push-validation) | Dependencies, assets, releases, and gates |

## 0. Writing Style

Applies to chat responses, commit messages, docs, and code comments.

- Casual, direct, peer-to-peer tone. Short sentences.
- Focus on utility. Do not over-explain basics or state the obvious.
- No final summary or concluding paragraph unless asked for one.
- No corporate buzzwords: "leverage", "seamless", "robust", "delve", "testament", "beacon", "foster".
- No structural clichés: "harnessing the power of", "in today's digital landscape", "at the intersection of".
- Avoid the "X rather than Y" closing sentence structure.
- User-facing docs refer to **release versions**, never internal phase or slice names (those belong in `TODO.md`, `ROADMAP.md`, and design docs).

---

## 1. The Mental Model

ETL-SQL is an orchestration engine, not a traditional database. It owns session variables and
`#temp` tables, evaluates ETL-SQL language features, and coordinates reads and writes across
heterogeneous connections.

- **Engine context** — variables, `#temp` tables, control flow, portable transformations, validation,
  lineage, and cross-source work.
- **Remote context** — SQL executed by a named database connection. An `EXECUTE ... BEGIN ... END`
  block is passed to that connection in its native dialect.
- **Data movement** — cross-source data flows through the engine. Stage it before transforming,
  validating, masking, or loading it elsewhere.

When writing user-facing material, keep the product position clear: ETL-SQL is script-first and
source-control friendly; it keeps transformation visible and portable instead of locking it into one
warehouse; and lineage, tags, validation, and zero-trust operations are part of the execution model.

---

## 2. Script Authoring Rule — Read the Focused Reference

Do not treat this file as a syntax manual. Before writing or reviewing `.etlsql` or `.rptsql`, read
[the syntax index](docs/syntax-index.md) and the focused reference page for every nontrivial statement,
function, connector, or visual involved.

The parser, immutable AST, canonical formatter, tests, and focused reference page are authoritative.
If they disagree, inspect the implementation and fix the stale surface. Do not infer current syntax
from old samples, roadmap prose, release notes, or examples in `AGENTS.md`.

Use these starting points:

| Task | Required starting point |
| :--- | :--- |
| Statements and clauses | [Statement Reference](docs/reference/statements/README.md) |
| Connectors and authentication | [Data Connectors](docs/reference/connectors/README.md) |
| Functions and data types | [Standard Library](docs/reference/functions/README.md) |
| File operations and transfer | [File Operations](docs/reference/file-operations/README.md) |
| Report-SQL and visuals | [Report-SQL Guide](docs/guides/feature-guides/report-sql.md) |
| Lineage and data quality | [Lineage](docs/reference/statements/session-control/lineage.md) and [Data Quality Guides](docs/guides/data-quality/README.md) |
| Production and HA | [Administration](docs/administration/platform/README.md) |

---

## 3. Zero-Trust Security Guardrails

Do not generate or approve ETL-SQL that:

- Reads from or writes to `.sql`, `.etlsql`, or `.rptsql` script files.
- Accesses system directories such as `C:\Windows`, `C:\bin`, `/etc`, or `/root`; a drive root;
  `.git`; or local `.ssh` content. SFTP and explicit `KEYFILE` use remain governed by their focused
  references and path policy.
- Exceeds 100 file operations or five recursion levels without explicit
  `SET ALLOW_FILE_OPERATIONS` or `SET ALLOW_RECURSIVE_LAYERS` authorization.
- Logs, prints, concatenates, serializes, or otherwise exposes connection strings, passwords, API
  keys, raw secret values, `SECRET:` references, or `ENC:` values.
- Uses `DELETE`, `MERGE`, `TRUNCATE`, or destructive file operations without either a
  `SET WHAT_IF ON` validation pass or an explicit transaction/rollback guard.

Before authoring destructive work, read the applicable statement reference and
[connector/security standard](docs/architecture/standards/Connectors_Standards.md). Keep the safety
mechanism visible in the script.

---

## 4. Engine and Remote Dialects

Dialect validity depends on where an expression executes.

- In an `EXECUTE connection BEGIN ... END` block, write the target system's native SQL. ETL-SQL
  passes the block through.
- A query against a remote connection must satisfy that connector's supported dialect and pushdown
  rules. Run lint; do not assume T-SQL keywords are portable.
- Queries over `#temp` tables and file-backed data run in engine context and use ETL-SQL semantics.
- When portability or execution context is uncertain, stage remote data into a `#temp` table and
  perform cross-source transformations in the engine.

Do not maintain keyword compatibility tables here. The connector reference, dialect metadata, linter,
and tests own those details.

---

## 5. Script Composition

Use the smallest pattern that preserves correctness and portability:

- A simple inspection query may read directly from one source.
- Cross-source ETL stages source columns into an engine `#temp` table, transforms and validates
  there, then loads or merges into the destination.
- Keep database-specific work inside an explicit remote execution block.
- Validate destructive operations with `WHAT_IF` or a transaction/rollback guard before execution.
- Use connector-backed file operations, use explicit allowed paths, check existence before destructive
  file work, and encrypt sensitive content before transfer.
- Use `CREATE JOB` for recurring work and `RUN SCRIPT` for composable modules.

For complete runnable patterns, use the [ETL Cookbook](docs/cookbooks/etl/README.md) and
[Script Composition Standards](docs/architecture/standards/Script_Composition_Standards.md).

---

## 6. Documentation Routing

Use the narrowest authoritative document for the task. Do not copy inventories or syntax into other
documents when a canonical reference already owns them.

| Need | Source |
| :--- | :--- |
| Learn the pipeline model | [Getting Started](docs/guides/onboarding/getting-started.md) |
| Find shipped syntax | [Syntax Index](docs/syntax-index.md) |
| Find runnable samples | [Sample Guide](docs/guides/patterns/sample-guide.md) |
| Author reports | [Reporting Guides](docs/guides/reporting/README.md) |
| Operate Portal or Orchestrator | [Administration](docs/administration/platform/README.md) |
| Understand engine internals | [Engine Architecture](docs/architecture/Engine.md) |
| Change parser or syntax | [Parser/Lexer Architecture](docs/architecture/ParserLexer.md) and [Language Syntax Standards](docs/architecture/standards/Language_Syntax_Standards.md) |
| Change reporting | [Reporting Architecture](docs/architecture/Reporting.md) and [Reporting Semantic Contracts](docs/architecture/ReportingSemanticContracts.md) |
| Change Portal, LSP, or VS Code | [Portal](docs/architecture/Portal.md), [Language Server](docs/architecture/LanguageServer.md), and [VS Code Extension](docs/architecture/VSCodeExtension.md) |
| Change connectors | [Connector Architecture](docs/architecture/Connectors.md) and [Connector Standards](docs/architecture/standards/Connectors_Standards.md) |
| Change deployment behavior | [Deployment Profiles](docs/architecture/DeploymentProfiles.md) |
| Cut a release | [Release Checklist](docs/releases/release-checklist.md) |

Architecture documentation must match the live source. Grammar documentation must match parser-tested
syntax. Check `TODO.md` and `ROADMAP.md` before describing unfinished work as shipped.

---

## 7. Documentation Stewardship Rules

When you create or modify any documentation, follow these standards:

- **Standard Library**: Every function entry must include its full **signature**, **return type**, and a **copy-pasteable example**. Never add a function without all three.
- **Data Connectors**: Always include **both authentication patterns** (e.g., Password vs. Keyfile for SFTP) and call out any **mutually exclusive options** explicitly.
- **Cookbook**: Prefer **self-contained lifecycle scripts** (Extract → Stage → Validate → Merge → Cleanup → Notify) over isolated snippets. Every recipe must be runnable as-is.
- **Architecture docs**: Any interface contract shown must match the actual C# source. Cross-reference before writing.
- **Grammar docs**: Every syntax form must have a minimal working example. Do not document syntax that the parser does not actually accept.

### 7.1 Help Documents Formatting Standards

Help documents located under `docs/reference/` are loaded dynamically by the engine and LSP server to serve hover tooltips and in-editor help. Because editor hover viewports are narrow and automatically collapse single-newline paragraphs, all help documents must adhere to these consistency rules:

- **Title Header**: The file must start with a level-1 header `# TOPIC` matching the exact keyword or visual type (e.g., `# PAGE` or `# TABLE`).
- **Description Paragraph**: A short, single-paragraph description immediately following the title header, explaining what the command or feature does.
- **Syntax Block**: The statement syntax MUST be placed inside a code-fenced block ` ```sql ... ``` ` to prevent line collapsing and preserve structure, indentation, and casing.
- **Lists and Options**: All lists of types, mappings, configuration options, or actions MUST be formatted as Markdown bullet points (`- **OptionName** — Description`).
  - Never use raw leading-space indentation (e.g., `  PAGE_SIZE — rows per page`) as it collapses into a single paragraph in editor hover cards.
  - Bold the option/parameter name (e.g., `- **PAGE_SIZE = n** — ...`).
- **Examples**: Provide one or two clean, copy-pasteable example blocks using ` ```sql ... ``` ` to illustrate common use cases.
- **References**: Always end the document with a `References` section pointing to the official manuals or specifications (e.g., `- [Report SQL Guide](docs/guides/feature-guides/report-sql.md)`).

### 7.2 Snippet Formatting Standards

Snippet files located under `snippets/` are used by the LSP server to generate autocomplete templates. They must adhere to this exact structure:

- **Frontmatter**: Every snippet file must start with a YAML frontmatter delimited by `---` containing exactly:
  - `trigger`: The autocomplete trigger keyword, which MUST start with a `$` (e.g., `trigger: $dataset`).
  - `label`: A short visual title of what the snippet creates (e.g., `label: CREATE DATASET &name`).
  - `description`: A brief description shown in the autocomplete suggestion list.
- **Placeholder Syntax**: Use French quotation marks `«` and `»` around placeholder/tabstop names (e.g., `«VisualName»`, `«1h»`). These allow the editor to cycle through fields using `Tab`.
- **Formatting**: Indent block statements using 2 spaces for SQL clauses, options, or mappings. Keep them clean, valid, and consistent with core syntax.

---

## 8. Code Principles by Surface

### 8.1 C# and Engine Rules

These rules apply when editing C# source. Engine-specific rules are identified explicitly:

- **Path Resolution**: Never use relative paths in engine code. Always call `IExecutionContext.ResolvePath()` before any file I/O — this is the Zero-Trust security boundary.
- **Logging**: `Logger.Instance` is **obsolete**. Always use the `ILogger` provided via dependency injection or pulled from `IExecutionContext`. Do not use `Console.WriteLine`.
- **AST Nodes**: Prefer `record` types for all AST node classes to enforce immutability. Do not use mutable `class` declarations for nodes.
- **Async**: All I/O calls must use the `Async` overloads with a `CancellationToken`. No `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` in connector or handler code.
- **Exceptions**: Connector-level provider exceptions (`SqlException`, `NpgsqlException`, etc.) must be caught and re-thrown as `ExecutionException` with a sanitized message. Never let raw provider exceptions escape the connector boundary. Reserve exceptions for exceptional conditions — do not use them for ordinary control flow. The codebase has no `Result<T>`/`ErrorOr` library and adding one falls under [§11](#11-third-party-dependency-policy).
- **C# style**: Use file-scoped namespaces. Prefer primary constructors when they improve clarity and match the surrounding project. Use constructor injection; do not introduce service-locator lookups. Use 4-space indentation and follow `.editorconfig`; `dotnet format` runs from the pre-commit hook.
- **Web surfaces**: Prefer Minimal API `Map*` methods over controllers for new endpoints.
- **Tests**: Use xUnit assertions and follow the surrounding test project's conventions. Do not introduce a second assertion library without following [§11](#11-third-party-dependency-policy). `INT`/`TINYINT`/`BIGINT` all store as `decimal` at runtime — assert with an `m` suffix or `Convert.ToDecimal`, never `int`/`long`/`byte` literals.

### 8.2 Connector-Specific Rules

These apply when authoring or modifying any `IConnector` / `IDataSource` implementation:

- **Option naming**: All `WITH()`-style option keys must be **UPPERCASE with underscores** (`TIMEOUT_SECONDS`, `MIN_POOL_SIZE`, `CREDENTIAL_FILE`). Never PascalCase, camelCase, or mixed.
- **PASSWORD for ETL-SQL-owned options; vendor-native names are allowed**: Use `PASSWORD` for ETL-SQL-created connector password options and do not add convenience aliases such as `PWD` for those options. Vendor-native pass-through syntax is exempt when intentionally exposed by a connector; for example, ODBC `PWD` is valid because it is part of the ODBC connection-string vocabulary. The `ConnectionStringBuilder` should map ETL-SQL `PASSWORD` → the driver-native keyword when building structured connection strings.
- **Timeout defaults**: OLTP connectors (MSSQL, Postgres, Oracle, MySQL, SQLite, ODBC) default to **30 seconds**. Data warehouse connectors (Snowflake, BigQuery) default to **1800 seconds** (30 min). Both must read `TIMEOUT_SECONDS` from options to allow override.
- **Constructor signature**: `IDataSource` implementations use `(IExecutionContext context, string connectionString, string? tableName, Dictionary<string, string>? options)` — this order exactly.
- **ResolvePath for file path options**: Any connector option accepting a file path (`CREDENTIAL_FILE`, `KEY_FILE`, `CERT_FILE`, `PRIVATE_KEY_FILE`, `PATH`) must call `context.ResolvePath()` before any I/O, even when the connector is not in the engine tier.
- **No aliases / no legacy fallbacks**: If two option names existed, pick one and remove the other. Product has no live users — consistency now costs nothing.
- **CREATE CONNECTION syntax**: Options go directly in parentheses after the type name. `WITH()` is not used on `CREATE CONNECTION`. `WITH()` is valid in CTEs and `ALTER CONNECTION` only.

For the full 10-inviolable-rules + 25-item checklist, see **[Standards/Connectors_Standards.md](./docs/architecture/standards/Connectors_Standards.md)** and **[Architecture/Connectors.md](docs/architecture/Connectors.md)**.

### 8.3 HTML, JavaScript, TypeScript, and VS Code Rules

The repository has three browser/extension surfaces. Follow the conventions and toolchain of the surface you are changing:

| Surface | Source | Stack | Targeted validation |
| :--- | :--- | :--- | :--- |
| VS Code extension | `src/etl-sql-vscode/src/` | Strict TypeScript, CommonJS, ES2020 | From `src/etl-sql-vscode`: `npm run compile`, `npm run lint`, `npm run test:unit` |
| VS Code UI | `src/etl-sql-vscode/ui/src/` | React, TSX, Vite, browser DOM | From `src/etl-sql-vscode/ui`: `npm run lint`, `npm run build`, `npm run test:unit` |
| Portal/report runtime | Portal `wwwroot` sources and `src/ETL-SQL.ReportRuntime/Resources/Shared/` | HTML, CSS, browser JavaScript | Use the UI sandbox and the asset-sync checks in [§12](#12-shared-report-runtime-assets) |

- Keep TypeScript strict. Do not weaken compiler or ESLint rules to make a change pass.
- Preserve the module system and JavaScript target configured by the package you are editing; the extension and UI use different targets.
- Prefer typed messages and shared contracts at extension/webview boundaries. Validate untrusted message and API payloads before use.
- Preserve keyboard navigation, focus behavior, labels, semantic HTML, and existing accessibility tests when changing UI.
- Avoid unsafe HTML injection. Use DOM APIs or framework rendering for untrusted content; if raw HTML is required, use the existing sanitization boundary.
- Add or update focused Vitest tests for TypeScript/React behavior. Add or extend a UI sandbox story for browser-side visual or interaction changes.
- Do not edit generated report-runtime copies. Follow the canonical asset workflow in [§12](#12-shared-report-runtime-assets).

---

## 9. Build, Test & Run

Solution file: `ETL-SQL.slnx`. The canonical engine/CLI composition root is `DependencyInjectionSetup.BuildServiceProvider()` in `src/ETL-SQL.App/App/DependencyInjectionSetup.cs`. Host-specific roots, such as the TUI setup, may add presentation services; focused tests may construct isolated service collections. Configuration lives in `src/appsettings.json` (engine tuning, security boundaries, connector defaults, logging).

```bash
# Build
dotnet build ETL-SQL.slnx

# Default lane — excludes Docker integration, long-running perf, the SLT corpus,
# the randomized fuzzer (non-deterministic, own lane), and the Playwright browser lane
dotnet test ETL-SQL.slnx --filter "Category!=Integration&Category!=Performance&Category!=SLT&Category!=Fuzz&Category!=Browser"

# Single class / single method
dotnet test ETL-SQL.slnx --filter "FullyQualifiedName=ETL_SQL.Tests.AliasTests"
dotnet test ETL-SQL.slnx --filter "FullyQualifiedName=ETL_SQL.Tests.AliasTests.TestFileAlias"

# Coverage (requires the dotnet-reportgenerator local tool)
dotnet test ETL-SQL.slnx --collect:"XPlat Code Coverage" --results-directory ./coverage
dotnet reportgenerator -reports:"./coverage/**/coverage.cobertura.xml" -targetdir:"./coverage/report" -reporttypes:"Html;TextSummary"
```

Never run the solution-wide suite while iterating — scope to one test project plus a filter.

**Opt-in lanes** (excluded from the default run):

| Lane | Command | Why it is separate |
| :--- | :--- | :--- |
| Portal | `--filter "Category=Portal"` | `WebApplicationFactory`, no Docker — also included in the default run |
| Browser | `pwsh -File scripts/test-lane.ps1 -Lane browser` | Real Chromium against a Kestrel-hosted Portal; downloads ~113 MB on first run. Set `ETLSQL_PLAYWRIGHT_SKIP_INSTALL=1` when cached |
| Integration | `--filter "Category=Integration"` | Needs Docker |
| Performance | `--filter "Category=Performance"` | Crash risk — `TestLargeDatasetMemory` uses 1M rows |
| SLT corpus | `--filter "Category=SLT"` | OOM risk on large files; run manually to pinpoint failures |
| Fuzz | `--filter "Category=Fuzz"` | Grammar-driven generation + NoREC oracle. Deterministic: `ETLSQL_FUZZ_SEED` reproduces a failure, `ETLSQL_FUZZ_ITERATIONS` scales it (default 500) |

**Sandbox lifecycle evidence** — build the worker image first; the lanes skip with a precise diagnostic when the image or runtime is missing, and never silently downgrade.

```bash
pwsh -File scripts/Test-SandboxWorkerImage.ps1 -Tag etlsql-sandbox-worker-test:local

# Standard tier (ordinary runc) — NOT a hostile-tenant boundary result
dotnet test ETL-SQL.slnx --filter "FullyQualifiedName~DockerStandardSandboxLifecycleTests"

# Hardened tier (gVisor/Kata, digest-pinned image) — the citable Hardened evidence.
# Linux host only; prepare once, then export the pinned image reference:
#   sudo bash scripts/enable-hardened-sandbox-lane.sh
#   export ETLSQL_SANDBOX_WORKER_DIGEST_IMAGE=$(cat /tmp/etlsql-pinned-worker-image)
dotnet test ETL-SQL.slnx --filter "FullyQualifiedName~DockerHardenedSandboxLifecycleTests"
```

**CI gate:** 70% minimum line coverage (`scripts/Test-CoverageGate.ps1`). Test projects that include `xunit.runner.json` disable assembly and collection parallelism. Do not assume the same setting in projects without that file.

### Running the application

```bash
dotnet run --project src/ETL-SQL.App -- run MyScript.etlsql        # headless
dotnet run --project src/ETL-SQL.App -- ui edit MyScript.etlsql    # TUI editor
```

### Environment assumptions

.NET 10 with `LangVersion=latest`, ASP.NET Core (Minimal APIs preferred for new services), and EF Core over SQLite and PostgreSQL. Development is on Windows 11 with PowerShell 7+; give local commands in `dotnet` CLI + PowerShell form with backslash paths. Production runs cross-platform, so nothing in the engine may assume Windows. Tests use xUnit and the assertion style already present in the target test project. Secrets go in user-secrets or `.env` — never hardcoded.

---

## 10. Engine Architecture Patterns

### Dependency tiers

Strictly layered — a project may reference only its own tier or lower. This is **enforced**, not aspirational: `ArchitectureBoundaryTests` asserts the live `src/*.csproj` reference graph against the table below. Existing upward edges are pinned in allow-lists tied to open `TODO.md` items; the lists may only shrink, and a new upward edge fails CI immediately. Update that test in the same change if you move a project between tiers.

| Tier | Project(s) | Role |
| :--- | :--- | :--- |
| 0 | `Core` | Parser, AST, interfaces (`IExecutionContext`, etc.), shared language contracts |
| 1 | `Analysis`, `Engine`, `Reporting.Contracts` | Lint/explain/diagnostics; evaluator, statement handlers, expression evaluator, external engines |
| 2 | `Reporting`, `ReportBuilder`, `ReportRuntime`, `Portal.Data`, `Connectors.Common` | Report-SQL compilation, report authoring model, browser runtime assets, portal catalog schema. Engine must not consume these |
| 3 | `Connectors` (+ `.Cloud`, `.Databases`, `.Files`, `.Messaging`, `.Remote`), `Infrastructure.Docker`, `.Logging`, `.Sqlite`, `Orchestrator`, `ReportHosting`, `Portal.Migrations.Postgres`, `Gateway` | Provider I/O, container/logging/local-state infrastructure, job scheduler, execution history, leases/fencing, reusable report session hosting, outbound egress |
| 4 | `LanguageServer`, `Orchestrator.Service`, `ReportPlayer`, `WorkstationEditor` | LSP; Windows Service / systemd daemon wrapping the Orchestrator; standalone report host |
| 5 | `Portal` | Dashboard HTTP server, catalog and admin API |
| 6 | `App`, `TUI` | CLI entry point (System.CommandLine), Spectre.Console terminal IDE |
| 7 | `ReportBuilder.CLI`, `TenantValidator` | Top-level tools |

Each per-domain connector group references only `Core` and `Connectors.Common` — never `Engine`.

### Parser → AST → Evaluator

1. `Lexer` tokenizes the script into `TokenType` tokens.
2. `StatementParser` (recursive descent, split across partial classes by domain) converts tokens to an AST.
3. `Evaluator.Evaluate(Script)` walks the AST, dispatching each `Statement` to a registered `IStatementHandler` via a `Dictionary<Type, IStatementHandler>`.

AST nodes are `record` types inheriting from `AstNode` (which tracks source location). `Script` contains `List<Statement>`.

### Adding a statement

Every handler implements `IStatementHandler` and declares `Type SupportedStatementType`; handlers are registered via DI and auto-discovered into the dispatch table. A new statement needs all five:

1. A `record` AST node in `ETL-SQL.Core/Ast.cs`
2. A `TokenType.cs` entry (if a new keyword is involved)
3. A parser case in the matching `StatementParser.*.cs` partial
4. An `IStatementHandler` implementation in `ETL-SQL.Engine/Handlers/`
5. DI registration in `src/ETL-SQL.App/App/DependencyInjectionSetup.cs`

Naming rules for the record and handler are in [§17](#17-syntax-consistency-rules).

### Evaluator as execution context

`Evaluator` implements ~8 context interfaces at once (`IExecutionContext`, `IVariableContext`, `IQueryContext`, `ILineageContext`, `ISqlCompilerContext`, `ITransactionContext`, `IDockerContext`, `ILoggingContext`). Handlers receive `IExecutionContext` and downcast only to the interface they actually need.

### External engines (disk-spilling)

Four engines take over when row counts exceed the thresholds in `appsettings.json → Engine` (default 100k rows per operation, tunable):

- `ExternalAggregateEngine` — GROUP BY with disk spill
- `ExternalJoinEngine` — hash-based JOIN with disk spill
- `ExternalWindowEngine` — PARTITION BY streaming
- `ExternalSortEngine` — chunked sort

Spill behavior affects determinism — see [§18](#18-triage--defect-principles--a-wrong-answer-outranks-a-crash) before changing it.

---

## 11. Third-Party Dependency Policy

Use only free and open-source software for new third-party libraries, tools, and bundled assets unless the user explicitly approves an exception.

- Prefer OSI-approved licenses such as MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, MPL-2.0, EPL-2.0, LGPL, or GPL-compatible licenses that fit the distribution model.
- Do not add proprietary, source-available-only, noncommercial, trial, paid, freemium-gated, or revenue-threshold licenses without calling out the license and asking first.
- Before adding or upgrading a dependency, check its license metadata and update `THIRD-PARTY-NOTICES.md` and `THIRD-PARTY-INVENTORY.md` when applicable.
- Preserve license and copyright banners in bundled JavaScript, CSS, fonts, images, and generated browser assets.
- Existing non-FOSS or commercially conditioned dependencies are grandfathered only until replaced; do not expand their use without explicit approval.

### 11.1 Before Adding Any NuGet Package

1. **Search first**: Check `Directory.Packages.props` — if a package already handles the need, use it. Do not add a second library for the same domain.
2. **Try BCL/framework first**: If `System.*` or `Microsoft.*` provides the capability, use it. External packages are for capabilities the framework doesn't cover.
3. **One library per domain**: Any second library doing the same job as an existing one requires explicit justification.

**Four-question evaluation checklist:**
1. License: OSI-approved? If not, stop — existing policy.
2. Maintenance: Last commit within 12 months? If abandoned, find an alternative.
3. Transitive deps: Run `dotnet list package --include-transitive` after adding — any conflicts?
4. Necessity: Can the need be met with ~50 lines of BCL code? If yes, write it inline.

**What to update when adding a library:**
- `Directory.Packages.props` — add version under the appropriate property group (`$(MicrosoftExtensionsVersion)`, `$(SerilogVersion)`, etc.)
- `THIRD-PARTY-NOTICES.md` — license attribution
- `THIRD-PARTY-INVENTORY.md` — inventory entry

**Approved library table (one per domain — do not duplicate):**

| Domain | Approved | Do Not Add |
| :--- | :--- | :--- |
| JSON | `System.Text.Json` (BCL) | Newtonsoft.Json |
| Logging | `Serilog` | NLog, log4net, direct console sinks |
| Session store | `Microsoft.Data.Sqlite` | EF Core for engine use |
| PGP crypto | `PgpCore` | Standalone BouncyCastle |

---

## 12. Shared Report Runtime Assets

The report browser runtime has exactly one source of truth:

```
src/ETL-SQL.ReportRuntime/Resources/Shared/
```

Files copied under these host folders are generated sync outputs and must not be edited directly:

- `src/ETL-SQL.ReportPlayer/wwwroot/`
- `src/ETL-SQL.Portal/wwwroot/js/`
- `src/ETL-SQL.Portal/wwwroot/css/`
- `src/etl-sql-vscode/media/`

When changing report runtime JavaScript, CSS, themes, or shared browser dependencies:

1. Edit the canonical file in `src/ETL-SQL.ReportRuntime/Resources/Shared/`.
2. Run `node .\scripts\sync-assets.js`.
3. Run `node .\scripts\sync-assets.js -Check`.

Do not "fix" drift by editing generated host copies. The check step compares host copies to the canonical shared source and will fail if they diverge.

### Prototyping browser-side UI (no Docker)

Before changing a browser-side report/portal component, prototype and visually verify it in the **UI sandbox** at `tools/ui-sandbox/` (`pwsh -File tools\ui-sandbox\serve.ps1`) — do **not** spin up Docker or the full portal just to eyeball a JS/CSS change. It is a no-build "stories" harness that imports the canonical/source files directly (cache-busted on **↻ Reload**), so an edit shows immediately with no sync, no portal build, and no catalog DB. It hosts the `designer.js` exports (`renderDag`, `createScriptEditor`, `createDesigner`) and extracted portal UI modules (e.g. `src/ETL-SQL.Portal/wwwroot/js/lineage-ui.js`); each surface is a story under `tools/ui-sandbox/stories/` driven by fixture data, with an injectable mock fetch (`mockApi.js`) for API-backed components. Add or extend a story when you change a surface. The sandbox is dev-only and does **not** replace the sync step above.

---

## 13. Source Boundary Rules for Agents

Before moving source files, projects, report runtime assets, or host-owned behavior, read **[Source_Boundary_Migration_Plan.md](./docs/architecture/roadmaps/Source_Boundary_Migration_Plan.md)**.

- Keep Core focused on shared language contracts, Engine focused on execution, Connectors focused on provider I/O, and host shells focused on hosting.
- Move linting, lineage, explain, dialect checks, help verification, and diagnostics toward `ETL-SQL.Analysis` in small, testable steps.
- Keep report semantics in the reporting layer; ReportPlayer, Portal, and VS Code should host reports, not fork manifest, style, visual, page, dataset, or chart behavior.
- Keep reusable report session hosting in `ETL-SQL.ReportHosting`; ReportPlayer and Portal may consume it, but Portal should not depend on Player for execution/session behavior.
- Preserve the VS Code extension's ecosystem-facing `src/etl-sql-vscode` folder/package naming unless there is a deliberate release plan.
- Do not start source cleanup with a broad restructure. Prefer one ownership boundary at a time, update docs/tests with the move, and leave compatibility shims while hosts migrate.

---

## 14. Developer Workflows & Utility Scripts

To assist in local development, compiling, and executing test suites, the repository includes several core scripts under the `scripts/` folder. Both PowerShell (`.ps1` for Windows) and Bash (`.sh` for Linux/macOS) scripts are provided.

For full usage and script details, refer to **[scripts/README.md](./scripts/README.md)**.

### Key Scripts Reference:
- **Build Debug Environment:** Compiles the .NET solution, Vite React UI components, and the VS Code TypeScript extension.
  - Windows: `.\scripts\build-debug.ps1`
  - Linux/macOS: `./scripts/build-debug.sh`
- **Smoke Tests:** Runs targeted categories of fast smoke tests (Core, Security, Reporting, Portal).
  - Windows: `.\scripts\test-smoke.ps1 -Lane all`
  - Linux/macOS: `./scripts/test-smoke.sh --lane all`
- **General Test Lanes:** Gateway script for named suites. Use `scripts/README.md` or the script's help for the current lane inventory.
  - Windows: `.\scripts\test-lane.ps1 -Lane fast`
  - Linux/macOS: `./scripts/test-lane.sh --lane fast`
- **SQLite Logic Tests (SLT) Corpus:** Runs the SLT verification engine against corpus files, writing output to teed timestamped logs in `slt_results/`.
  - Windows: `.\scripts\Test-SltCorpus.ps1 -CorpusOnly`
  - Linux/macOS: `./scripts/Test-SltCorpus.sh --corpus-only`
- **TRX Results Summarizer:** In Windows PowerShell, run `.\scripts\Parse-SltResults.ps1` to print a color-coded test run summary directly to the command prompt.

---

## 16. Breaking Change Protocol

A **breaking change** is any modification that could produce different results for identical script input. This includes:
- **Syntax changes** — keyword renamed, clause made required, operator removed
- **Semantic changes** — same syntax, different output (the silent and most dangerous kind)
- **Type system changes** — implicit coercions, NULL propagation edge cases
- **Runtime behavior** — pushdown decisions that change observable ordering or type coercions

### Protocol — follow all three steps when introducing a breaking change:

1. Add a `// COMPAT_BREAK: x.y` comment at the exact line of change in the source
2. Add an entry to `BREAKING_CHANGES.md` at repo root (format: `version | category | description | migration path`)
3. Write a regression test proving old vs. new behavior — mark the test class with `[Trait("CompatBreak", "x.y")]`

For **parser syntax removals**: keep the old form parsing (with a lint warning emitted) for one minor version before removing it entirely.

### High-risk sites — apply extra scrutiny here:
- `ExpressionEvaluator.cs` — operator precedence, NULL propagation, implicit coercions
- `Evaluator.IsSqlPushdown()` — pushdown decisions change observable ordering/types
- `ExternalAggregateEngine`, `ExternalJoinEngine`, `ExternalWindowEngine`, `ExternalSortEngine` — spill behavior affects determinism
- `StatementParser.cs` — any removal of previously-valid syntax

**Do not add behavioral shims** ("run v1 semantics on a v3 engine"). Shims accumulate indefinitely and make the engine unmaintainable. The migration linter is the compatibility layer — build it when a major version ships with breaking changes, not before.

### 16.1 Versioning Rules

ETL-SQL strictly follows [Semantic Versioning 2.0.0](https://semver.org/). Agents must respect the following rules:

- **Pre-1.0.0 Releases (`0.y.z`):** The engine runtime is in active development. Breaking changes and syntax deprecations are allowed between minor versions (e.g., `v0.13.0` to `v0.14.0`) but MUST be logged in `BREAKING_CHANGES.md` and follow the deprecation process. Patch releases (e.g., `v0.14.1`) are reserved for bug fixes.
- **Post-1.0.0 Releases (`1.y.z` and beyond):** 
  - **Minor versions (`1.x.0`):** New features, connector additions, or enhancements. Must be strictly backwards-compatible; no breaking changes are permitted.
  - **Patch versions (`1.x.y`):** Backward-compatible bug fixes and security hotfixes only.
  - **Major versions (`x.0.0`):** Reserved for breaking changes, syntax removals, and paradigm shifts.

### 16.2 Branching Model

- **No direct commits to `main` or `dev`.**
- Active development happens on the **release branch for the version in flight** (e.g. `release/v0.19.0`). Branch features and fixes off that branch, not off `dev` or `main`, and open pull requests back into it.
- After a stable release is tagged, critical bug fixes and security hotfixes are applied (or cherry-picked) to that version's `release/vX.Y.Z` branch, shipped as a patch, and merged forward.
- Batch work into a single push. Every push to `main` or a `release/**` branch runs the full ~40-minute CI; back-to-back pushes queue the runners. Cancel superseded runs when the queue backs up.

### 16.3 Release Flow

Full procedure: **[release-checklist.md](./docs/releases/release-checklist.md)**. The mechanical sequence:

1. `scripts/Set-Version.ps1 -Version x.y.z` plus a hand-authored `CHANGELOG.md` entry.
2. `scripts/Test-PreRelease.ps1 -IncludeSlt -IncludeDockerIntegration` — the authoritative gate. Writes `release-validation/latest/state.json`; `-Explain` previews the plan, `-Resume` continues after fixing a failed phase.
3. `scripts/Master-Release.ps1 -Version x.y.z` — builds the VS Code UI, runs cross-platform packaging via `scripts/publish-release.ps1` (emits `release/`, `sha256sums.txt`, a CycloneDX `sbom.json`, and SLSA provenance), and builds the Windows MSI.
4. Tag and publish **manually**: push the branch, then `git tag vx.y.z && git push origin vx.y.z`. The tag push triggers `.github/workflows/release.yml`.

`.github/workflows/ci.yml` runs on pushes and PRs to `main` and `release/**` but does **not** cover the Docker integration or SLT lanes — a green CI is not a substitute for `Test-PreRelease`. Confirm CI is green before tagging: a few failures only reproduce there.

---

## 17. Syntax Consistency Rules

These rules apply when **adding new language syntax** (keywords, statements, or connection options) to the engine.

### Keyword tokens
All ETL-SQL keywords must be **UPPERCASE, underscore-separated**: `ENGINE_COMPAT`, `WEEK_START_DAY`, `SCRIPT_HASH_POLICY`, `REQUIRE`. Never CamelCase, never mixed-case tokens.

### When to add a keyword vs. a connection option
- **Add a keyword** when: the concept is part of core control flow — an ON/OFF toggle, a mode selection between fundamentally different behaviors
- **Add a connection option** when: it is a configuration value, connector-specific, or optional-by-default
- Never add a keyword for something that varies per-connector or is optional

### Statement naming convention
- AST record: `{Verb}{Noun}Statement` — `SetEngineCompatStatement`, `RequireVersionStatement`, `CreateConnectionStatement`
- Handler: `{Verb}{Noun}StatementHandler` — must match the record name exactly
- AST record properties: **PascalCase** — `TargetTable`, `ConnectionName`, `CompatVersion`, `IfNotExists`
- No abbreviations except universally understood ones (`Sql`, `Id`)

### Option value conventions
| Value type | Form | Example |
| :--- | :--- | :--- |
| Boolean | `ON`/`OFF` or `TRUE`/`FALSE` — pick one, never accept both silently | `POOLING = TRUE` |
| Numeric | Unquoted | `TIMEOUT_SECONDS = 30` |
| String | Single-quoted | `SERVER = 'localhost'` |
| Enum | UPPERCASE, normalized — never expose raw provider casing | `SSL_MODE = 'REQUIRED'` not `'Required'` |

---

## 18. Triage & Defect Principles — A Wrong Answer Outranks a Crash

All agents, contributors, and maintainers must strictly adhere to this core engineering rule:

- **A defect that returns a wrong answer is more serious than one that fails / crashes**, and must be treated as at least **P0** regardless of how narrow the trigger appears.
  - A crash is self-reporting: execution stops immediately, operators are notified, and nothing downstream consumes corrupt data.
  - A wrong answer / silent data corruption is silently consumed: written to destination databases, incorporated into reports, and acted upon by downstream business logic without warning.
- **Never trade a wrong answer for a crash.** Correct-but-crashing (e.g. silently declining a spill path and causing out-of-memory crashes on large datasets) is not a fix; it is merely substituting one defect for another.
- **Reproduction & Regressions:** When filing or fixing a data correctness bug, always create a deterministic reproducer that asserts directly on *behavior and output correctness*, not internal plan selection or memory thresholds.

---

## 19. Cross-Platform Line Endings & Pre-Push Validation

To prevent cross-platform CI failures and avoid wasting 1-hour GitHub Actions runs:

1. **Enable and Verify Repository Hooks**:
   - Once per clone, run `git config core.hooksPath scripts`. The same command works on every
     platform and requires no symlink or elevation.
   - Before the first commit, verify `git config --get core.hooksPath` returns `scripts`.
   - Do not bypass commit or push hooks unless the reported failure is proven unrelated to the staged
     change and the staged diff has been validated independently. Record the bypass and the unrelated
     failure in the handoff.
   - Hooks must inspect staged content for commit-specific checks. If a hook blocks a commit because
     of unrelated unstaged files, fix the hook's scope instead of formatting, staging, or modifying
     someone else's work.
2. **Shell Scripts (`.sh`) Must Use LF**:
   - Shell scripts (`*.sh`) and systemd units (`*.service`) must strictly use LF line endings (CRLF causes `bad interpreter` / bash syntax errors on Linux runners).
   - Normalize with: `node scripts/check-shell-line-endings.js --fix`
3. **Shared Report Runtime Assets Must Use LF**:
   - Always edit canonical assets in `src/ETL-SQL.ReportRuntime/Resources/Shared/` and run `node .\scripts\sync-assets.js` followed by `node .\scripts\sync-assets.js -Check`.
4. **C# Text/Doc Assertions Must Normalize Line Endings**:
   - When writing C# tests that read markdown or text files from disk via `File.ReadAllText()`, never assume `\r\n` or `\n`. Always use `.Replace("\r\n", "\n")` or regex `\r?\n` for cross-platform stability.
5. **Always Run Pre-Push Validation Locally**:
   - Before pushing to remote, always execute `.\scripts\Test-PrePush.ps1` (or verify via git pre-push hook). It verifies formatting, asset sync, syntax index sync, link coverage, flaky sleep patterns, test lane inventory, and fast contract tests in ~30 seconds.

---

*For a complete syntax walkthrough, start at [Getting Started](docs/guides/onboarding/getting-started.md), then use the [Syntax Index](docs/syntax-index.md) to find the focused reference page for each statement, function, or option.*
