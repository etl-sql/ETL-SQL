# Breaking Change Standards

This document establishes the official protocol and safety guidelines for introducing any breaking changes to the **ETL-SQL** engine.

---

## 1. What Constitutes a Breaking Change?

A **breaking change** is any modification to the engine, compiler, parser, or standard library that could produce different results or failures for identical script inputs across releases. This includes:

- **Syntax Changes**: Renaming keywords, making optional parameters required, or changing parsing rules.
- **Semantic Changes**: Changing what the same statement does at runtime (the most dangerous, silent regressions).
- **Type System Changes**: Altering implicit type coercions, operator precedence, or NULL propagation rules.
- **Runtime Execution**: Changing pushdown query optimization decisions that alter output ordering or type formatting.

---

## 2. Deprecation and Compatibility Policy

ETL-SQL uses a strict deprecation policy for all language syntax, connector options, report options, CLI switches, and configuration keys:

- **No silent behavior changes**: A script or report that parses successfully in one stable release must not produce different results in the next stable release without an explicit compatibility note, test, and migration path.
- **Warn before removal**: Deprecated syntax and options must continue to parse and execute while emitting a lint/LSP warning and, when applicable, a runtime warning.
- **Minimum support window**: Deprecated syntax and options must remain supported for at least two minor releases after the first released warning. Removal earlier than that requires a security exception approved by maintainers and documented in the changelog.
- **Canonical replacement required**: Every deprecation must document the exact replacement syntax or option key in the reference docs and migration guide.
- **Machine-readable diagnostics**: Deprecated surfaces must have a stable diagnostic code so IDEs, CI, and migration tooling can detect them consistently.
- **No new aliases during freeze**: Once a release enters language freeze, new aliases or compatibility spellings are treated as syntax additions and require explicit maintainer approval.

When introducing a deprecation or breaking change, developers must strictly adhere to the following protocol:

1. **Inline Annotation**: Add a `// COMPAT_BREAK: x.y` or `// DEPRECATED_SYNTAX: x.y` comment directly above the modified line of code in the source, where `x.y` is the first release that warns.
2. **Attribution Log**: Register the change in [BREAKING_CHANGES.md](../../../BREAKING_CHANGES.md) at the repository root, including the diagnostic code and earliest removal version when the change is a deprecation.
3. **Reference Update**: Update the affected focused reference page, [Syntax Index](../../syntax-index.md), [Data Connectors](../../reference/connectors/README.md), [Standard Library](../../reference/functions/README.md), or the affected product guide in the same change.
4. **Regression Testing**: Write a dedicated compatibility test proving both the deprecated form and replacement behavior. Mark the test class with the Xunit trait:
   `[Trait("CompatBreak", "x.y")]`

---

## 3. Grace Period for Syntax Removals

- **Deprecation Warning**: When removing a keyword, syntax form, connector option, report option, CLI switch, or configuration key, keep the old form accepted but emit a static lint/LSP warning indicating the deprecation and replacement.
- **Removal**: The deprecated surface must remain supported for a minimum of **two minor versions** before being physically deleted from the parser, binder, connector option reader, report manifest builder, or CLI binder.
- **Removal Checklist**: Before removal, verify that compatibility diagnostics shipped, migration docs shipped, compatibility tests existed for the full warning window, and release notes called out the final removal.

---

## 4. High-Risk Components

Extra scrutiny and extensive integration testing are required when modifying these high-risk areas of the codebase:

- **`ExpressionEvaluator.cs`**: Resolves operator precedence, implicit coercions, and NULL propagation.
- **`Evaluator.IsSqlPushdown()`**: Governs which query clauses are pushed to remote database connections versus run in-memory.
- **Spill Engines (`ExternalJoinEngine`, `ExternalSortEngine`, `ExternalWindowEngine`)**: Handle overflow to disk; modifications here can compromise deterministic ordering.
- **`StatementParser.cs`**: Handles core grammar tokenization and AST generation.

---

## 5. No Behavioral Shims

- **Anti-Pattern**: Do not introduce conditional runtime checks or flags to execute legacy behaviors (e.g. `if (version == "v1") { ExecuteLegacy(); }`).
- **Rationale**: Shims quickly accumulate, bloat the runtime, and make long-term maintenance impossible. 
- **Solution**: The engine must remain clean and compile only the latest syntax and execution rules. Backwards compatibility is managed exclusively at the authoring/linting tier via the LSP migration helper.

---

## References

- [Language Syntax Standards](Language_Syntax_Standards.md)
- [Unit Testing Standards](SLT_Coverage.md)
