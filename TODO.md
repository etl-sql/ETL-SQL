# ETL-SQL Development Roadmap
## VS Code issues
- [ ] **On Error should go directly to the messages tab**  Currently just sits there and doesn't give you any indication of what happened until you start looking around and see a failure on the messages tab.
- [x] **Paths surrounded in "" the "" should be ignored** `ResolvePath` now strips surrounding double-quotes before resolution, covering all file operations engine-wide.
- [ ] **Sometimes when executing there is a serious lag**  First why is it so slow to execute?  Second no visual indicator that its working.  Third the execute button should be disabled until its done running.

## Documentation

## Up Next
- [x] **Credential Auto-Decryption Expansion** `decryptSensitive: true` applied to all credential-bearing handlers: CREATE/ALTER CONNECTION, BULK INSERT, ENCRYPT/DECRYPT FILE, ENCRYPT/DECRYPT DIRECTORY, CREATE SSH KEY PAIR.

- [x] **Version 0.7.0: Arrow Columnar Format — Phase A (SpillStore IPC)**
    - Strategy document complete: `Docs/Strategy/Arrow_Columnar_Strategy.md`
    - **Phase A implemented:** `ArrowSpillWriter`/`ArrowSpillReader` replace JSON-line spill in `SpillStore.cs`.
    - `CREATE COLUMNAR TABLE` syntax and full `DataTable` replacement (Phase B/C) explicitly deferred.
    - **`Security:SpillFormat`** config key added — `"Arrow"` (default).
        
- [ ] **Security Manifest**: Strategy document for script signing.
- [ ] **Data Lake Connection brainstorm**: Strategy document complete.
- [ ] **Fresh Eyes Deep Code Architecture & Refactor Audit**
    - [ ] **De-bloat `Evaluator.cs`**: Extract concerns (Reporting, Metrics, Variable Scoping) to specialized services; current class is a "God Object" (60KB).
    - [ ] **Refactor `SelectStatementHandler.cs` (SRP Violation)**: Move CTE registration, Lineage tracking, and Pushdown logic to dedicated engines/helpers.
    - [ ] **Harden `CreateConnectionStatementHandler`**: Replace hardcoded `fileConnectors` list with interface-based capability detection for `ResolvePath` enforcement.
    - [ ] **Centralize Security Guardrails**: Move manual recursion and `IncrementOperationCount` logic in `DirectoryOperationStatementHandler` to a centralized file system security policy.
    - [ ] **Simplify `ExpressionEvaluator`**: Move ANSI string/date functions (`SUBSTRING`, `OVERLAY`, etc.) to `FunctionRegistry` and investigate performance of `ResolveIdentifierFallback` on wide rows.
---