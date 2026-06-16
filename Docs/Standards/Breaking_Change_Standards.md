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

## 2. Deprecation Protocol

When introducing a breaking change, developers must strictly adhere to the following three-step protocol:

1. **Inline Annotation**: Add a `// COMPAT_BREAK: x.y` comment directly above the modified line of code in the source, where `x.y` is the target release version.
2. **Attribution Log**: Register the breaking change in [BREAKING_CHANGES.md](../../BREAKING_CHANGES.md) at the repository root using this format:
   `version | category | description | migration path`
3. **Regression Testing**: Write a dedicated regression test proving the difference between the old and new behaviors. Mark the test class with the Xunit trait:
   `[Trait("CompatBreak", "x.y")]`

---

## 3. Grace Period for Syntax Removals

- **Deprecation Warning**: When removing a keyword or syntax, keep the old syntax parsing in `StatementParser.cs` but emit a static lint/LSP warning indicating the syntax is deprecated.
- **Removal**: The deprecated syntax must remain supported for a minimum of **one minor version** before being physically deleted from the parser codebase.

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
