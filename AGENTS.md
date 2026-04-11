# ETL-SQL: AI Agent Instruction Manual

Welcome, Agent. You are assisting in the development and operation of **ETL-SQL**, a unique hybrid engine that executes SQL-like syntax against diverse data sources (SQL, NoSQL, FlatFiles) with an emphasis on portability and "Zero-Trust" security.

## 1. Dialect & Syntax (The "Mental Model")
ETL-SQL follows a T-SQL-like dialect but with specific extensions and restrictions:

- **Variables**: Use `@VariableName` for temporary storage.
- **Connection Strings**: Use `ENC:base64...` for encrypted strings.
- **Master Password**: Controlled via `USE PASSWORD '...'` or provided at the session level.
- **Wait/Polling**: Supports `WAITFOR TIME '00:00:30'` and `WAITFOR (SELECT ...)` polling.
- **Connectors**:
  - `FLATFILE`: Supports CSV, TSV, and Fixed-Width.
  - `JSON`/`XML`: Specialized flat-file handling.
  - `SQL`: MSSQL, MySQL, Oracle (See [Dialect Awareness](#3-dialect-awareness)).

## 2. Zero-Trust Security Guardrails
As an AI, you MUST NOT generate code that:
- Attempts to write to scripts (`.sql`, `.etlsql`, `.rptsql`). The engine will block these via the **Script Immutability Guardrail**.
- Accesses system directories (`C:\bin`, `/etc`, `.git`, etc.).
- Performs operations on the root of any drive.
- Exceeds 100 file operations or 5 levels of recursion without an explicit `### ALLOW_...` permission.

## 3. Dialect Awareness
Be aware that while ETL-SQL uses a unified syntax, it is **Dialect-Aware**.
- A standard keyword in T-SQL (like `TOP`) may be rejected by the Linter if the target connection is Postgres.
- Always check the `GetExcludedKeywords()` in the connector reference before generating complex queries.

## 4. Documentation Library Stewardship

The documentation is a high-fidelity knowledge base. When updating documents, you MUST adhere to these "Gold Standard" rules:

- **Standard Library**: Never add or modify a function without providing its full **Signature**, **Return Type**, and a **Copy-Pasteable Example**.
- **Data Connectors**: Always include common **Authentication Patterns** (e.g., Keyfile vs. Password) and **Security Patterns** for new connectors.
- **Cookbook**: Prefer **Self-Contained Lifecycles** (Extract -> Stage -> Merge -> Clean) over snippets.

---

© 2026 ETL-SQL Team. Built for speed, designed for clarity.

## 5. Coding Principles for Agents
- **Path Resolution**: Never use relative paths in code or scripts. Use the `ResolvePath` utility which ensures security validation occurs immediately.
- **Logger Obscurity**: `Logger.Instance` is obsolete. Always use the `ILogger` provided via dependency injection or the `IExecutionContext`.
- **Immutability**: Prefer `record` types for AST nodes.

---
*Refer to [Grammar.md](file:///c:/Users/chuck/scratch/ETL-SQL/Docs/Reference/Grammar.md) for full syntax specifications.*
