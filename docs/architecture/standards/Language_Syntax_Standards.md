# Language Syntax Standards

This document establishes the official consistency rules and design patterns for adding or modifying language syntax (keywords, statements, options, or parser rules) in the **ETL-SQL** engine.

---

## 1. Keyword Casing and Formatting

- **Rule**: All ETL-SQL keyword tokens must be fully **UPPERCASE** and **underscore-separated** (snake_case).
- **Forbidden**: Never use mixed-case, CamelCase, or hyphenated keywords.
- **Examples**:
  - `ENGINE_COMPAT` (Correct)
  - `WEEK_START_DAY` (Correct)
  - `EngineCompat` (Incorrect)
  - `week-start-day` (Incorrect)

---

## 2. Keywords vs. Connector Options

When introducing a new configurable concept, decide whether to introduce a keyword or an option using these rules:

- **Add a Keyword** when:
  - The concept dictates core control flow (e.g. `IF`, `WHILE`, `TRY...CATCH`).
  - It defines a global engine toggle or mode selection (e.g. `SET PROFILING ON`).
  - It applies universally across all statements and connectors.
- **Add a Connector Option** (in `WITH(...)` or connector constructors) when:
  - The parameter is specific to a database or file provider (e.g., `TIMEOUT_SECONDS`, `CREDENTIAL_FILE`).
  - The setting is optional by default and does not affect the engine's core orchestration parser.
- **Golden Rule**: Never add a keyword for configuration values that vary per-connector or are optional by default.

---

## 3. Naming Conventions

### 3.1 AST Node Naming
- **Record Class**: Must follow the pattern `{Verb}{Noun}Statement` (e.g., `SetEngineCompatStatement`, `RequireVersionStatement`).
- **Handler Class**: Must follow the pattern `{Verb}{Noun}StatementHandler` and match the AST node name exactly.
- **Properties**: All properties on the record must use **PascalCase** (e.g. `TargetTable`, `ConnectionName`, `CompatVersion`, `IfNotExists`).
- **Abbreviations**: Avoid abbreviations unless they are universally accepted in the codebase (e.g., `Sql`, `Id`).

---

## 4. Option Value Conventions

When parsing options inside `WITH(...)` blocks or connection declarations, normalize values using these types:

| Option Type | Formatting Rules | Example |
| :--- | :--- | :--- |
| **Boolean** | Use `ON`/`OFF` or `TRUE`/`FALSE`. Pick one per statement/connector and reject the other; do not accept both silently. | `POOLING = TRUE` |
| **Numeric** | Unquoted integer or float. | `TIMEOUT_SECONDS = 30` |
| **String** | Single-quoted literal. | `SERVER = 'localhost'` |
| **Enum** | Single-quoted, UPPERCASE, and normalized to engine values. Never leak raw provider-specific casing. | `SSL_MODE = 'REQUIRED'` (Normalized) |

---

## 5. Parser Compatibility Rules

To ensure a stable developer experience, never break existing parser syntax rules without following the deprecation protocol:

- **Deprecation Grace Period**: When removing or changing a statement syntax, keep the old form parsing successfully but emit a lint warning (`LSP Warning`) alerting the user to migrate.
- **Warning Duration**: The deprecated syntax must remain supported for at least **one minor version** before it can be removed from the parser grammar.
- **No Behavioral Shims**: Do not implement multiple runtime execution paths for the same syntax (e.g., "run v1 semantics on a v3 engine"). Instead, write a static lint validator or migration assistant in the analyzer tier.

---

## References

- [Breaking Change Standards](Breaking_Change_Standards.md)
- [Grammar Guide](../../guides/onboarding/getting-started.md)
