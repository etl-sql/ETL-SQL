# Contributing to ETL-SQL

Thank you for your interest in contributing! This document explains how to set up a development environment, the conventions we follow, and what the review process looks like.

---

## Table of Contents

1. [Development Environment](#1-development-environment)
2. [Project Structure](#2-project-structure)
3. [Branching Model](#3-branching-model)
4. [Making a Change](#4-making-a-change)
5. [Writing Tests](#5-writing-tests)
6. [Documentation Standards](#6-documentation-standards)
7. [AI-Assisted Development](#7-ai-assisted-development)
8. [Contribution Certification](#8-contribution-certification)
9. [Pull Request Checklist](#9-pull-request-checklist)
10. [Reporting a Bug](#10-reporting-a-bug)
11. [Feature Requests](#11-feature-requests)

---

## 1. Development Environment

### Prerequisites

| Tool | Version | Notes |
| :--- | :--- | :--- |
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0+** | Required |
| Git | Any recent | — |
| Docker (optional) | 20+ | Required for Testcontainers integration tests only |

### Setup

```bash
git clone https://github.com/etl-sql/ETL-SQL.git
cd ETL-SQL
git config core.hooksPath scripts
dotnet build
```

The repository hook runs `dotnet format` on staged C# files before each commit and re-stages formatter
output. If a staged C# file also has unstaged edits, the hook stops so unrelated local changes are not
swept into the commit.

### Running the Test Suite

```bash
# The default lane — what CI runs. Excludes Docker integration, long-running perf,
# the SLT corpus, the randomized fuzzer, and the Playwright browser lane.
dotnet test ETL-SQL.slnx --filter "Category!=Integration&Category!=Performance&Category!=SLT&Category!=Fuzz&Category!=Browser"

# A single test project — do this while iterating, not the solution-wide suite
dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj

# With coverage (requires the dotnet-reportgenerator local tool)
dotnet test ETL-SQL.slnx --collect:"XPlat Code Coverage" --results-directory ./coverage
```

The opt-in lanes (Docker integration, performance, SLT, fuzz, browser) and the `scripts/test-lane.ps1`
gateway are documented in [AGENTS.md §9](AGENTS.md#9-build-test--run). Run
`.\scripts\Test-PrePush.ps1` before pushing — it checks formatting, asset sync, syntax-index sync, link
coverage, and fast contract tests in about 30 seconds.

### Running the Application

```bash
# Interactive terminal IDE
dotnet run --project src/ETL-SQL.App -- ui edit samples/sample.etlsql

# Headless script execution
dotnet run --project src/ETL-SQL.App -- run samples/sample.etlsql

# Interactive REPL (no file)
dotnet run --project src/ETL-SQL.App
```

`run` and `ui` are verbs, not flags. `dotnet run --project src/ETL-SQL.App -- --help` lists the rest.

### Recommended VS Code Extensions

- **C# Dev Kit** (`ms-dotnettools.csdevkit`)
- **ETL-SQL** (this project's own VS Code extension, in `src/etl-sql-vscode/`)

---

## 2. Project Structure

```
ETL-SQL/
├── src/
│   ├── ETL-SQL.Core/          # Parser, AST, interfaces, shared language contracts
│   ├── ETL-SQL.Analysis/      # Lint, explain, diagnostics, grammar state engine
│   ├── ETL-SQL.Engine/        # Evaluator, statement handlers, query pipeline
│   ├── ETL-SQL.Connectors*/   # IConnector / IDataSource implementations, split per domain
│   ├── ETL-SQL.Infrastructure.*/  # Docker, logging, and local SQLite state
│   ├── ETL-SQL.Orchestrator/  # ExecutionSession, SchedulerService, job history
│   ├── ETL-SQL.Orchestrator.Service/  # Windows Service / systemd host
│   ├── ETL-SQL.Gateway/       # Outbound egress gateway
│   ├── ETL-SQL.LanguageServer/ # LSP implementation
│   ├── ETL-SQL.App/           # Primary CLI entry point
│   ├── ETL-SQL.TUI/           # Terminal IDE
│   ├── ETL-SQL.Reporting*/    # Report-SQL compilation and semantic contracts
│   ├── ETL-SQL.ReportBuilder*/ # Report authoring model and its CLI
│   ├── ETL-SQL.ReportRuntime/ # Canonical browser runtime assets (see AGENTS.md §12)
│   ├── ETL-SQL.ReportHosting/ # Reusable report session hosting
│   ├── ETL-SQL.ReportPlayer/  # Standalone ASP.NET report server
│   ├── ETL-SQL.Portal*/       # Dashboard server, catalog/admin API, EF migrations
│   └── etl-sql-vscode/        # VS Code language extension (TypeScript/Node)
├── tests/                     # xUnit suites, SLT corpus, fixtures, benchmarks
├── samples/                   # Sample .etlsql scripts (see docs/guides/patterns/sample-guide.md)
├── docs/                      # Full documentation library
├── scripts/                   # Build, test-lane, and release automation
├── tools/ui-sandbox/          # No-build harness for browser-side UI work
├── AGENTS.md                  # The agent standard — read before contributing
├── CHANGELOG.md               # Version history
├── CONTRIBUTING.md            # This file
└── SECURITY.md                # Security policy
```

Projects are strictly tiered and the layering is enforced by `ArchitectureBoundaryTests` against the
live `csproj` reference graph. See [AGENTS.md §10](AGENTS.md#10-engine-architecture-patterns) for the
tier table, and [docs/architecture/Engine.md](docs/architecture/Engine.md) for what each project owns.

---

## 3. Branching Model

Canonical rules live in [AGENTS.md §16.2](AGENTS.md#162-branching-model). The short version:

| Branch | Purpose |
| :--- | :--- |
| `main` | Stable and releasable at any time. No direct commits. |
| `release/v<version>` | The version in flight (e.g. `release/v0.19.0`). Active development happens here; merges to `main` at release time. |
| `feature/<name>` | New features — branch from the active `release/v<version>`. |
| `fix/<name>` | Bug fixes — branch from the active `release/v<version>` (or from `main` for a hotfix on a shipped release). |
| `docs/<name>` | Documentation-only changes — branch from the active `release/v<version>`. |

```bash
# Start a new feature targeting the version in flight
git checkout release/v0.19.0
git pull
git checkout -b feature/my-feature

# When ready, open a PR targeting release/v0.19.0
```

**Why `main` is protected.** Release tags are cut from `main`, and pushing a `vx.y.z` tag triggers
`release.yml`, which builds and publishes every platform artifact. `main` is therefore the branch
releases are published *from*, not just the newest stable code. It carries GitHub branch protection
requiring pull-request review, signed commits, and seven status checks — including Enterprise
Certification (Windows and Linux), the MSI in-place upgrade lane, and CodeQL. Several of those only
fail in CI and do not reproduce locally, so the PR gate is what keeps a red commit off `main` rather
than merely telling you about it afterwards.

Batch your work into one push. Every push to `main` or `release/**` runs the full ~40-minute CI, so
back-to-back pushes queue the runners; cancel superseded runs when the queue backs up.

---

## 4. Making a Change

### Engine Code

Before touching the engine, read:
- **[docs/architecture/Engine.md](docs/architecture/Engine.md)** — dispatch loop, `#temp` scoping, pushdown logic
- **[docs/architecture/Connectors.md](docs/architecture/Connectors.md)** — connector interface contract and lifecycle
- **[AGENTS.md](AGENTS.md)** §8 — Engine Coding Principles (async, ILogger, record types)

**Key rules enforced by CI:**
- All AST nodes must be `record` types (not `class`) — enforces immutability for tree manipulation
- All I/O must use `Async` overloads with `CancellationToken` — no `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`
- Use injected `ILogger` from `IExecutionContext` — `Logger.Instance` is obsolete and will fail review
- All file paths must go through `IExecutionContext.ResolvePath()` — this is the Zero-Trust security boundary
- Connector-level exceptions must be caught and re-thrown as `ExecutionException` with a sanitized message

### New Connectors

See [docs/architecture/standards/Connectors_Standards.md](docs/architecture/standards/Connectors_Standards.md) for the full compliance checklist (25 items). The short version:
1. Implement `IConnector` and `IDataSource` (or `IDatabaseSource` for SQL engines)
2. Register in `DependencyInjectionSetup.cs`
3. Add a `GetSupportedOptions()` implementation documenting every `WITH()` key
4. Add connector documentation to [docs/administration/platform/README.md](docs/administration/platform/README.md)
5. Write integration tests with Testcontainers (SQL) or temp files (file connectors)

### Presentation / TUI

See [docs/architecture/standards/Presentation_Standards.md](docs/architecture/standards/Presentation_Standards.md) — the color system, layout rules, and error sanitization requirements.

### Browser-side UI (portal / report runtime)

The portal and report-runtime UI is plain ES-module JavaScript + CSS (no build step). Shared components live in `src/ETL-SQL.ReportRuntime/Resources/Shared/designer/`; portal-specific UI modules live under `src/ETL-SQL.Portal/wwwroot/`.

**Develop and test these without Docker or the full portal** using the UI sandbox:

```powershell
pwsh -File tools\ui-sandbox\serve.ps1
```

It serves a Storybook-style harness (opens at `http://localhost:8099/tools/ui-sandbox/index.html`) that imports the canonical/source files directly, so edits show on **↻ Reload** — no sync, no portal build, no catalog DB. Pick a component "story" and a fixture from the sidebar; components that call APIs are driven by a mock fetch so no server is needed. See [`tools/ui-sandbox/README.md`](tools/ui-sandbox/README.md) for the story list and how to add one.

**Build & deploy notes:**

- The sandbox is **dev-only** — it is not part of any build or shipped artifact (safe to delete).
- After editing a **canonical** shared asset under `Resources/Shared/...`, run `scripts/sync-assets.ps1` so the host copies (`wwwroot`, VS Code `media`) match. CI fails if they drift — verify with `scripts/sync-assets.ps1 -Check`.
- To run a change in the real portal, build/run the `ETL-SQL.Portal` project as usual.

---

## 5. Writing Tests

Tests live in `tests/ETL-SQL.Tests/`. We use **xUnit** with **Testcontainers** for SQL integration tests.

### Test Categories

| Category | What they cover | Docker required |
| :--- | :--- | :--- |
| Unit | Parser, evaluator, standard functions, security | No |
| Integration | Connector round-trips, `BULK INSERT`, Docker lifecycle | Yes |
| Engine | Statement handler dispatch, `FOREACH`, `PARALLEL`, `MERGE` | No |
| Security | `SecurityService` sandbox, blocked paths, immutability | No |

### Conventions

```csharp
// Use the DI container from Program.ServiceProvider — not manual construction
var evaluator = Program.ServiceProvider.GetRequiredService<Evaluator>();

// Parse via the shared helper — don't call Parser directly in most tests
var script = TestHelpers.Parse("SELECT 1 AS x;");

// For security tests, assert the exact exception type
var ex = await Assert.ThrowsAsync<SecurityException>(() => eval.Evaluate(script));
Assert.Contains("protected system directory", ex.Message);

// Tag integration tests so they can be filtered out
[Fact, Trait("Category", "Integration")]
public async Task MyConnectorTest() { ... }
```

### Coverage Target

The current coverage baseline is **~70% lines / ~58% branches**. Do not submit a PR that significantly reduces coverage. New handlers and connectors must include tests.

---

## 6. Documentation Standards

When your change affects user-facing behavior, update the relevant docs:

| What changed | Update |
| :--- | :--- |
| New syntax / keyword | [docs/reference/statements/](docs/reference/statements/README.md) plus [docs/syntax-index.md](docs/syntax-index.md) |
| New built-in function | [docs/reference/functions/](docs/reference/functions/README.md) — signature, return type, and copy-pasteable example required |
| New connector or connector option | [docs/reference/connectors/](docs/reference/connectors/README.md) — include both authentication patterns |
| New file/email/Docker operation | [docs/reference/file-operations/](docs/reference/file-operations/README.md) |
| New connector implementation | [docs/architecture/Connectors.md](docs/architecture/Connectors.md) |
| Security behavior change | [SECURITY.md](SECURITY.md) |
| Breaking syntax change | [BREAKING_CHANGES.md](BREAKING_CHANGES.md) and [docs/guides/onboarding/migration-guide.md](docs/guides/onboarding/migration-guide.md) |
| User-facing code, docs, samples, scripts, or workflow changes | Add a `changelog.d/<feature>.md` fragment; the pre-release gate compiles it into [CHANGELOG.md](CHANGELOG.md) |
| New release | [CHANGELOG.md](CHANGELOG.md) — use Keep a Changelog format |

See [AGENTS.md](AGENTS.md) §7 for the complete documentation stewardship rules.

---

## 7. AI-Assisted Development

This project is developed with AI assistance. If you use an AI coding tool (Copilot, Claude, Gemini, etc.) to contribute, you **must** follow the rules in [AGENTS.md](AGENTS.md). Key requirements:

- **Never generate** scripts that write to `.etlsql` / `.sql` / `.py` files (Script Immutability Guardrail)
- **Never generate** scripts that access system directories or include plaintext credentials in output
- **Always validate** destructive operations (`DELETE`, `MERGE`, `TRUNCATE`) with `SET WHAT_IF ON` first
- **Always use** `ILogger` via DI — never `Logger.Instance` or `Console.WriteLine`
- **Always use** `record` types for new AST nodes

AI-generated engine code must pass the same review bar as human-written code. "AI wrote it" is not a justification for skipping the standards above.

---

## 8. Contribution Certification

ETL-SQL uses the [Developer Certificate of Origin 1.1](https://developercertificate.org/) instead of a
Contributor License Agreement. By adding a `Signed-off-by` line, you certify that you have the right to
submit the contribution under the project's Apache License 2.0 terms.

Sign each commit with:

```bash
git commit -s -m "Describe the change"
```

The sign-off must use your real name and an email address you control:

```text
Signed-off-by: Your Name <you@example.com>
```

## 9. Pull Request Checklist

Before opening a PR, verify:

- [ ] `dotnet build` passes with zero warnings
- [ ] `dotnet test --filter "Category!=Integration&Category!=Browser"` passes (all unit tests green)
- [ ] New or changed behavior has test coverage
- [ ] No `.Result`, `.Wait()`, or `Logger.Instance` introduced
- [ ] All new file paths go through `ResolvePath()`
- [ ] All new AST nodes are `record` types
- [ ] Connector exceptions are caught and re-thrown as `ExecutionException`
- [ ] Documentation updated per Section 6 above
- [ ] If syntax, keywords, or functions were added/modified, they meet the **Syntax Addition Checklist** (see below)
- [ ] Every commit includes a valid DCO `Signed-off-by` line
- [ ] PR description explains *why* the change is needed, not just *what* it does

### Syntax Addition Checklist

For contributions that introduce or modify language syntax, keywords, or functions:

- [ ] **Parser & Runtime**: Update the execution parser, immutable AST records, and runtime handlers or evaluators together; cover both accepted syntax and rejected or unsupported forms.
- [ ] **EBNF Reference**: Update the canonical [`docs/grammar.ebnf`](docs/grammar.ebnf) specification so it describes exactly what the execution parser accepts.
- [ ] **Documentation, Help & Snippets**: Update the syntax index and relevant guides, `docs/reference/` help pages, and `snippets/` templates, including a minimal working example for every new syntax form.
- [ ] **Lint & Autocomplete**: Register the new tokens or state transitions in `src/ETL-SQL.Analysis/Linting/Grammar/DefaultGrammar.cs` and update diagnostics so editor guidance agrees with the execution parser.
- [ ] **Connector Pushdown**: Add connector/dialect translation mappings where pushdown is supported and explicit unsupported-feature behavior where it is not.
- [ ] **Compatibility**: Check keyword, function-signature, and operator-precedence compatibility across supported dialects and document any intentional breaking change through the breaking-change process.
- [ ] **Regression Tests**: Add focused parser/runtime tests plus representative SqlLogicTests under `tests/slt_data/` for successful execution, boundary behavior, and rejection paths.

---

## 10. Reporting a Bug

Open a GitHub Issue with:

1. **ETL-SQL version** (output of `ETL-SQL.exe --version`)
2. **OS and .NET version** (`dotnet --version`)
3. **Minimal reproduction** — the smallest `.etlsql` script that demonstrates the problem
4. **Expected behavior** vs. **Actual behavior**
5. **Error output** — full exception message and stack trace if applicable

For **security vulnerabilities**, do **not** open a public issue. See [SECURITY.md](SECURITY.md) for responsible disclosure instructions.

---

## 11. Feature Requests

Open a GitHub Issue with the **[Feature Request]** label. Include:

1. The use case — what are you trying to accomplish?
2. Current workaround (if any)
3. Proposed syntax (if it affects the ETL-SQL language)
4. Which connector(s) or subsystem(s) are affected

Large features (new connector types, new language constructs) will be discussed and may be tracked in a Strategy document under `docs/architecture/roadmaps/` before implementation begins.

---

*Questions? Open a Discussion on GitHub or email [etlsqlsoftware@gmail.com](mailto:etlsqlsoftware@gmail.com).*
