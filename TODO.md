# ETL-SQL Development Roadmap
## VS Code issues

## Documentation

## Up Next
- [ ] **Security Manifest**: Strategy document for script signing.
- [ ] **Data Lake Connection brainstorm**: Strategy document complete.
- [ ] **Fresh Eyes Deep Code Architecture & Refactor Audit**
    - [ ] **De-bloat `Evaluator.cs`**: Extract concerns (Reporting, Metrics, Variable Scoping) to specialized services; current class is a "God Object" (60KB).
    - [ ] **Refactor `SelectStatementHandler.cs` (SRP Violation)**: Move CTE registration, Lineage tracking, and Pushdown logic to dedicated engines/helpers.
    - [ ] **Harden `CreateConnectionStatementHandler`**: Replace hardcoded `fileConnectors` list with interface-based capability detection for `ResolvePath` enforcement.
    - [ ] **Centralize Security Guardrails**: Move manual recursion and `IncrementOperationCount` logic in `DirectoryOperationStatementHandler` to a centralized file system security policy.
    - [ ] **Simplify `ExpressionEvaluator`**: Move ANSI string/date functions (`SUBSTRING`, `OVERLAY`, etc.) to `FunctionRegistry` and investigate performance of `ResolveIdentifierFallback` on wide rows.

---
