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
8. [Pull Request Checklist](#8-pull-request-checklist)
9. [Reporting a Bug](#9-reporting-a-bug)
10. [Feature Requests](#10-feature-requests)

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
git clone https://github.com/AmericanSuperstar/ETL-SQL.git
cd ETL-SQL
dotnet build
```

### Running the Test Suite

```bash
# All tests (unit + integration — Docker required for Testcontainers tests)
dotnet test

# Unit tests only (no Docker required)
dotnet test --filter "Category!=Integration"

# A single test project
dotnet test tests/ETL-SQL.Tests/ETL-SQL.Tests.csproj

# With coverage (requires coverlet)
dotnet test --collect:"XPlat Code Coverage"
```

### Running the Application

```bash
# Interactive console IDE
dotnet run --project src/ETL-SQL.App -- --ui edit samples/sample.etlsql

# Headless script execution
dotnet run --project src/ETL-SQL.App -- --run samples/sample.etlsql

# Interactive REPL (no file)
dotnet run --project src/ETL-SQL.App
```

### Recommended VS Code Extensions

- **C# Dev Kit** (`ms-dotnettools.csdevkit`)
- **ETL-SQL** (this project's own VS Code extension, in `src/etl-sql-vscode/`)

---

## 2. Project Structure

```
ETL-SQL/
├── src/
│   ├── ETL-SQL.Core/          # Parser, AST, interfaces, linting, crypto, security
│   ├── ETL-SQL.Engine/        # Evaluator, statement handlers, query pipeline
│   ├── ETL-SQL.Connectors/    # All IConnector / IDataSource implementations
│   ├── ETL-SQL.Orchestrator/  # ExecutionSession, SchedulerService, job history
│   ├── ETL-SQL.Orchestrator.Service/  # Windows Service / systemd host
│   ├── ETL-SQL.App/           # Primary CLI + TUI entry point
│   ├── ETL-SQL.ReportBuilder/ # Report-SQL compiler, DashboardService
│   ├── ETL-SQL.ReportBuilder.CLI/
│   ├── ETL-SQL.ReportPlayer/  # ASP.NET report server
│   └── etl-sql-vscode/        # VS Code language extension (TypeScript/Node)
├── tests/
│   └── ETL-SQL.Tests/         # xUnit test suite (unit + integration)
├── samples/                   # Sample .etlsql scripts (see Docs/Sample_Guide.md)
├── Docs/                      # Full documentation library
├── AGENTS.md                  # AI assistant instruction manual
├── CHANGELOG.md               # Version history
├── CONTRIBUTING.md            # This file
└── SECURITY.md                # Security policy
```

See [Docs/Architecture/Engine.md](Docs/Architecture/Engine.md) for the full project dependency graph and what each project owns.

---

## 3. Branching Model

| Branch | Purpose |
| :--- | :--- |
| `main` | Stable, releasable at any time. No direct commits. |
| `dev` | Active development. PRs merge here first. |
| `feature/<name>` | New features — branch from `dev`. |
| `fix/<name>` | Bug fixes — branch from `dev` (or `main` for hotfixes). |
| `docs/<name>` | Documentation-only changes — branch from `dev`. |

```bash
# Start a new feature
git checkout dev
git pull
git checkout -b feature/my-feature

# When ready, open a PR targeting dev
```

---

## 4. Making a Change

### Engine Code

Before touching the engine, read:
- **[Docs/Architecture/Engine.md](Docs/Architecture/Engine.md)** — dispatch loop, `#temp` scoping, pushdown logic
- **[Docs/Architecture/Connectors.md](Docs/Architecture/Connectors.md)** — connector interface contract and lifecycle
- **[AGENTS.md](AGENTS.md)** §8 — Engine Coding Principles (async, ILogger, record types)

**Key rules enforced by CI:**
- All AST nodes must be `record` types (not `class`) — enforces immutability for tree manipulation
- All I/O must use `Async` overloads with `CancellationToken` — no `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`
- Use injected `ILogger` from `IExecutionContext` — `Logger.Instance` is obsolete and will fail review
- All file paths must go through `IExecutionContext.ResolvePath()` — this is the Zero-Trust security boundary
- Connector-level exceptions must be caught and re-thrown as `ExecutionException` with a sanitized message

### New Connectors

See [Docs/Standards/Connectors_Standards.md](Docs/Standards/Connectors_Standards.md) for the full compliance checklist (25 items). The short version:
1. Implement `IConnector` and `IDataSource` (or `IDatabaseSource` for SQL engines)
2. Register in `DependencyInjectionSetup.cs`
3. Add a `GetSupportedOptions()` implementation documenting every `WITH()` key
4. Add connector documentation to [Docs/Reference/Data_Connectors.md](Docs/Reference/Data_Connectors.md)
5. Write integration tests with Testcontainers (SQL) or temp files (file connectors)

### Presentation / TUI

See [Docs/Standards/Presentation_Standards.md](Docs/Standards/Presentation_Standards.md) — the color system, layout rules, and error sanitization requirements.

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
| New syntax / keyword | [Docs/Reference/Grammar.md](Docs/Reference/Grammar.md) |
| New built-in function | [Docs/Reference/Standard_Library.md](Docs/Reference/Standard_Library.md) — signature, return type, and copy-pasteable example required |
| New connector or new `WITH()` option | [Docs/Reference/Data_Connectors.md](Docs/Reference/Data_Connectors.md) |
| New file/email/Docker operation | [Docs/Reference/Specialized_Operations.md](Docs/Reference/Specialized_Operations.md) |
| New connector implementation | [Docs/Architecture/Connectors.md](Docs/Architecture/Connectors.md) |
| Security behavior change | [SECURITY.md](SECURITY.md) |
| Breaking syntax change | [Docs/Migration_Guide.md](Docs/Migration_Guide.md) |
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

## 8. Pull Request Checklist

Before opening a PR, verify:

- [ ] `dotnet build` passes with zero warnings
- [ ] `dotnet test --filter "Category!=Integration"` passes (all unit tests green)
- [ ] New or changed behavior has test coverage
- [ ] No `.Result`, `.Wait()`, or `Logger.Instance` introduced
- [ ] All new file paths go through `ResolvePath()`
- [ ] All new AST nodes are `record` types
- [ ] Connector exceptions are caught and re-thrown as `ExecutionException`
- [ ] Documentation updated per Section 6 above
- [ ] `CHANGELOG.md` updated with your change under `[Unreleased]`
- [ ] PR description explains *why* the change is needed, not just *what* it does

---

## 9. Reporting a Bug

Open a GitHub Issue with:

1. **ETL-SQL version** (output of `ETL-SQL.exe --version`)
2. **OS and .NET version** (`dotnet --version`)
3. **Minimal reproduction** — the smallest `.etlsql` script that demonstrates the problem
4. **Expected behavior** vs. **Actual behavior**
5. **Error output** — full exception message and stack trace if applicable

For **security vulnerabilities**, do **not** open a public issue. See [SECURITY.md](SECURITY.md) for responsible disclosure instructions.

---

## 10. Feature Requests

Open a GitHub Issue with the **[Feature Request]** label. Include:

1. The use case — what are you trying to accomplish?
2. Current workaround (if any)
3. Proposed syntax (if it affects the ETL-SQL language)
4. Which connector(s) or subsystem(s) are affected

Large features (new connector types, new language constructs) will be discussed and may be tracked in a Strategy document under `Docs/Strategy/` before implementation begins.

---

*Questions? Open a Discussion on GitHub or email [etlsqlsoftware@gmail.com](mailto:etlsqlsoftware@gmail.com).*
